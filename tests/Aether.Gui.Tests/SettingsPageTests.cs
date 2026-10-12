using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class SettingsPageTests
{
    [Theory]
    [InlineData("关闭")]
    [InlineData("舰长")]
    [InlineData("提督")]
    [InlineData("总督")]
    public Task Welcome_fields_display_and_save_only_changed_lines(string guard) =>
        WithSettingsPage(SettingsSection.Welcome, "# 保留\r\nwelcome:\r\n  enabled: false # 开关\r\n  text: '欢迎{昵称}' # 正文\r\n  max_length: 20 # 上限\r\n  uids: [42] # 老朋友\r\n  fan_club_level: 1 # 老粉\r\n  guard: 舰长 # 档位\r\n  wealth_level: 2 # 荣耀\r\nfuture: 保留\r\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var enabled = window.FindControl<CheckBox>("WelcomeInput");
            var text = window.FindControl<TextBox>("WelcomeTextInput");
            var uids = window.FindControl<TextBox>("WelcomeUidsInput");
            var fanClub = window.FindControl<NumericUpDown>("WelcomeFanClubInput");
            var threshold = window.FindControl<ComboBox>("WelcomeGuardInput");
            var wealth = window.FindControl<NumericUpDown>("WelcomeWealthInput");
            Assert.NotNull(enabled);
            Assert.NotNull(text);
            Assert.NotNull(uids);
            Assert.NotNull(fanClub);
            Assert.NotNull(threshold);
            Assert.NotNull(wealth);
            Assert.False(enabled.IsChecked);
            Assert.True(enabled.IsEnabled);
            Assert.Equal("下次连接直播间时生效", window.FindControl<TextBlock>("TimingHint")!.Text);
            Assert.Equal("欢迎{昵称}", text.Text);
            Assert.False(text.IsReadOnly);
            Assert.Equal(["42"], UidLabels(window));
            Assert.False(uids.IsReadOnly);
            Assert.Equal("1", fanClub.Text);
            Assert.Equal("2", wealth.Text);
            foreach (var number in new[] { fanClub, wealth })
            {
                Assert.False(number.IsReadOnly);
                Assert.True(number.AllowSpin);
            }
            Assert.Equal(new[] { "关闭", "舰长", "提督", "总督" }, threshold.Items.Cast<string>());
            Assert.Equal("舰长", threshold.SelectedItem);
            Assert.True(threshold.IsEnabled);

            enabled.IsChecked = true;
            text.Text = "{@}欢迎{大航海}{昵称}回家";
            SetUids(window, "2147483648", "9223372036854775807");
            fanClub.Text = "21";
            threshold.SelectedItem = guard;
            wealth.Text = "45";
            Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<Button>("SaveButton")!;
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: false # 开关", "enabled: true # 开关")
                .Replace("text: '欢迎{昵称}'", "text: '{@}欢迎{大航海}{昵称}回家'")
                .Replace("uids: [42]", "uids: [2147483648, 9223372036854775807]")
                .Replace("fan_club_level: 1", "fan_club_level: 21")
                .Replace("guard: 舰长", $"guard: {guard}")
                .Replace("wealth_level: 2", "wealth_level: 45"), File.ReadAllText(path));
            var settings = Settings.Load(Path.GetDirectoryName(path)!);
            Assert.Null(settings.Error);
            Assert.True(settings.WelcomeEnabled.Value);
            Assert.Equal("{@}欢迎{大航海}{昵称}回家", settings.WelcomeText.Value);
            Assert.Contains("welcome.max_length", settings.UnknownKeys);
            Assert.Equal([2147483648L, long.MaxValue], settings.WelcomeUids.Value);
            Assert.Equal(21, settings.WelcomeFanClubLevel.Value);
            Assert.Equal(guard, settings.WelcomeGuard.Value);
            Assert.Equal(45, settings.WelcomeWealthLevel.Value);
        });

    [Theory]
    [InlineData("欢迎{舰长}")]
    [InlineData("欢迎{@}")]
    [InlineData("欢迎{昵称")]
    [InlineData("欢迎}")]
    public Task Invalid_welcome_placeholders_disable_save_and_can_be_corrected(string text) =>
        WithSettingsPage(SettingsSection.Welcome, null, (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<TextBox>("WelcomeTextInput")!;
            input.Text = text;
            Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<Button>("SaveButton")!;
            Assert.False(save.IsEnabled);
            Assert.Contains("welcome.text", window.FindControl<TextBlock>("WelcomeTextError")!.Text);
            Assert.Contains("可用占位符为 {@}（只能放开头）、{昵称}、{大航海}", window.FindControl<TextBlock>("WelcomeTextError")!.Text);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before, File.ReadAllText(path));
            Assert.Equal(text, input.Text);

            input.Text = "{@}欢迎{{{昵称}}}";
            Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("{@}欢迎{{{昵称}}}", Settings.Load(Path.GetDirectoryName(path)!).WelcomeText.Value);
        });

    [Fact]
    public Task Long_welcome_text_can_be_saved_before_the_accounts_limit_is_known() =>
        WithSettingsPage(SettingsSection.Welcome, null, (window, path) =>
        {
            window.FindControl<TextBox>("WelcomeTextInput")!.Text = "一二三四五六七八九十百";
            Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<Button>("SaveButton")!;
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settings = Settings.Load(Path.GetDirectoryName(path)!);
            Assert.Null(settings.Error);
            Assert.Equal("一二三四五六七八九十百", settings.WelcomeText.Value);
        });

    [Fact]
    public Task Welcome_defaults_and_help_are_shown_and_missing_values_can_be_saved() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome:\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.False(window.FindControl<CheckBox>("WelcomeInput")!.IsChecked);
            Assert.Equal("欢迎{大航海}{昵称}", window.FindControl<TextBox>("WelcomeTextInput")!.Text);
            Assert.Null(window.FindControl<NumericUpDown>("WelcomeMaxLengthInput"));
            Assert.Empty(UidLabels(window));
            Assert.Equal("0", window.FindControl<NumericUpDown>("WelcomeFanClubInput")!.Text);
            Assert.Equal("舰长", window.FindControl<ComboBox>("WelcomeGuardInput")!.SelectedItem);
            Assert.Equal("0", window.FindControl<NumericUpDown>("WelcomeWealthInput")!.Text);
            var hint = window.FindControl<TextBlock>("WelcomeTextHint")!.Text;
            Assert.Contains("英文、数字、半角符号算 1，其他字符算 2", hint);
            Assert.Contains("上限自动读取账号的", hint);
            Assert.Contains("{@}（只能放开头）、{昵称}、{大航海}", hint);
            Assert.Contains("{{ 和 }}", hint);
            SetUids(window, "12345", "67890");
            Dispatcher.UIThread.RunJobs();
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var after = File.ReadAllText(path);
            Assert.Contains("  uids: [12345, 67890]\n", after);
            Assert.Equal(before, after.Replace("  uids: [12345, 67890]\n", ""));
            Assert.Equal([12345L, 67890L], Settings.Load(Path.GetDirectoryName(path)!).WelcomeUids.Value);
        });

    [Fact]
    public Task Flow_welcome_group_is_read_only_and_preserved_when_other_settings_are_saved() =>
        WithSettingsPage(SettingsSection.Welcome, "welcome: {enabled: true, text: '欢迎{昵称}', uids: [12345, 67890], fan_club_level: 21, guard: 提督, wealth_level: 45}\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.True(window.FindControl<CheckBox>("WelcomeInput")!.IsChecked);
            Assert.False(window.FindControl<CheckBox>("WelcomeInput")!.IsEnabled);
            Assert.Equal("欢迎{昵称}", window.FindControl<TextBox>("WelcomeTextInput")!.Text);
            Assert.True(window.FindControl<TextBox>("WelcomeTextInput")!.IsReadOnly);
            Assert.Equal(["12345", "67890"], UidLabels(window));
            Assert.True(window.FindControl<TextBox>("WelcomeUidsInput")!.IsReadOnly);
            Assert.Equal("提督", window.FindControl<ComboBox>("WelcomeGuardInput")!.SelectedItem);
            Assert.False(window.FindControl<ComboBox>("WelcomeGuardInput")!.IsEnabled);
            foreach (var (name, value) in new[] { ("WelcomeFanClub", "21"), ("WelcomeWealth", "45") })
            {
                var number = window.FindControl<NumericUpDown>(name + "Input")!;
                Assert.Equal(value, number.Text);
                Assert.True(number.IsReadOnly);
                Assert.False(number.AllowSpin);
            }
            foreach (var name in new[] { "Welcome", "WelcomeText", "WelcomeUids", "WelcomeFanClub", "WelcomeGuard", "WelcomeWealth" })
                Assert.True(window.FindControl<TextBlock>(name + "ReadOnly")!.IsVisible);
            var general = new SettingsPage(Path.GetDirectoryName(path)!, SettingsSection.General);
            general.FindControl<ComboBox>("LevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            Dispatcher.UIThread.RunJobs();
            var save = general.FindControl<Button>("SaveButton")!;
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("level: Information", "level: Warning"), File.ReadAllText(path));
        });

    [Theory]
    [InlineData("WelcomeFanClub", "fan_club_level", "-1")]
    [InlineData("WelcomeWealth", "wealth_level", "-1")]
    [InlineData("WelcomeUids", "uids", "12345, 67890")]
    [InlineData("WelcomeUids", "uids", "[0]")]
    [InlineData("WelcomeUids", "uids", "[9223372036854775808]")]
    public Task Invalid_welcome_values_disable_save_without_changing_the_file(string name, string key, string value) =>
        WithSettingsPage(SettingsSection.Welcome, null, (window, path) =>
        {
            var before = File.ReadAllText(path);
            var control = window.FindControl<Control>(name + "Input")!;
            var input = control is NumericUpDown number
                ? Assert.Single(number.GetVisualDescendants().OfType<TextBox>()) : Assert.IsType<TextBox>(control);
            input.Text = value;
            Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<Button>("SaveButton")!;
            Assert.False(save.IsEnabled);
            Assert.Contains("welcome." + key, window.FindControl<TextBlock>(name + "Error")!.Text);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before, File.ReadAllText(path));
        });

    [Theory]
    [InlineData(null)]
    [InlineData("  announce: true # 开关\n")]
    public Task Announcement_switch_defaults_on_and_saves_only_its_line(string? line) =>
        WithSettingsPage(SettingsSection.BlindBox, "blind_box:\n  enabled: true\n" + line, (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<CheckBox>("AnnounceInput");
            Assert.NotNull(input);
            Assert.True(input.IsChecked);
            Assert.True(input.IsEnabled);
            Assert.Equal("下次连接直播间时生效", window.FindControl<TextBlock>("TimingHint")!.Text);
            input.IsChecked = false;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(line is null ? before.Replace("  enabled: true\n", "  enabled: true\n  announce: false\n")
                : before.Replace("announce: true", "announce: false"), File.ReadAllText(path));
            Assert.False(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxAnnounce.Value);
        });

    [Fact]
    public Task Multiline_announcement_switch_is_read_only_and_preserved() =>
        WithSettingsPage(SettingsSection.BlindBox, "blind_box:\n  announce: |-\n    false\n  enabled: true\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<CheckBox>("AnnounceInput");
            Assert.NotNull(input);
            Assert.False(input.IsChecked);
            Assert.False(input.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("AnnounceReadOnly")!.IsVisible);
            window.FindControl<CheckBox>("BlindBoxInput")!.IsChecked = false;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: true", "enabled: false"), File.ReadAllText(path));
        });

    [Fact]
    public Task Blind_box_tally_and_send_queue_fields_load_defaults_validate_and_save_missing_settings() =>
        WithSettingsPage(SettingsSection.BlindBox, "# 保留\nblind_box:\n  enabled: true\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var keyword = window.FindControl<TextBox>("KeywordInput");
            var send = new SettingsPage(Path.GetDirectoryName(path)!, SettingsSection.Send);
            var interval = send.FindControl<NumericUpDown>("SendIntervalInput");
            Assert.NotNull(keyword);
            Assert.NotNull(interval);
            Assert.Equal("今日盲盒", keyword.Text);
            Assert.Equal("1", interval.Text);
            var save = window.FindControl<Button>("SaveButton")!;
            keyword.Text = " ";
            Dispatcher.UIThread.RunJobs();
            Assert.False(save.IsEnabled);
            Assert.Contains("blind_box.keyword", window.FindControl<TextBlock>("KeywordError")!.Text);
            keyword.Text = "查盲盒";
            Dispatcher.UIThread.RunJobs();
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: true\n", "enabled: true\n  keyword: 查盲盒\n"), File.ReadAllText(path));
            ((Window)TopLevel.GetTopLevel(window)!).Content = send;
            GuiHarness.Flush();
            save = send.FindControl<Button>("SaveButton")!;
            var numberText = Assert.Single(interval.GetVisualDescendants().OfType<TextBox>());
            numberText.Text = "1.5";
            Dispatcher.UIThread.RunJobs();
            Assert.False(save.IsEnabled);
            Assert.Contains("send.interval_seconds", send.FindControl<TextBlock>("SendIntervalError")!.Text);
            numberText.Text = "7";
            Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: true\n", "enabled: true\n  keyword: 查盲盒\n")
                .Replace("interval_seconds: 1", "interval_seconds: 7"), File.ReadAllText(path));
            var settings = Settings.Load(Path.GetDirectoryName(path)!);
            Assert.Equal("查盲盒", settings.BlindBoxKeyword.Value);
            Assert.Equal(7, settings.SendIntervalSeconds.Value);
        });

    [Fact]
    public Task Multiline_blind_box_tally_and_send_queue_fields_are_read_only_and_preserved() =>
        WithSettingsPage(SettingsSection.BlindBox, "blind_box:\n  keyword: |-\n    查盲盒\nsend:\n  interval_seconds: |-\n    7\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var keyword = window.FindControl<TextBox>("KeywordInput");
            var interval = window.FindControl<NumericUpDown>("SendIntervalInput");
            Assert.NotNull(keyword);
            Assert.NotNull(interval);
            Assert.True(keyword.IsReadOnly);
            Assert.Equal("查盲盒", keyword.Text);
            Assert.True(interval.IsReadOnly);
            Assert.False(interval.AllowSpin);
            Assert.True(window.FindControl<TextBlock>("KeywordReadOnly")!.IsVisible);
            Assert.True(window.FindControl<TextBlock>("SendIntervalReadOnly")!.IsVisible);
            window.FindControl<CheckBox>("BlindBoxInput")!.IsChecked = false;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("\nsend:", "\n  enabled: false\nsend:"), File.ReadAllText(path));
        });

    [Fact]
    public Task Multiline_blind_box_switch_is_read_only_and_preserved_when_saving() =>
        WithSettingsPage(SettingsSection.BlindBox, "blind_box:\n  enabled: |-\n    false\nlog:\n  level: Debug\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<CheckBox>("BlindBoxInput")!;
            Assert.False(input.IsChecked);
            Assert.False(input.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("BlindBoxReadOnly")!.IsVisible);
            var general = new SettingsPage(Path.GetDirectoryName(path)!, SettingsSection.General);
            general.FindControl<ComboBox>("LevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            general.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("level: Debug", "level: Warning"), File.ReadAllText(path));
        });

    [Fact]
    public Task Blind_box_switch_loads_changes_and_saves_without_touching_other_lines() =>
        WithSettingsPage(SettingsSection.BlindBox, "# 保留\nblind_box:\n  enabled: false # 开关\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            var input = window.FindControl<CheckBox>("BlindBoxInput");
            Assert.NotNull(input);
            Assert.False(input.IsChecked);
            input.IsChecked = true;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("enabled: false # 开关", "enabled: true # 开关"), File.ReadAllText(path));
            Assert.True(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxEnabled.Value);
            Assert.StartsWith("已保存", window.FindControl<TextBlock>("Message")!.Text);
            Assert.Equal("下次连接直播间时生效", window.FindControl<TextBlock>("TimingHint")!.Text);
            input.IsChecked = false;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Settings.Load(Path.GetDirectoryName(path)!).BlindBoxEnabled.Value);
        });

    [Fact]
    public Task Multiline_level_is_read_only() =>
        WithSettingsPage(SettingsSection.General, "log:\n  level: |-\n    Debug\n", (window, _) =>
        {
            Assert.False(window.FindControl<ComboBox>("LevelInput")!.IsEnabled);
            Assert.True(window.FindControl<TextBlock>("LevelReadOnly")!.IsVisible);
            Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Fact]
    public Task Save_rereads_external_changes_and_refreshes_the_form() =>
        WithSettingsPage(SettingsSection.General, null, (window, path) =>
        {
            File.WriteAllText(path, "# 外部修改\nlog:\n  retention_days: 30\n  level: Warning\n");
            window.FindControl<NumericUpDown>("RetentionInput")!.Text = "7";
            var save = window.FindControl<Button>("SaveButton")!;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, window.FindControl<ComboBox>("LevelInput")!.SelectedItem);
            var after = File.ReadAllText(path);
            Assert.StartsWith("# 外部修改\nlog:\n  retention_days: 7\n  level: Warning\n", after);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(after, File.ReadAllText(path));

            File.WriteAllText(path, "log:\n  level: @bad\n");
            window.FindControl<NumericUpDown>("RetentionInput")!.Text = "14";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(save.IsEnabled);
            Assert.Contains("第 2 行", window.FindControl<TextBlock>("FileMessage")!.Text);
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
        WithSettingsPage(SettingsSection.General, null, (window, _) =>
        {
            var number = window.FindControl<NumericUpDown>("RetentionInput")!;
            var textBox = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
            textBox.Text = input;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(Settings.Validate("log.retention_days", input)!, window.FindControl<TextBlock>("RetentionError")!.Text);
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
        WithSettingsPage(SettingsSection.General, yaml, (window, path) =>
        {
            var message = window.FindControl<TextBlock>("FileMessage")!;
            Assert.True(message.IsVisible);
            Assert.Contains(path, message.Text);
            Assert.Contains(expected, message.Text);
            Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled);
        });

    [Fact]
    public Task Multiline_settings_are_read_only_and_do_not_block_editing_other_settings() =>
        WithSettingsPage(SettingsSection.General, "log:\n  retention_days: |-\n    7\n  level: Debug\n", (window, path) =>
        {
            var before = File.ReadAllText(path);
            Assert.True(window.FindControl<NumericUpDown>("RetentionInput")!.IsReadOnly);
            Assert.False(window.FindControl<NumericUpDown>("RetentionInput")!.AllowSpin);
            Assert.True(window.FindControl<TextBlock>("RetentionReadOnly")!.IsVisible);
            window.FindControl<ComboBox>("LevelInput")!.SelectedItem = Microsoft.Extensions.Logging.LogLevel.Warning;
            window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.Replace("level: Debug", "level: Warning"), File.ReadAllText(path));
        });

    [Fact]
    public Task Main_window_shows_general_settings_and_saves_only_the_changed_line() =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            Directory.CreateDirectory(harness.DataDirectory);
            var path = Settings.FilePath(harness.DataDirectory);
            const string yaml = "# 我的设置\nlog:\n  retention_days: '7' # 保留\n  level: 'Debug' # 级别\n  future: yes\n";
            File.WriteAllText(path, yaml);
            var viewModel = harness.CreateViewModel();
            var main = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
            main.Show();
            try
            {
                main.FindControl<ListBox>("Navigation")!.SelectedIndex = 4;
                GuiHarness.Flush();
                var window = main.GetVisualDescendants().OfType<SettingsPage>().Single(page => page.IsEffectivelyVisible);
                Assert.Empty(main.OwnedWindows);
                var before = File.ReadAllText(path);
                Assert.Contains("原样保留", window.FindControl<TextBlock>("UnknownKeysMessage")!.Text);
                window.FindControl<NumericUpDown>("RetentionInput")!.Text = "14";
                Dispatcher.UIThread.RunJobs();
                var save = window.FindControl<Button>("SaveButton")!;
                Assert.True(save.IsEnabled);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(before.Replace("retention_days: '7'", "retention_days: 14"), File.ReadAllText(path));
                Assert.Equal(14, Settings.Load(harness.DataDirectory).LogRetentionDays.Value);

            }
            finally
            {
                await viewModel.ShutdownAsync();
                main.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return true;
        }, TestContext.Current.CancellationToken);

    private static IEnumerable<string?> UidLabels(SettingsPage page) => page.FindControl<WrapPanel>("UidTags")!
        .Children.OfType<Border>().Select(tag => ((StackPanel)tag.Child!).Children.OfType<TextBlock>().Single().Text);

    private static void SetUids(SettingsPage page, params string[] values)
    {
        var tags = page.FindControl<WrapPanel>("UidTags")!;
        foreach (var tag in tags.Children.ToArray())
            ((StackPanel)((Border)tag).Child!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var input = page.FindControl<TextBox>("WelcomeUidsInput")!;
        foreach (var value in values)
        {
            input.Text = value;
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        }
    }

    [Fact]
    public Task Welcome_uid_tags_validate_add_remove_and_save_a_single_line() => WithSettingsPage(SettingsSection.Welcome, null, (page, path) =>
    {
        var input = page.FindControl<TextBox>("WelcomeUidsInput")!;
        var tags = page.FindControl<WrapPanel>("UidTags");
        Assert.NotNull(tags);
        foreach (var invalid in new[] { "0", "-1", "abc", "1.5", "1, 2", "9223372036854775808" })
        {
            Enter(invalid);
            Assert.Empty(tags.Children);
            Assert.Contains("正整数", page.FindControl<TextBlock>("WelcomeUidsError")!.Text);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
        }
        Enter("42");
        Enter("42");
        Assert.Single(tags.Children);
        Enter("9223372036854775807");
        Enter("2147483648");
        Assert.Equal(3, tags.Children.Count);
        tags.Children[0].GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, tags.Children.Count);

        void Enter(string value)
        {
            input.Text = value;
            Dispatcher.UIThread.RunJobs();
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
            input.RaiseEvent(key);
            Assert.True(key.Handled);
            GuiHarness.Flush();
        }
        Assert.Equal([long.MaxValue, 2147483648L], Settings.Load(Path.GetDirectoryName(path)!).WelcomeUids.Value);
        Assert.Contains("  uids: [9223372036854775807, 2147483648]", File.ReadAllText(path));
    });

    [Fact]
    public Task Welcome_insert_buttons_use_caret_and_at_viewer_is_inserted_only_once() => WithSettingsPage(SettingsSection.Welcome, null, (page, path) =>
    {
        var text = page.FindControl<TextBox>("WelcomeTextInput")!;
        text.Text = "欢迎回家";
        text.CaretIndex = 2;
        Click("InsertNicknameButton");
        Assert.Equal("欢迎{昵称}回家", text.Text);
        Assert.Equal(6, text.CaretIndex);
        Click("InsertGuardButton");
        Assert.Equal("欢迎{昵称}{大航海}回家", text.Text);
        Click("InsertAtButton");
        Click("InsertAtButton");
        Assert.Equal("{@}欢迎{昵称}{大航海}回家", text.Text);

        void Click(string name)
        {
            var button = page.FindControl<Button>(name);
            Assert.NotNull(button);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    });

    [Fact]
    public Task Escaped_at_text_does_not_block_inserting_an_at_placeholder() => WithSettingsPage(SettingsSection.Welcome, null, (page, path) =>
    {
        var text = page.FindControl<TextBox>("WelcomeTextInput")!;
        text.Text = "欢迎{{@}}";
        var button = page.FindControl<Button>("InsertAtButton")!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("{@}欢迎{{@}}", text.Text);
        Assert.Null(Settings.Validate("welcome.text", text.Text));
    });

    private static Task WithSettingsPage(SettingsSection section, string? yaml, Action<SettingsPage, string> test) =>
        HeadlessApp.Session.Dispatch(() =>
        {
            using var harness = new GuiHarness();
            var path = Settings.FilePath(harness.DataDirectory);
            if (yaml is not null)
            {
                Directory.CreateDirectory(harness.DataDirectory);
                File.WriteAllText(path, yaml);
            }
            var page = new SettingsPage(harness.DataDirectory, section);
            var window = new Window { Content = page, Width = 736, Height = 640 };
            window.Show();
            try { test(page, path); }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);
}
