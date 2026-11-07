using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Microsoft.Extensions.Time.Testing;
using static Aether.Gui.Tests.Support.GiftBoardScene;

namespace Aether.Gui.Tests;

/// <summary>The streamer stopping on purpose (断开, closing Aether, logging out) ends a running 期, so the next connection does not
/// put the last 一场直播's board back on the stream; a reconnect to apply settings does not (ADR 0014). The room connection is
/// held at room_init, and the board is connected the way the room connection would.</summary>
public sealed class GiftBoardLeavingTests
{
    [Theory]
    [InlineData("断开", true)]
    [InlineData("关窗口", true)]
    [InlineData("退出登录", true)]
    [InlineData("重新连接", false)]
    public Task Stopping_on_purpose_ends_the_running_period_and_a_reconnect_does_not(string action, bool ends) =>
        Run(async (h, clock, viewModel, window) =>
        {
            await ConnectAsync(h, viewModel);
            var started = clock.GetUtcNow();
            h.Client.GiftBoard.StartNewPeriod(RoomId);
            await GuiHarness.WaitForAsync(() => viewModel.GiftBoard.Running);
            clock.Advance(TimeSpan.FromHours(3));

            // Still on its way when the streamer stops: it is written into the 期 before it ends.
            viewModel.GiftBoard.Threshold = 333;
            await (action switch
            {
                "断开" => ClickDisconnectAsync(viewModel, window),
                "关窗口" => viewModel.ShutdownAsync(),
                "退出登录" => LogOutAsync(h, viewModel),
                _ => viewModel.ReconnectCommand.ExecuteAsync(null),
            });

            await GuiHarness.WaitForAsync(async () => (await h.Client.GiftBoard.CurrentAsync()).Options.Threshold == 333);
            Assert.Equal(new GiftBoardPeriod(started, ends ? clock.GetUtcNow() : null), (await h.Client.GiftBoard.CurrentAsync()).Period);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Leaving_a_room_whose_period_is_not_running_leaves_it_as_it_is(bool ended) => Run(async (h, clock, viewModel, window) =>
    {
        await ConnectAsync(h, viewModel);
        if (ended)
        {
            h.Client.GiftBoard.StartNewPeriod(RoomId);
            h.Client.GiftBoard.EndPeriod(RoomId);
        }
        var before = await h.Client.GiftBoard.CurrentAsync();
        await GuiHarness.WaitForAsync(() => viewModel.GiftBoard.Phase == before.Phase);
        clock.Advance(TimeSpan.FromHours(3));

        await ClickDisconnectAsync(viewModel, window);

        Assert.Equal(RoomConnectionState.Disconnected, viewModel.ConnectionState);
        Assert.Equal(before.Period, (await h.Client.GiftBoard.CurrentAsync()).Period);
    });

    // The web service stops first, and its failing does not keep the 期 running, the room connected or the window open.
    // Stopping the web service refreshes what its rows show, and that refresh now fails.
    [Fact]
    public Task A_closing_step_that_fails_skips_none_after_it_and_the_window_still_closes() =>
        ClosesDespite(viewModel => viewModel.WebService.PropertyChanged += (_, _) => throw new InvalidOperationException("网页服务出错"));

    // Closing first refreshes the buttons, before any step; a refresh that fails skips none of the steps either. It fails once:
    // the connect command refreshes them again itself when its run ends.
    [Fact]
    public Task A_closing_action_before_the_steps_that_fails_skips_none_of_them() => ClosesDespite(viewModel =>
    {
        var failed = false;
        viewModel.ConnectCommand.CanExecuteChanged += (_, _) =>
        {
            if (failed) return;
            failed = true;
            throw new InvalidOperationException("按钮刷新出错");
        };
    });

    private static Task ClosesDespite(Action<MainWindowViewModel> fail) => Run(async (h, clock, viewModel, window) =>
    {
        await ConnectAsync(h, viewModel);
        h.Client.GiftBoard.StartNewPeriod(RoomId);
        await GuiHarness.WaitForAsync(() => viewModel.GiftBoard.Running);
        fail(viewModel);

        window.Close();
        await GuiHarness.WaitForAsync(() => !window.IsVisible);

        Assert.Equal(RoomConnectionState.Disconnected, viewModel.ConnectionState);
        Assert.Equal(GiftBoardPhase.Ended, (await h.Client.GiftBoard.CurrentAsync()).Phase);
    });

    // Whether to end is decided once the writes before it are done: a 期 the streamer started just before stopping, before the
    // panel heard of it, ends too.
    [Fact]
    public Task A_period_started_just_before_stopping_ends_too() => Run(async (h, clock, viewModel, window) =>
    {
        await ConnectAsync(h, viewModel);
        var starting = viewModel.GiftBoard.AskNewPeriodAsync(RoomId.ToString());
        var leaving = viewModel.GiftBoard.EndBeforeLeavingAsync();
        await Task.WhenAll(starting, leaving);

        Assert.Equal(GiftBoardPhase.Ended, (await h.Client.GiftBoard.CurrentAsync()).Phase);
    });

    /// <summary>Connects the GUI to <see cref="GiftBoardScene.RoomId"/>, held at room_init, and the board to the same room.</summary>
    private static async Task ConnectAsync(GuiHarness h, MainWindowViewModel viewModel)
    {
        var requested = h.HoldRoomRequest();
        viewModel.RoomNumber = RoomId.ToString();
        viewModel.ConnectCommand.Execute(null);
        await requested.WaitAsync(TimeSpan.FromSeconds(5));
        ConnectBoard(h, RoomId);
        await GuiHarness.WaitForAsync(() => viewModel.GiftBoard.Connected);
    }

    private static async Task ClickDisconnectAsync(MainWindowViewModel viewModel, MainWindow window)
    {
        GuiHarness.Flush();
        var button = GuiHarness.FindVisual<Button>(window, "DisconnectButton");
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(button)).Invoke();
        await viewModel.DisconnectCommand.ExecutionTask!;
        await GuiHarness.WaitForAsync(() => viewModel.ConnectionState == RoomConnectionState.Disconnected);
    }

    // B站 is not asked to log out in tests, and the login page that follows waits for its QR code.
    private static async Task LogOutAsync(GuiHarness h, MainWindowViewModel viewModel)
    {
        h.HoldLoginRequests();
        await viewModel.LogoutCommand.ExecuteAsync(null);
        Assert.Equal("https://passport.bilibili.com/login/exit/v2", Assert.Single(h.RejectedRequests).AbsoluteUri);
        h.RejectedRequests.Clear();
    }

    /// <summary>The main window over a data directory whose web service has a port of its own, with a saved account and the
    /// scene's clock.</summary>
    private static Task Run(Func<GuiHarness, FakeTimeProvider, MainWindowViewModel, MainWindow, Task> check) => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider(Today);
        using var h = new GuiHarness(clock);
        await h.SaveCredentialAsync();
        h.SaveSettings();
        var viewModel = h.CreateViewModel(clock);
        var window = new MainWindow(h.DataDirectory) { DataContext = viewModel };
        window.Show();
        try { await check(h, clock, viewModel, window); }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
