using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryPreconditionTests
{
    private static readonly PoolPrize[] Prizes = [new("抱枕", 1, Grade.Grand)];
    private static readonly LotteryAward[] Awards = [new("一等奖", "抱枕", 1, Grade.Grand)];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposed_controller_rejects_pool_and_entry_operations(bool stop)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.StartAsync(Prizes, "停");
        var before = lottery.State;
        lottery.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop ? lottery.StopAsync() : lottery.CancelAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lottery.StartEntryAsync(Awards, "抽奖"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(lottery.CloseEntriesAsync);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lottery.DrawEntryAsync(0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(lottery.EndEntryAsync);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lottery.SetEntryHiddenAsync(true));
        Assert.Equal(before, lottery.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconnecting_blocks_starts_but_allows_stopping_or_cancelling_the_pool(bool stop)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.StartAsync(Prizes, "停");
        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        await h.Client.IdleAsync();
        Assert.False(lottery.State.Connected);
        Assert.True(lottery.State.CanControl);
        var before = lottery.State;
        Assert.Equal("请先连接直播间。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.NewAsync(LotteryMode.Pool))).Message);
        Assert.Equal("请先连接直播间。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartAsync(Prizes, "停"))).Message);
        Assert.Equal("请先连接直播间。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartEntryAsync(Awards, "抽奖"))).Message);
        Assert.Equal("请选择抽奖方式。", (await Assert.ThrowsAsync<ArgumentException>(() => lottery.NewAsync((LotteryMode)99))).Message);
        Assert.Equal("口令不能为空", (await Assert.ThrowsAsync<ArgumentException>(() => lottery.StartAsync(Prizes, " "))).Message);
        Assert.Equal("报名关键字不能为空", (await Assert.ThrowsAsync<ArgumentException>(() => lottery.StartEntryAsync(Awards, " "))).Message);
        Assert.Equal(before, lottery.State);

        if (stop) await lottery.StopAsync(); else await lottery.CancelAsync();
        var pool = Assert.IsType<PoolLottery>(lottery.State.Lottery);
        Assert.Equal(PoolLotteryPhase.Idle, pool.Phase);
        Assert.Equal(stop, pool.IsDrawing);
        Assert.Equal(stop ? 1 : 0, pool.Draws.Length);
    }

    [Fact]
    public async Task Reconnecting_allows_closing_drawing_ending_and_hiding_an_entry_lottery()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync(Awards, "抽奖");
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"抽奖",[42,"小明"]]}""");
        h.Server.ReplyToAuthentication = false;
        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        await h.Client.IdleAsync();
        Assert.False(lottery.State.Connected);
        Assert.True(lottery.State.CanControl);
        var before = lottery.State;
        Assert.Equal("请先连接直播间。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.NewAsync(LotteryMode.Pool))).Message);
        Assert.Equal("请先连接直播间。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartAsync(Prizes, "停"))).Message);
        Assert.Equal("请先连接直播间。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartEntryAsync(Awards, "抽奖"))).Message);
        Assert.Equal(before, lottery.State);

        await lottery.CloseEntriesAsync();
        Assert.Equal(EntryLotteryPhase.Closed, Assert.IsType<EntryLottery>(lottery.State.Lottery).Phase);
        await lottery.DrawEntryAsync(0);
        Assert.Equal(42, Assert.Single(Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws).Winner.Uid);
        h.Time.Advance(LotteryController.DrawDuration);
        await h.Client.IdleAsync();
        Assert.False(lottery.State.Connected);
        Assert.True(lottery.State.CanControl);
        Assert.False(lottery.State.Lottery!.IsDrawing);
        await lottery.EndEntryAsync();
        Assert.Equal(EntryLotteryPhase.Ended, Assert.IsType<EntryLottery>(lottery.State.Lottery).Phase);
        await lottery.SetEntryHiddenAsync(true);
        Assert.True(Assert.IsType<EntryLottery>(lottery.State.Lottery).Hidden);
        await lottery.SetEntryHiddenAsync(false);
        Assert.False(Assert.IsType<EntryLottery>(lottery.State.Lottery).Hidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_pool_blocks_start_while_spinning_or_drawing_but_only_drawing_blocks_new(bool drawing)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.StartAsync(Prizes, "停");
        if (drawing) await lottery.StopAsync();
        var before = lottery.State;
        Assert.Equal("请等本次开奖结束。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartAsync(Prizes, "停"))).Message);
        Assert.Equal("当前阶段不能执行此操作。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartEntryAsync(Awards, "抽奖"))).Message);
        Assert.Equal("当前阶段不能执行此操作。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(0))).Message);
        Assert.Equal("当前阶段不能执行此操作。", (await Assert.ThrowsAsync<InvalidOperationException>(lottery.EndEntryAsync)).Message);
        if (drawing)
        {
            Assert.Equal("请等本次开奖结束。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.NewAsync(LotteryMode.Pool))).Message);
            Assert.Equal("当前没有转动中的抽奖。", (await Assert.ThrowsAsync<InvalidOperationException>(lottery.CancelAsync)).Message);
            Assert.Equal("当前没有转动中的抽奖。", (await Assert.ThrowsAsync<InvalidOperationException>(lottery.StopAsync)).Message);
            Assert.Equal(before, lottery.State);
        }
        else
        {
            await lottery.NewAsync(LotteryMode.Pool);
            Assert.NotEqual(before.Lottery!.Id, lottery.State.Lottery!.Id);
            Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
        }
    }

    [Fact]
    public async Task An_entry_draw_blocks_drawing_and_ending_without_replacing_mode_or_phase_errors()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync(Awards, "抽奖");
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"抽奖",[42,"小明"]]}""");
        await lottery.CloseEntriesAsync();
        await lottery.DrawEntryAsync(0);
        var before = lottery.State;
        Assert.True(before.Lottery!.IsDrawing);
        Assert.Equal("先结束这一场才能新开。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.NewAsync(LotteryMode.Pool))).Message);
        Assert.Equal("请先新开一场奖池抽奖。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartAsync(Prizes, "停"))).Message);
        Assert.Equal("请等本次开奖结束。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(0))).Message);
        Assert.Equal("请等本次开奖结束。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(-1))).Message);
        Assert.Equal("请等本次开奖结束。", (await Assert.ThrowsAsync<InvalidOperationException>(lottery.EndEntryAsync)).Message);
        Assert.Equal("当前阶段不能执行此操作。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartEntryAsync(Awards, "抽奖"))).Message);
        Assert.Equal("当前阶段不能执行此操作。", (await Assert.ThrowsAsync<InvalidOperationException>(lottery.CloseEntriesAsync)).Message);
        Assert.Equal("当前阶段不能执行此操作。", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.SetEntryHiddenAsync(true))).Message);
        Assert.Equal("当前没有转动中的抽奖。", (await Assert.ThrowsAsync<InvalidOperationException>(lottery.CancelAsync)).Message);
        Assert.Equal("当前没有转动中的抽奖。", (await Assert.ThrowsAsync<InvalidOperationException>(lottery.StopAsync)).Message);
        Assert.Equal(before, lottery.State);
    }
}
