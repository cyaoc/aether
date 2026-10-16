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
            Assert.Equal("", row.FindControl<TextBox>("UidInput")!.Text);
            Assert.True(page.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Assert.False(viewModel.HasRoomConnection);
            navigation.SelectedIndex = 5;
            GuiHarness.Flush();
            var general = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            general.FindControl<NumericUpDown>("RetentionInput")!.Text = "14";
            general.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            navigation.SelectedIndex = 2;
            GuiHarness.Flush();
            Assert.Same(row, Rows(page)[0]);
            Assert.Equal("", row.FindControl<TextBox>("UidInput")!.Text);
            Assert.Equal("填一个 UID，或删掉这条", row.FindControl<TextBlock>("ErrorMessage")!.Text);
            var settings = Settings.Load(harness.DataDirectory);
            Assert.Equal(14, settings.LogRetentionDays.Value);
            Assert.Empty(settings.WelcomeUids.Value);
            row.FindControl<TextBox>("UidInput")!.Text = "42";
            GuiHarness.Flush();
            page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            navigation.SelectedIndex = 4;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var input = page.FindControl<NumericUpDown>("SendIntervalInput")!;
            Assert.True(input.IsReadOnly);
            navigation.SelectedIndex = 0;
            File.WriteAllText(path, File.ReadAllText(path).Replace("interval_seconds: |-\n    7", "interval_seconds: 8"));
            navigation.SelectedIndex = 4;
            Assert.False(input.IsReadOnly);
            Assert.Equal("8", input.Text);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
            navigation.SelectedIndex = 0;
            File.WriteAllText(path, File.ReadAllText(path).Replace("interval_seconds: 8", "interval_seconds: |-\n    9"));
            navigation.SelectedIndex = 4;
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
            navigation.SelectedIndex = 4;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var input = page.FindControl<NumericUpDown>("SendIntervalInput")!;
            input.Text = "5";
            Dispatcher.UIThread.RunJobs();
            navigation.SelectedIndex = 0;
            File.WriteAllText(path, "send:\n  interval_seconds: 8\n");
            navigation.SelectedIndex = 4;
            Assert.Equal("8", input.Text);
            Assert.False(page.FindControl<Button>("SaveButton")!.IsEnabled);
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
            for (var index = 1; index <= 5; index++)
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
            Assert.Equal(6, navigation.ItemCount);
            Assert.Null(window.FindControl<Button>("SettingsButton"));
            var path = Settings.FilePath(harness.DataDirectory);
            var before = File.ReadAllText(path);
            navigation.SelectedIndex = 1;
            GuiHarness.Flush();
            var blindBox = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            var save = blindBox.FindControl<Button>("SaveButton")!;
            Assert.False(save.IsEnabled);
            blindBox.FindControl<ToggleSwitch>("BlindBoxInput")!.IsChecked = false;
            blindBox.FindControl<TextBox>("DailyKeywordInput")!.Text = "查今天";
            blindBox.FindControl<TextBox>("MonthlyKeywordInput")!.Text = "查本月";
            GuiHarness.Flush();
            Assert.True(save.IsEnabled);
            Assert.True(blindBox.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            Assert.Equal("开", window.FindControl<TextBlock>("BlindBoxStatus")!.Text);

            navigation.SelectedIndex = 2;
            GuiHarness.Flush();
            var welcome = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            Assert.Equal("关", window.FindControl<TextBlock>("WelcomeStatus")!.Text);
            welcome.FindControl<ToggleSwitch>("WelcomeInput")!.IsChecked = true;
            Assert.Equal("关", window.FindControl<TextBlock>("WelcomeStatus")!.Text);
            WelcomeTextList.Input(welcome).Text = "欢迎回家{昵称}";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SettingsSection.Welcome, Assert.IsType<SettingsPage>(welcome).Section);
            Assert.True(welcome.FindControl<Button>("SaveButton")!.IsEnabled,
                string.Join(" | ", welcome.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsVisible).Select(x => x.Text)));
            welcome.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            Assert.False(blindBox.FindControl<TextBlock>("DirtyMessage")!.IsVisible);
            Assert.Empty(window.OwnedWindows);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
