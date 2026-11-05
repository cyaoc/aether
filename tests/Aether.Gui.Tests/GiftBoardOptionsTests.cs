using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class GiftBoardOptionsTests
{
    [Fact]
    public Task The_navigation_lists_the_gift_board_after_the_gift_animation_and_says_when_it_is_recording() => OnMainWindow(async (h, vm, window) =>
    {
        var status = window.FindControl<TextBlock>("GiftBoardStatus")!;
        var gift = (ListBoxItem)window.Navigation.Items[3]!;
        Assert.Equal("送礼动画", ((Grid)gift.Content!).Children.OfType<TextBlock>().First().Text);
        Assert.Equal("礼物榜", ((Grid)((ListBoxItem)window.Navigation.Items[4]!).Content!).Children.OfType<TextBlock>().First().Text);
        window.Navigation.SelectedIndex = 4;
        GuiHarness.Flush();
        Assert.Single(window.GetVisualDescendants().OfType<GiftBoardPanel>(), panel => panel.IsEffectivelyVisible);
        Assert.False(status.IsEffectivelyVisible);

        GiftBoardScene.ConnectBoard(h, GiftBoardScene.RoomId);
        h.Client.GiftBoard.StartNewPeriod();
        await GuiHarness.WaitForAsync(() => status.IsEffectivelyVisible);
        Assert.Equal("统计中", status.Text);
        Assert.Contains("success", status.Classes);

        h.Client.GiftBoard.EndPeriod();
        await GuiHarness.WaitForAsync(() => !status.IsEffectivelyVisible);
    });

    [Fact]
    public Task Every_option_applies_at_once_and_stays_with_the_period() => GiftBoardScene.Run(async scene =>
    {
        var page = scene.Page;
        scene.Connect();
        scene.Start();
        await GuiHarness.WaitForAsync(() => page.FindControl<StackPanel>("Options")!.IsEffectivelyEnabled);
        Assert.Equal("选项改了立即生效，开始新一期时沿用", page.FindControl<TextBlock>("FooterText")!.Text);

        page.FindControl<NumericUpDown>("ThresholdInput")!.Text = "250";
        page.FindControl<RadioButton>("ByTotalPriceInput")!.IsChecked = true;
        page.FindControl<ToggleSwitch>("MascotInput")!.IsChecked = false;
        page.FindControl<Slider>("VolumeInput")!.Value = 25;
        var changed = new GiftBoardOptions(250, GiftBoardOptions.ByTotalPrice, false, 25);
        await GuiHarness.WaitForAsync(async () => (await scene.Board.CurrentAsync()).Options == changed);

        // The room left and joined again: the inputs show the 期's options, and a new 期 starts with them.
        scene.Disconnect();
        await GuiHarness.WaitForAsync(() => page.FindControl<NumericUpDown>("ThresholdInput")!.Text == "100");
        scene.Connect();
        await GuiHarness.WaitForAsync(() => page.FindControl<NumericUpDown>("ThresholdInput")!.Text == "250");
        Assert.True(page.FindControl<RadioButton>("ByTotalPriceInput")!.IsChecked);
        Assert.False(page.FindControl<ToggleSwitch>("MascotInput")!.IsChecked);
        Assert.Equal(25, page.FindControl<Slider>("VolumeInput")!.Value);
        scene.Board.EndPeriod();
        scene.Board.StartNewPeriod();
        Assert.Equal(changed, (await scene.Board.CurrentAsync()).Options);
    });

    [Fact]
    public Task Without_a_room_the_options_are_greyed_out_and_the_footer_says_why() => GiftBoardScene.Run(scene =>
    {
        GuiHarness.Flush();
        Assert.False(scene.Page.FindControl<StackPanel>("Options")!.IsEffectivelyEnabled);
        Assert.Equal("选项跟着直播间的期，连上直播间后才能改", scene.Page.FindControl<TextBlock>("FooterText")!.Text);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1.5")]
    [InlineData("")]
    public Task A_threshold_that_is_not_a_positive_integer_is_shown_wrong_and_not_applied(string text) => GiftBoardScene.Run(async scene =>
    {
        scene.Connect();
        scene.Start(threshold: 7);
        var input = scene.Page.FindControl<NumericUpDown>("ThresholdInput")!;
        var error = scene.Page.FindControl<TextBlock>("ThresholdError")!;
        await GuiHarness.WaitForAsync(() => input.Text == "7");

        input.Text = text;
        GuiHarness.Flush();
        Assert.True(error.IsVisible);
        Assert.Equal("上榜门槛必须是正整数。", error.Text);
        Assert.Contains("invalid", input.Classes);
        Assert.Equal(7, (await scene.Board.CurrentAsync()).Options.Threshold);

        input.Text = "1";
        await GuiHarness.WaitForAsync(async () => (await scene.Board.CurrentAsync()).Options.Threshold == 1);
        Assert.False(error.IsVisible);
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
        var page = window.GetVisualDescendants().OfType<GiftBoardPanel>().Single();
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
