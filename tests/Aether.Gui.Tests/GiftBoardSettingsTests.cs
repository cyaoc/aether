using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class GiftBoardSettingsTests
{
    [Fact]
    public Task The_navigation_lists_the_gift_board_after_the_gift_animation_and_shows_its_saved_switch() => OnMainWindow(async (h, vm, window) =>
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
        await GuiHarness.SaveAsync(page);
        Assert.Equal("开", status.Text);
    });

    [Fact]
    public Task Every_input_is_saved_and_shown_again_when_the_page_is_reopened() => GiftBoardScene.Run(async scene =>
    {
        var page = scene.Page;
        Assert.True(page.FindControl<RadioButton>("ByUnitPriceInput")!.IsChecked);
        Assert.Equal(60, page.FindControl<Slider>("VolumeInput")!.Value);

        page.FindControl<ToggleSwitch>("EnabledInput")!.IsChecked = true;
        page.FindControl<NumericUpDown>("ThresholdInput")!.Text = "250";
        page.FindControl<RadioButton>("ByTotalPriceInput")!.IsChecked = true;
        page.FindControl<ToggleSwitch>("ResetOnLiveInput")!.IsChecked = false;
        page.FindControl<ToggleSwitch>("MascotInput")!.IsChecked = false;
        page.FindControl<Slider>("VolumeInput")!.Value = 25;
        await GuiHarness.SaveAsync(scene.Page);

        var saved = Settings.Load(scene.DataDirectory);
        Assert.Equal((true, 250, Settings.GiftBoardByTotalPrice, false, false, 25),
            (saved.GiftBoardEnabled.Value, saved.GiftBoardThreshold.Value, saved.GiftBoardThresholdBy.Value,
                saved.GiftBoardResetOnLive.Value, saved.GiftBoardMascot.Value, saved.GiftBoardVolume.Value));
        var reopened = new GiftBoardSettingsPage(scene.DataDirectory);
        Assert.Equal("250", reopened.FindControl<NumericUpDown>("ThresholdInput")!.Text);
        Assert.False(reopened.FindControl<RadioButton>("ByUnitPriceInput")!.IsChecked);
        Assert.True(reopened.FindControl<RadioButton>("ByTotalPriceInput")!.IsChecked);
        Assert.Equal(25, reopened.FindControl<Slider>("VolumeInput")!.Value);
    });

    [Fact]
    public Task Saving_takes_effect_at_once_without_asking_for_a_reconnect() => GiftBoardScene.Run(async scene =>
    {
        scene.ViewModel.ConnectionState = RoomConnectionState.Connected;
        scene.Page.FindControl<ToggleSwitch>("MascotInput")!.IsChecked = false;
        scene.Page.FindControl<Slider>("VolumeInput")!.Value = 25;
        await GuiHarness.SaveAsync(scene.Page);

        Assert.Equal("保存后立即生效", scene.Page.TimingHint);
        Assert.False(scene.Page.ShowReconnectNotice);
        var state = await scene.Board.CurrentAsync();
        Assert.Equal((false, 25), (state.Mascot, state.Volume));
    });

    [Fact]
    public Task A_setting_edited_in_the_file_reaches_the_board_when_a_page_is_next_shown() => OnMainWindow(async (h, vm, window) =>
    {
        await GuiHarness.WaitForAsync(() => vm.WebService.WebServiceStatus.Running);
        Assert.Equal(60, (await h.Client.GiftBoard.CurrentAsync()).Volume);

        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["gift_board.volume"] = "10" });
        window.Navigation.SelectedIndex = 4;
        await GuiHarness.WaitForAsync(() => !vm.WebService.AnimationReloading);
        Assert.Equal(10, (await h.Client.GiftBoard.CurrentAsync()).Volume);
    });

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1.5")]
    [InlineData("")]
    public Task A_threshold_that_is_not_a_positive_integer_cannot_be_saved(string text) => GiftBoardScene.Run(scene =>
    {
        var input = scene.Page.FindControl<NumericUpDown>("ThresholdInput")!;
        var error = scene.Page.FindControl<TextBlock>("ThresholdError")!;
        input.Text = text;
        GuiHarness.Flush();
        Assert.False(scene.Page.CanSave);
        Assert.True(error.IsVisible);
        Assert.Contains("必须为正整数", error.Text);
        Assert.Contains("invalid", input.Classes);

        input.Text = "1";
        GuiHarness.Flush();
        Assert.True(scene.Page.CanSave);
        Assert.False(error.IsVisible);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true, 1, null, "礼物榜页：已有 1 个推流软件连着", null)]
    [InlineData(true, 0, null, "礼物榜页：还没有推流软件连着", "在直播姬里添加浏览器素材，地址和宽高照通用设置里“礼物榜页”那一行填；已经加过的，右键素材 → 刷新。")]
    [InlineData(false, 0, "端口 17890 被别的程序占用", "网页服务没打开：端口 17890 被别的程序占用，礼物榜页显示不了", null)]
    public Task The_status_bar_tells_how_the_gift_board_page_is_connected(bool running, int connections, string? error, string text, string? steps) => GiftBoardScene.Run(scene =>
    {
        // The video and lottery pages have nine connections each, which this page must not count as its own.
        scene.ViewModel.WebService.Apply(new WebServiceStatus(running, 17890, 9, 9, connections, error));
        GuiHarness.Flush();
        Assert.Equal(text, GuiHarness.FindVisual<TextBlock>(scene.Page, "PageStatusText").Text);
        var help = GuiHarness.FindVisual<TextBlock>(scene.Page, "PageSetupSteps");
        Assert.Equal(steps is not null, help.IsEffectivelyVisible);
        Assert.Equal(steps ?? "", help.Text ?? "");
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_status_bar_sends_the_streamer_to_the_general_settings_for_the_address_and_size() => OnMainWindow((h, vm, window) =>
    {
        window.Navigation.SelectedIndex = 4;
        GuiHarness.Flush();
        var page = window.GetVisualDescendants().OfType<GiftBoardSettingsPage>().Single();
        GuiHarness.FindVisual<Button>(page, "OpenGeneralSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(7, window.Navigation.SelectedIndex);
        return Task.CompletedTask;
    });

    /// <summary>The main window over a data directory whose web service has a port of its own.</summary>
    private static Task OnMainWindow(Func<GuiHarness, MainWindowViewModel, MainWindow, Task> check) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        await h.SaveCredentialAsync();
        h.SaveSettings();
        var vm = h.CreateViewModel();
        var window = new MainWindow(h.DataDirectory) { DataContext = vm };
        window.Show();
        try { await check(h, vm, window); }
        finally { await vm.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
