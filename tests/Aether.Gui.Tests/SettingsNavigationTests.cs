using Aether.Core;
using Aether.Gui.Tests.Support;
using static Aether.Gui.Tests.Support.ConditionList;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class SettingsNavigationTests
{
    [Fact]
    public Task An_unfilled_condition_survives_navigation_and_another_pages_save() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var navigation = window.FindControl<ListBox>("Navigation")!;
            navigation.SelectedIndex = 2;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var row = Add(page, WelcomeCondition.Uid);
            Assert.Equal("", GuiHarness.FindRowControl<TextBox>(page, row, "UidInput")!.Text);
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.IsVisible);
            Assert.True(GuiHarness.FindVisual<TextBlock>(page, "DirtyMessage").IsVisible);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Assert.False(viewModel.HasRoomConnection);
            navigation.Focus();
            navigation.SelectedIndex = 6;
            GuiHarness.Flush();
            var general = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            general.FindControl<NumericUpDown>("RetentionInput")!.Text = "14";
            GuiHarness.FindVisual<Button>(general, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            navigation.SelectedIndex = 2;
            GuiHarness.Flush();
            Assert.Same(row, Rows(page)[0]);
            Assert.Equal("", GuiHarness.FindRowControl<TextBox>(page, row, "UidInput")!.Text);
            Assert.Equal("填一个 UID，或删掉这条", GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage")!.Text);
            var settings = Settings.Load(harness.DataDirectory);
            Assert.Equal(14, settings.LogRetentionDays.Value);
            Assert.Empty(settings.WelcomeUids.Value);
            GuiHarness.FindRowControl<TextBox>(page, row, "UidInput")!.Text = "42";
            GuiHarness.Flush();
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([42L], Settings.Load(harness.DataDirectory).WelcomeUids.Value);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(1, "DailyKeywordInput")]
    [InlineData(1, "MonthlyKeywordInput")]
    [InlineData(2, "ValueInput")]
    public Task Enter_connects_only_from_the_room_input(int index, string field) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
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
            (index == 2 ? WelcomeTextList.Input(page) : page.FindControl<TextBox>(field)!).Focus();
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
        await harness.SaveCredentialAsync();
        Directory.CreateDirectory(harness.DataDirectory);
        var path = Settings.FilePath(harness.DataDirectory);
        File.WriteAllText(path, "send:\n  interval_seconds: |-\n    7\n");
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var navigation = window.FindControl<ListBox>("Navigation")!;
            navigation.SelectedIndex = 5;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var input = page.FindControl<NumericUpDown>("SendIntervalInput")!;
            Assert.True(input.IsReadOnly);
            navigation.SelectedIndex = 0;
            File.WriteAllText(path, File.ReadAllText(path).Replace("interval_seconds: |-\n    7", "interval_seconds: 8"));
            navigation.SelectedIndex = 5;
            Assert.False(input.IsReadOnly);
            Assert.Equal("8", input.Text);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            navigation.SelectedIndex = 0;
            File.WriteAllText(path, File.ReadAllText(path).Replace("interval_seconds: 8", "interval_seconds: |-\n    9"));
            navigation.SelectedIndex = 5;
            Assert.True(input.IsReadOnly);
            Assert.Equal("9", input.Text);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task A_draft_made_over_a_file_error_gives_way_to_the_fixed_file() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        Directory.CreateDirectory(harness.DataDirectory);
        var path = Settings.FilePath(harness.DataDirectory);
        File.WriteAllText(path, "send:\n  interval_seconds: 8\nlog:\n  level: @bad\n");
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var navigation = window.FindControl<ListBox>("Navigation")!;
            navigation.SelectedIndex = 5;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var input = page.FindControl<NumericUpDown>("SendIntervalInput")!;
            input.Text = "5";
            Dispatcher.UIThread.RunJobs();
            navigation.SelectedIndex = 0;
            File.WriteAllText(path, "send:\n  interval_seconds: 8\n");
            navigation.SelectedIndex = 5;
            Assert.Equal("8", input.Text);
            Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
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
        await harness.SaveCredentialAsync();
        Directory.CreateDirectory(harness.DataDirectory);
        var path = Settings.FilePath(harness.DataDirectory);
        File.WriteAllText(path, yaml);
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            foreach (var index in new[] { 1, 2, 3, 5, 6 })
            {
                window.FindControl<ListBox>("Navigation")!.SelectedIndex = index;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
                var error = GuiHarness.FindVisual<TextBlock>(page, "FileMessage");
                Assert.True(error.IsEffectivelyVisible);
                Assert.Contains(path, error.Text);
                Assert.Contains(expected, error.Text);
                Assert.False(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            }
            Assert.Equal(yaml, File.ReadAllText(path));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Navigation_keeps_drafts_and_saves_only_the_selected_page() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var navigation = window.FindControl<ListBox>("Navigation");
            Assert.NotNull(navigation);
            Assert.Equal(7, navigation.ItemCount);
            Assert.Equal(new[] { "弹幕", "盲盒统计", "进场欢迎", "欢迎动画", "抽奖", "发送队列", "通用设置" },
                navigation.Items.Cast<ListBoxItem>().Select(item => ((Grid)item.Content!).Children.OfType<TextBlock>().First().Text));
            Assert.Null(window.FindControl<Button>("SettingsButton"));
            var path = Settings.FilePath(harness.DataDirectory);
            var before = File.ReadAllText(path);
            navigation.SelectedIndex = 1;
            GuiHarness.Flush();
            var blindBox = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var save = GuiHarness.FindVisual<Button>(blindBox, "PART_SaveButton");
            Assert.False(save.IsEnabled);
            blindBox.FindControl<ToggleSwitch>("BlindBoxInput")!.IsChecked = false;
            blindBox.FindControl<TextBox>("DailyKeywordInput")!.Text = "查今天";
            blindBox.FindControl<TextBox>("MonthlyKeywordInput")!.Text = "查本月";
            GuiHarness.Flush();
            Assert.True(save.IsEnabled);
            Assert.True(GuiHarness.FindVisual<TextBlock>(blindBox, "DirtyMessage").IsVisible);
            Assert.Equal("开", window.FindControl<TextBlock>("BlindBoxStatus")!.Text);

            navigation.SelectedIndex = 2;
            GuiHarness.Flush();
            var welcome = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            Assert.Equal("关", window.FindControl<TextBlock>("WelcomeStatus")!.Text);
            welcome.FindControl<ToggleSwitch>("WelcomeInput")!.IsChecked = true;
            Assert.Equal("关", window.FindControl<TextBlock>("WelcomeStatus")!.Text);
            WelcomeTextList.Input(welcome).Text = "欢迎回家{昵称}";
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<WelcomeSettingsPage>(welcome);
            Assert.True(GuiHarness.FindVisual<Button>(welcome, "PART_SaveButton").IsEnabled,
                string.Join(" | ", welcome.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsVisible).Select(x => x.Text)));
            GuiHarness.FindVisual<Button>(welcome, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("欢迎回家{昵称}", Settings.Load(harness.DataDirectory).WelcomeTexts.Value[0]);
            Assert.Equal("开", window.FindControl<TextBlock>("WelcomeStatus")!.Text);
            Assert.Equal(new System.Text.RegularExpressions.Regex("enabled: false").Replace(before, "enabled: true", 1).Split('\n').Where(line => !line.StartsWith("  texts:")),
                File.ReadAllText(path).Split('\n').Where(line => !line.StartsWith("  texts:")));

            navigation.SelectedIndex = 1;
            GuiHarness.Flush();
            Assert.Same(blindBox, window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible));
            Assert.False(blindBox.FindControl<ToggleSwitch>("BlindBoxInput")!.IsChecked);
            Assert.Equal("查今天", blindBox.FindControl<TextBox>("DailyKeywordInput")!.Text);
            Assert.Equal("查本月", blindBox.FindControl<TextBox>("MonthlyKeywordInput")!.Text);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Settings.Load(harness.DataDirectory).BlindBoxEnabled.Value);
            Assert.Equal("查今天", Settings.Load(harness.DataDirectory).BlindBoxDailyKeyword.Value);
            Assert.Equal("查本月", Settings.Load(harness.DataDirectory).BlindBoxMonthlyKeyword.Value);
            Assert.Equal("关", window.FindControl<TextBlock>("BlindBoxStatus")!.Text);
            Assert.False(save.IsEnabled);
            Assert.False(GuiHarness.FindVisual<TextBlock>(blindBox, "DirtyMessage").IsVisible);
            Assert.Empty(window.OwnedWindows);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Save_message_stays_across_navigation_until_the_page_is_edited() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var navigation = window.FindControl<ListBox>("Navigation")!;
            navigation.SelectedIndex = 1;
            GuiHarness.Flush();
            var blindBox = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var input = blindBox.FindControl<TextBox>("DailyKeywordInput")!;
            input.Text = "查今天";
            GuiHarness.Flush();
            GuiHarness.FindVisual<Button>(blindBox, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var message = GuiHarness.FindVisual<TextBlock>(blindBox, "Message");
            Assert.Equal("已保存。", message.Text);
            // Each navigation rereads the settings file into every untouched page.
            navigation.SelectedIndex = 0;
            GuiHarness.Flush();
            navigation.SelectedIndex = 1;
            GuiHarness.Flush();
            Assert.Equal("已保存。", message.Text);
            input.Text = "查今日";
            GuiHarness.Flush();
            Assert.Equal("", message.Text);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
