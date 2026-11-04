using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class GiftBoardSettingsTests
{
    [Fact]
    public Task The_navigation_lists_the_gift_board_after_the_gift_animation_and_shows_its_saved_switch() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        await h.SaveCredentialAsync();
        var vm = h.CreateViewModel();
        var window = new MainWindow(h.DataDirectory) { DataContext = vm };
        window.Show();
        try
        {
            var status = window.FindControl<TextBlock>("GiftBoardStatus")!;
            var gift = (ListBoxItem)window.Navigation.Items[3]!;
            Assert.Equal("送礼动画", ((Grid)gift.Content!).Children.OfType<TextBlock>().First().Text);
            Assert.Equal("礼物榜", ((Grid)((ListBoxItem)window.Navigation.Items[4]!).Content!).Children.OfType<TextBlock>().First().Text);
            Assert.Equal("关", status.Text);

            window.Navigation.SelectedIndex = 4;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            Assert.IsType<GiftBoardSettingsPage>(page);
            page.FindControl<ToggleSwitch>("EnabledInput")!.IsChecked = true;
            GuiHarness.Flush();
            Assert.Equal("关", status.Text);
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.PendingSave;
            Assert.Equal("开", status.Text);
        }
        finally { await vm.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Every_input_is_saved_and_shown_again_when_the_page_is_reopened() => OnPage(async (h, vm, page) =>
    {
        Assert.True(page.FindControl<RadioButton>("ByUnitPriceInput")!.IsChecked);
        Assert.Equal(60, page.FindControl<Slider>("VolumeInput")!.Value);

        page.FindControl<ToggleSwitch>("EnabledInput")!.IsChecked = true;
        page.FindControl<NumericUpDown>("ThresholdInput")!.Text = "250";
        page.FindControl<RadioButton>("ByTotalPriceInput")!.IsChecked = true;
        page.FindControl<ToggleSwitch>("ResetOnLiveInput")!.IsChecked = false;
        page.FindControl<ToggleSwitch>("MascotInput")!.IsChecked = false;
        page.FindControl<Slider>("VolumeInput")!.Value = 25;
        await Save(page);

        var saved = Settings.Load(h.DataDirectory);
        Assert.Equal((true, 250, Settings.GiftBoardByTotalPrice, false, false, 25),
            (saved.GiftBoardEnabled.Value, saved.GiftBoardThreshold.Value, saved.GiftBoardThresholdBy.Value,
                saved.GiftBoardResetOnLive.Value, saved.GiftBoardMascot.Value, saved.GiftBoardVolume.Value));
        var reopened = new GiftBoardSettingsPage(h.DataDirectory);
        Assert.Equal("250", reopened.FindControl<NumericUpDown>("ThresholdInput")!.Text);
        Assert.False(reopened.FindControl<RadioButton>("ByUnitPriceInput")!.IsChecked);
        Assert.True(reopened.FindControl<RadioButton>("ByTotalPriceInput")!.IsChecked);
        Assert.Equal(25, reopened.FindControl<Slider>("VolumeInput")!.Value);
    });

    [Fact]
    public Task Saving_takes_effect_at_once_without_asking_for_a_reconnect() => OnPage(async (h, vm, page) =>
    {
        vm.ConnectionState = RoomConnectionState.Connected;
        page.FindControl<ToggleSwitch>("MascotInput")!.IsChecked = false;
        page.FindControl<Slider>("VolumeInput")!.Value = 25;
        await Save(page);

        Assert.Equal("保存后立即生效", page.TimingHint);
        Assert.False(page.ShowReconnectNotice);
        var state = await h.Client.GiftBoard.CurrentAsync();
        Assert.Equal((false, 25), (state.Mascot, state.Volume));
    });

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1.5")]
    [InlineData("")]
    public Task A_threshold_that_is_not_a_positive_integer_cannot_be_saved(string text) => OnPage((h, vm, page) =>
    {
        var input = page.FindControl<NumericUpDown>("ThresholdInput")!;
        var error = page.FindControl<TextBlock>("ThresholdError")!;
        input.Text = text;
        GuiHarness.Flush();
        Assert.False(page.CanSave);
        Assert.True(error.IsVisible);
        Assert.Contains("必须为正整数", error.Text);
        Assert.Contains("invalid", input.Classes);

        input.Text = "1";
        GuiHarness.Flush();
        Assert.True(page.CanSave);
        Assert.False(error.IsVisible);
        return Task.CompletedTask;
    });

    /// <summary>The page in a window of the minimum size, over a data directory whose web service takes a port of its own.</summary>
    private static Task OnPage(Func<GuiHarness, MainWindowViewModel, GiftBoardSettingsPage, Task> check) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["web_service.port"] = GuiHarness.FreePort().ToString() });
        var vm = h.CreateViewModel();
        var page = new GiftBoardSettingsPage(h.DataDirectory) { DataContext = vm };
        var window = new Window { Content = page, Width = 1040, Height = 600 };
        window.Show();
        try { await check(h, vm, page); }
        finally { await vm.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static async Task Save(SettingsPage page)
    {
        GuiHarness.Flush();
        GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await page.PendingSave;
    }
}
