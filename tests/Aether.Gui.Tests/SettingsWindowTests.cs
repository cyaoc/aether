using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class SettingsWindowTests
{
    [Fact]
    public Task Multiline_blind_box_switch_is_read_only_and_preserved_when_saving() =>
        WithSettingsWindow("blind_box:\n  enabled: |-\n    false\nlog:\n  level: Debug\n", (window, path) =>
        {
            var input = window.FindControl<CheckBox>("BlindBoxInput")!;
            Assert.False(input.IsChecked);
            Assert.False(input.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("BlindBoxReadOnly")!.IsVisible);
            window.FindControl<ComboBox>("LevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("blind_box:\n  enabled: |-\n    false\nlog:\n  level: Warning\n", File.ReadAllText(path));
        });

    [Fact]
    public Task Blind_box_switch_loads_changes_and_saves_without_touching_other_lines() =>
        WithSettingsWindow("# 保留\nblind_box:\n  enabled: false # 开关\n", (window, path) =>
        {
            var input = window.FindControl<CheckBox>("BlindBoxInput");
            Assert.NotNull(input);
            Assert.False(input.IsChecked);
            input.IsChecked = true;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("# 保留\nblind_box:\n  enabled: true # 开关\n", File.ReadAllText(path));
            Assert.True(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxEnabled.Value);
            Assert.StartsWith("已保存", window.FindControl<TextBlock>("Message")!.Text);
            Assert.Contains("下次观看直播间时生效", input.Content as string);
            input.IsChecked = false;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxEnabled.Value);
        });

    [Fact]
    public Task Multiline_level_is_read_only() =>
        WithSettingsWindow("log:\n  level: |-\n    Debug\n", (window, _) =>
        {
            Assert.False(window.FindControl<ComboBox>("LevelInput")!.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("LevelReadOnly")!.IsVisible);
            Assert.True(window.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Fact]
    public Task Save_rereads_external_changes_and_refreshes_the_form() =>
        WithSettingsWindow(null, (window, path) =>
        {
            File.WriteAllText(path, "# 外部修改\nlog:\n  retention_days: 30\n  level: Warning\n");
            window.FindControl<NumericUpDown>("RetentionInput")!.Text = "7";
            var save = window.FindControl<Button>("SaveButton")!;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, window.FindControl<ComboBox>("LevelInput")!.SelectedItem);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("# 外部修改\nlog:\n  retention_days: 7\n  level: Warning\n", File.ReadAllText(path));

            File.WriteAllText(path, "log:\n  level: @bad\n");
            window.FindControl<NumericUpDown>("RetentionInput")!.Text = "14";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(save.IsEnabled);
            Assert.Contains("第 2 行", window.FindControl<TextBlock>("Message")!.Text);
            Assert.Equal("log:\n  level: @bad\n", File.ReadAllText(path));
        });

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public Task Invalid_input_immediately_shows_core_validation_and_disables_save(string input) =>
        WithSettingsWindow(null, (window, _) =>
        {
            var number = window.FindControl<NumericUpDown>("RetentionInput")!;
            var textBox = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
            textBox.Text = input;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(Settings.Validate("log.retention_days", input)!, window.FindControl<TextBlock>("Message")!.Text);
            Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled);
            textBox.Text = "14";
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Theory]
    [InlineData("log:\n  level: @bad\n", "第 2 行")]
    [InlineData("log:\n  retention_days: 0\n", "log.retention_days")]
    [InlineData("log:\n  !!str level: Debug\n  level: Warning\n", "第 3 行（log.level）：设置名重复")]
    [InlineData("!!str log:\n  level: Debug\nlog:\n  retention_days: 7\n", "第 3 行（log）：分组名重复")]
    public Task File_errors_display_core_location_and_disable_save(string yaml, string expected) =>
        WithSettingsWindow(yaml, (window, path) =>
        {
            var message = window.FindControl<TextBlock>("Message")!;
            Assert.True(message.IsVisible);
            Assert.Contains(path, message.Text);
            Assert.Contains(expected, message.Text);
            Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Fact]
    public Task Multiline_settings_are_read_only_and_do_not_block_editing_other_settings() =>
        WithSettingsWindow("log:\n  retention_days: |-\n    7\n  level: Debug\n", (window, path) =>
        {
            Assert.True(window.FindControl<NumericUpDown>("RetentionInput")!.IsReadOnly);
            Assert.False(window.FindControl<NumericUpDown>("RetentionInput")!.AllowSpin);
            Assert.True(window.FindControl<TextBlock>("RetentionReadOnly")!.IsVisible);
            window.FindControl<ComboBox>("LevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("log:\n  retention_days: |-\n    7\n  level: Warning\n", File.ReadAllText(path));
        });

    [Fact]
    public Task Main_window_opens_settings_and_saves_only_the_changed_line() =>
        HeadlessApp.Session.Dispatch(() =>
        {
            using var harness = new GuiHarness();
            Directory.CreateDirectory(harness.DataDirectory);
            var path = Settings.FilePath(harness.DataDirectory);
            const string yaml = "# 我的设置\nlog:\n  retention_days: '7' # 保留\n  level: 'Debug' # 级别\n  future: yes\n";
            File.WriteAllText(path, yaml);
            var main = new MainWindow(harness.DataDirectory) { DataContext = harness.CreateViewModel() };
            main.Show();
            try
            {
                main.FindControl<Button>("SettingsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var window = Assert.IsType<SettingsWindow>(Assert.Single(main.OwnedWindows));
                Assert.Contains("原样保留", window.FindControl<TextBlock>("UnknownKeysMessage")!.Text);
                window.FindControl<NumericUpDown>("RetentionInput")!.Text = "14";
                Dispatcher.UIThread.RunJobs();
                var save = window.FindControl<Button>("SaveButton")!;
                Assert.True(save.IsEnabled);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("# 我的设置\nlog:\n  retention_days: 14 # 保留\n  level: 'Debug' # 级别\n  future: yes\n",
                    File.ReadAllText(path));
                Assert.Equal(14, Settings.Load(harness.DataDirectory).LogRetentionDays.Value);
                window.Close();
            }
            finally
            {
                main.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }, TestContext.Current.CancellationToken);

    /// <summary>Opens a settings window on a fresh data directory; writes aether.yml first unless yaml is null.</summary>
    private static Task WithSettingsWindow(string? yaml, Action<SettingsWindow, string> test) =>
        HeadlessApp.Session.Dispatch(() =>
        {
            using var harness = new GuiHarness();
            var path = Settings.FilePath(harness.DataDirectory);
            if (yaml is not null)
            {
                Directory.CreateDirectory(harness.DataDirectory);
                File.WriteAllText(path, yaml);
            }
            var window = new SettingsWindow(harness.DataDirectory);
            window.Show();
            try { test(window, path); }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
}
