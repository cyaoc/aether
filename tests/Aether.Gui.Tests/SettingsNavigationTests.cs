using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class SettingsNavigationTests
{
    [Theory]
    [InlineData(1, "KeywordInput")]
    [InlineData(2, "WelcomeTextInput")]
    public Task Enter_connects_only_from_the_room_input(int index, string field) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var requested = new TaskCompletionSource();
        harness.RoomResponse = async (_, token) =>
        {
            requested.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        var viewModel = harness.CreateViewModel();
        viewModel.RoomNumber = "6";
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            window.FindControl<ListBox>("Navigation")!.SelectedIndex = index;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            page.FindControl<TextBox>(field)!.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            GuiHarness.Flush();
            Assert.False(requested.Task.IsCompleted);
            window.FindControl<StackPanel>("RoomInputPanel")!.GetVisualDescendants().OfType<TextBox>().Single().Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Returning_to_an_unchanged_page_rereads_external_values_and_read_only_state() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        Directory.CreateDirectory(harness.DataDirectory);
        var path = Settings.FilePath(harness.DataDirectory);
        File.WriteAllText(path, "send:\n  interval_seconds: |-\n    7\n");
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var navigation = window.FindControl<ListBox>("Navigation")!;
            navigation.SelectedIndex = 3;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var input = page.FindControl<NumericUpDown>("SendIntervalInput")!;
            Assert.True(input.IsReadOnly);
            navigation.SelectedIndex = 0;
            File.WriteAllText(path, File.ReadAllText(path).Replace("interval_seconds: |-\n    7", "interval_seconds: 8"));
            navigation.SelectedIndex = 3;
            Assert.False(input.IsReadOnly);
            Assert.Equal("8", input.Text);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            navigation.SelectedIndex = 0;
            File.WriteAllText(path, File.ReadAllText(path).Replace("interval_seconds: 8", "interval_seconds: |-\n    9"));
            navigation.SelectedIndex = 3;
            Assert.True(input.IsReadOnly);
            Assert.Equal("9", input.Text);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("log:\n  level: @bad\n", "第 2 行")]
    [InlineData("welcome:\n  uids:\n    - 42\n", "welcome.uids")]
    public Task Every_page_shows_file_errors_and_disables_save(string yaml, string expected) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        Directory.CreateDirectory(harness.DataDirectory);
        var path = Settings.FilePath(harness.DataDirectory);
        File.WriteAllText(path, yaml);
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            for (var index = 1; index <= 4; index++)
            {
                window.FindControl<ListBox>("Navigation")!.SelectedIndex = index;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
                var error = page.FindControl<TextBlock>("FileMessage")!;
                Assert.True(error.IsEffectivelyVisible);
                Assert.Contains(path, error.Text);
                Assert.Contains(expected, error.Text);
                Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            }
            Assert.Equal(yaml, File.ReadAllText(path));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Welcome_uid_tags_validate_add_remove_and_save_a_single_line() => WithWelcomePage(page =>
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
    }, settings => Assert.Equal([long.MaxValue, 2147483648L], settings.WelcomeUids.Value));

    [Fact]
    public Task Welcome_insert_buttons_use_caret_and_at_viewer_is_inserted_only_once() => WithWelcomePage(page =>
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
    public Task Escaped_at_text_does_not_block_inserting_an_at_placeholder() => WithWelcomePage(page =>
    {
        var text = page.FindControl<TextBox>("WelcomeTextInput")!;
        text.Text = "欢迎{{@}}";
        var button = page.FindControl<Button>("InsertAtButton")!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("{@}欢迎{{@}}", text.Text);
        Assert.Null(Settings.Validate("welcome.text", text.Text));
    });

    private static Task WithWelcomePage(Action<SettingsPage> test, Action<Settings>? saved = null) =>
        HeadlessApp.Session.Dispatch(() =>
        {
            using var harness = new GuiHarness();
            var page = new SettingsPage(harness.DataDirectory, SettingsSection.Welcome);
            var window = new Window { Content = page, Width = 736, Height = 640 };
            window.Show();
            try
            {
                test(page);
                if (saved is not null)
                {
                    saved(Settings.Load(harness.DataDirectory));
                    Assert.Contains("  uids: [9223372036854775807, 2147483648]", File.ReadAllText(Settings.FilePath(harness.DataDirectory)));
                }
            }
            finally { window.Close(); }
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Navigation_keeps_drafts_and_saves_only_the_selected_page() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var navigation = window.FindControl<ListBox>("Navigation");
            Assert.NotNull(navigation);
            Assert.Equal(5, navigation.ItemCount);
            Assert.Null(window.FindControl<Button>("SettingsButton"));
            var path = Settings.FilePath(harness.DataDirectory);
            var before = File.ReadAllText(path);
            navigation.SelectedIndex = 1;
            GuiHarness.Flush();
            var blindBox = window.GetVisualDescendants().OfType<UserControl>().Single(x => x.IsEffectivelyVisible);
            var save = blindBox.FindControl<Button>("SaveButton")!;
            Assert.False(save.IsEnabled);
            blindBox.FindControl<CheckBox>("BlindBoxInput")!.IsChecked = false;
            Assert.True(save.IsEnabled);
            Assert.True(blindBox.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            Assert.Equal("开", window.FindControl<TextBlock>("BlindBoxStatus")!.Text);

            navigation.SelectedIndex = 2;
            GuiHarness.Flush();
            var welcome = window.GetVisualDescendants().OfType<UserControl>().Single(x => x.IsEffectivelyVisible);
            Assert.Equal("关", window.FindControl<TextBlock>("WelcomeStatus")!.Text);
            welcome.FindControl<CheckBox>("WelcomeInput")!.IsChecked = true;
            Assert.Equal("关", window.FindControl<TextBlock>("WelcomeStatus")!.Text);
            welcome.FindControl<TextBox>("WelcomeTextInput")!.Text = "欢迎回家{昵称}";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SettingsSection.Welcome, Assert.IsType<SettingsPage>(welcome).Section);
            Assert.True(welcome.FindControl<Button>("SaveButton")!.IsEnabled,
                string.Join(" | ", welcome.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsVisible).Select(x => x.Text)));
            welcome.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("欢迎回家{昵称}", Settings.Load(harness.DataDirectory).WelcomeText.Value);
            Assert.Equal("开", window.FindControl<TextBlock>("WelcomeStatus")!.Text);
            Assert.Equal(before.Replace("enabled: false", "enabled: true").Split('\n').Where(line => !line.StartsWith("  text:")),
                File.ReadAllText(path).Split('\n').Where(line => !line.StartsWith("  text:")));

            navigation.SelectedIndex = 1;
            GuiHarness.Flush();
            Assert.Same(blindBox, window.GetVisualDescendants().OfType<UserControl>().Single(x => x.IsEffectivelyVisible));
            Assert.False(blindBox.FindControl<CheckBox>("BlindBoxInput")!.IsChecked);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Settings.Load(harness.DataDirectory).BlindBoxEnabled.Value);
            Assert.Equal("关", window.FindControl<TextBlock>("BlindBoxStatus")!.Text);
            Assert.False(save.IsEnabled);
            Assert.False(blindBox.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            Assert.Empty(window.OwnedWindows);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
