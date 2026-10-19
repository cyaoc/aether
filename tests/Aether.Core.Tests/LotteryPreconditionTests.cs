using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryPreconditionTests
{
    private static readonly PoolPrize[] Prizes = [new("抱枕", 1, Grade.Grand)];
    private static readonly LotteryAward[] Awards = [new("一等奖", "抱枕", 1, Grade.Grand)];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconnecting_blocks_starts_but_allows_stopping_or_cancelling_the_pool(bool stop)
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start(Prizes, "停");
        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        Assert.False(lottery.State.Connected);
        Assert.True(lottery.State.CanControl);
        var before = lottery.State;
        Assert.Equal("请先连接直播间。", Assert.Throws<InvalidOperationException>(() => lottery.New(LotteryMode.Pool)).Message);
        Assert.Equal("请先连接直播间。", Assert.Throws<InvalidOperationException>(() => lottery.Start(Prizes, "停")).Message);
        Assert.Equal("请先连接直播间。", Assert.Throws<InvalidOperationException>(() => lottery.StartEntry(Awards, "抽奖")).Message);
        Assert.Equal("请选择抽奖方式。", Assert.Throws<ArgumentException>(() => lottery.New((LotteryMode)99)).Message);
        Assert.Equal("口令不能为空", Assert.Throws<ArgumentException>(() => lottery.Start(Prizes, " ")).Message);
        Assert.Equal("报名关键字不能为空", Assert.Throws<ArgumentException>(() => lottery.StartEntry(Awards, " ")).Message);
        Assert.Equal(before, lottery.State);

        if (stop) lottery.Stop(); else lottery.Cancel();
        var pool = Assert.IsType<PoolLottery>(lottery.State.Lottery);
        Assert.Equal(PoolLotteryPhase.Idle, pool.Phase);
        Assert.Equal(stop, pool.IsDrawing);
        Assert.Equal(stop ? 1 : 0, pool.Draws.Length);
    }

    [Fact]
    public async Task Reconnecting_allows_closing_drawing_ending_and_hiding_an_entry_lottery()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry(Awards, "抽奖");
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"抽奖",[42,"小明"]]}""");
        h.Server.ReplyToAuthentication = false;
        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        Assert.False(lottery.State.Connected);
        Assert.True(lottery.State.CanControl);
        var before = lottery.State;
        Assert.Equal("请先连接直播间。", Assert.Throws<InvalidOperationException>(() => lottery.New(LotteryMode.Pool)).Message);
        Assert.Equal("请先连接直播间。", Assert.Throws<InvalidOperationException>(() => lottery.Start(Prizes, "停")).Message);
        Assert.Equal("请先连接直播间。", Assert.Throws<InvalidOperationException>(() => lottery.StartEntry(Awards, "抽奖")).Message);
        Assert.Equal(before, lottery.State);

        lottery.CloseEntries();
        Assert.Equal(EntryLotteryPhase.Closed, Assert.IsType<EntryLottery>(lottery.State.Lottery).Phase);
        lottery.DrawEntry(0);
        Assert.Equal(42, Assert.Single(Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws).Winner.Uid);
        h.Time.Advance(LotteryController.DrawDuration);
        Assert.False(lottery.State.Connected);
        Assert.True(lottery.State.CanControl);
        Assert.False(lottery.State.Lottery!.IsDrawing);
        lottery.EndEntry();
        Assert.Equal(EntryLotteryPhase.Ended, Assert.IsType<EntryLottery>(lottery.State.Lottery).Phase);
        lottery.SetEntryHidden(true);
        Assert.True(Assert.IsType<EntryLottery>(lottery.State.Lottery).Hidden);
        lottery.SetEntryHidden(false);
        Assert.False(Assert.IsType<EntryLottery>(lottery.State.Lottery).Hidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_pool_blocks_start_while_spinning_or_drawing_but_only_drawing_blocks_new(bool drawing)
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start(Prizes, "停");
        if (drawing) lottery.Stop();
        var before = lottery.State;
        Assert.Equal("请等本次开奖结束。", Assert.Throws<InvalidOperationException>(() => lottery.Start(Prizes, "停")).Message);
        Assert.Equal("当前阶段不能执行此操作。", Assert.Throws<InvalidOperationException>(() => lottery.StartEntry(Awards, "抽奖")).Message);
        Assert.Equal("当前阶段不能执行此操作。", Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(0)).Message);
        Assert.Equal("当前阶段不能执行此操作。", Assert.Throws<InvalidOperationException>(lottery.EndEntry).Message);
        if (drawing)
        {
            Assert.Equal("请等本次开奖结束。", Assert.Throws<InvalidOperationException>(() => lottery.New(LotteryMode.Pool)).Message);
            Assert.Equal("当前没有转动中的抽奖。", Assert.Throws<InvalidOperationException>(lottery.Cancel).Message);
            Assert.Equal("当前没有转动中的抽奖。", Assert.Throws<InvalidOperationException>(lottery.Stop).Message);
            Assert.Equal(before, lottery.State);
        }
        else
        {
            lottery.New(LotteryMode.Pool);
            Assert.NotEqual(before.Lottery!.Id, lottery.State.Lottery!.Id);
            Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
        }
    }

    [Fact]
    public async Task An_entry_draw_blocks_drawing_and_ending_without_replacing_mode_or_phase_errors()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry(Awards, "抽奖");
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"抽奖",[42,"小明"]]}""");
        lottery.CloseEntries();
        lottery.DrawEntry(0);
        var before = lottery.State;
        Assert.True(before.Lottery!.IsDrawing);
        Assert.Equal("先结束这一场才能新开。", Assert.Throws<InvalidOperationException>(() => lottery.New(LotteryMode.Pool)).Message);
        Assert.Equal("请先新开一场奖池抽奖。", Assert.Throws<InvalidOperationException>(() => lottery.Start(Prizes, "停")).Message);
        Assert.Equal("请等本次开奖结束。", Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(0)).Message);
        Assert.Equal("请等本次开奖结束。", Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(-1)).Message);
        Assert.Equal("请等本次开奖结束。", Assert.Throws<InvalidOperationException>(lottery.EndEntry).Message);
        Assert.Equal("当前阶段不能执行此操作。", Assert.Throws<InvalidOperationException>(() => lottery.StartEntry(Awards, "抽奖")).Message);
        Assert.Equal("当前阶段不能执行此操作。", Assert.Throws<InvalidOperationException>(lottery.CloseEntries).Message);
        Assert.Equal("当前阶段不能执行此操作。", Assert.Throws<InvalidOperationException>(() => lottery.SetEntryHidden(true)).Message);
        Assert.Equal("当前没有转动中的抽奖。", Assert.Throws<InvalidOperationException>(lottery.Cancel).Message);
        Assert.Equal("当前没有转动中的抽奖。", Assert.Throws<InvalidOperationException>(lottery.Stop).Message);
        Assert.Equal(before, lottery.State);
    }
}
