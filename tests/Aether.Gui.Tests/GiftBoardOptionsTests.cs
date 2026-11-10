using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

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
        await h.Client.GiftBoard.StartNewPeriodAsync(GiftBoardScene.RoomId);
        await GuiHarness.WaitForAsync(() => status.IsEffectivelyVisible);
        Assert.Equal("统计中", status.Text);
        Assert.Contains("success", status.Classes);

        await h.Client.GiftBoard.EndPeriodAsync(GiftBoardScene.RoomId);
        await GuiHarness.WaitForAsync(() => !status.IsEffectivelyVisible);
    });

    [Fact]
    public Task Every_option_applies_at_once_and_stays_with_the_period() => GiftBoardScene.Run(async scene =>
    {
        var page = scene.Page;
        scene.Connect();
        await scene.StartAsync();
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
        await scene.Board.EndPeriodAsync(GiftBoardScene.RoomId);
        await scene.Board.StartNewPeriodAsync(GiftBoardScene.RoomId);
        Assert.Equal(changed, (await scene.Board.CurrentAsync()).Options);
    });

    // The options belong to the room's 期: once the panel hears of another room it shows that room's, even while a write for
    // the room before still waits its turn, so changing one of them leaves the others as that room has them.
    [Fact]
    public Task After_a_room_switch_changing_one_option_keeps_the_new_rooms_others() => GiftBoardScene.Run(async scene =>
    {
        const long other = 24680;
        scene.Board.Connect(other);
        await scene.Board.StartNewPeriodAsync(other);
        var otherState = await scene.Board.CurrentAsync();
        scene.Board.EndRoom(other);
        scene.Connect();
        await scene.StartAsync();
        await GuiHarness.WaitForAsync(() => scene.Page.FindControl<StackPanel>("Options")!.IsEffectivelyEnabled);

        // In one go, so the write for this room has not come back when the panel hears of the other one.
        var board = scene.ViewModel.GiftBoard;
        board.Threshold = 333;
        scene.Board.EndRoom(GiftBoardScene.RoomId);
        scene.Board.Connect(other);
        board.Apply(otherState);
        Assert.Equal<decimal?>(GiftBoardOptions.Default.Threshold, board.Threshold);
        board.Volume = 30;

        await GuiHarness.WaitForAsync(async () => (await scene.Board.CurrentAsync()).Options.Volume == 30);
        Assert.Equal(GiftBoardOptions.Default with { Volume = 30 }, (await scene.Board.CurrentAsync()).Options);
    });

    // A write still on its way when the same room is connected again can be reported by Core before the panel sees it come
    // back, after the reconnect showed the threshold saved before it: once it is back the inputs show what Core saved, so
    // changing one option does not put the others back. Core's states take a detour through the thread pool, so this panel
    // is told them by the test, in that order; the writes still go to Core.
    [Fact]
    public Task After_reconnecting_the_same_room_changing_one_option_keeps_what_a_waiting_write_saved() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var core = h.Client.GiftBoard;
        GiftBoardScene.ConnectBoard(h, GiftBoardScene.RoomId);
        await core.StartNewPeriodAsync(GiftBoardScene.RoomId);
        var saved = await core.CurrentAsync();
        var board = new GiftBoardViewModel(h.Client, NullLogger.Instance, TimeProvider.System, TestContext.Current.CancellationToken);
        board.Apply(saved);

        // The database is busy, so the threshold is still on its way.
        using var database = new SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = "BEGIN IMMEDIATE";
        command.ExecuteNonQuery();
        board.Threshold = 333;
        board.Apply(new GiftBoardState([], GiftBoardOptions.Default, null, null));
        board.Apply(saved);
        board.Apply(saved with { Options = saved.Options with { Threshold = 333 } });
        Assert.Equal<decimal?>(saved.Options.Threshold, board.Threshold);
        command.CommandText = "ROLLBACK";
        command.ExecuteNonQuery();

        await GuiHarness.WaitForAsync(() => board.Threshold == 333);
        board.Volume = 30;
        await h.IdleAsync();
        Assert.Equal(GiftBoardOptions.Default with { Threshold = 333, Volume = 30 }, (await core.CurrentAsync()).Options);
        return true;
    }, TestContext.Current.CancellationToken);

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
        await scene.StartAsync(threshold: 7);
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
        Assert.Equal(8, window.Navigation.SelectedIndex);
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
