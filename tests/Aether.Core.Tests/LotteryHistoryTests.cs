using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryHistoryTests
{
    [Fact]
    public async Task History_orders_by_time_then_creation_and_marks_replaced_preparation_as_ended()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        var first = lottery.State.EntryLottery!.Id;
        lottery.New(LotteryMode.Pool);
        var second = lottery.State.PoolLottery!.Id;
        lottery.New(LotteryMode.Entry);
        var third = lottery.State.EntryLottery!.Id;
        var history = lottery.GetHistory(7734200);
        Assert.Equal(new[] { third, second, first }, history.Select(item => item.Id));
        Assert.Null(history[0].EndedAt);
        Assert.All(history.Skip(1), item => Assert.NotNull(item.EndedAt));
        Assert.All(history, item => Assert.Equal(0, item.DrawCount));
    }

    [Fact]
    public async Task History_includes_both_modes_and_unfinished_lotteries_after_restart()
    {
        await using var h = new WatchHarness(enableLottery: true);
        Assert.Empty(h.Client.Lottery.GetHistory(7734200));
        await h.LoginAsync();
        var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start([new("键盘", 1, Grade.Grand)], "开", 42);
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"开",[42,"小明"]]}""");
        var poolId = lottery.State.PoolLottery!.Id;
        Assert.Null(Assert.Single(lottery.GetHistory(7734200)).EndedAt);
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry([new("幸运奖", "抱枕", 2, Grade.Second)], "参加");
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"参加",[7,"七七"]]}""");
        lottery.CloseEntries();
        lottery.DrawEntry(0);
        var entryId = lottery.State.EntryLottery!.Id;
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.EndEntry();
        lottery.New(LotteryMode.Pool);
        var currentId = lottery.State.PoolLottery!.Id;
        await updates.DisposeAsync();
        h.Client.Dispose();
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        var history = restarted.Lottery.GetHistory(7734200);
        Assert.Equal(new[] { currentId, entryId, poolId }, history.Select(item => item.Id));
        Assert.Null(history[0].EndedAt);
        Assert.All(history.Skip(1), item => Assert.NotNull(item.EndedAt));
        Assert.Equal(LotteryMode.Entry, history[1].Mode);
        Assert.Equal(1, history[1].EntryCount);
        Assert.Equal(1, history[1].DrawCount);
        Assert.Equal("幸运奖", Assert.Single(history[1].Awards).Name);
        Assert.Equal("键盘", Assert.Single(history[2].Prizes).Name);
        var entry = restarted.Lottery.GetHistoryDetails(7734200, entryId)!;
        Assert.Equal("参加", entry.Lottery.Keyword);
        var entryDraw = Assert.Single(entry.EntryDraws);
        Assert.Equal("七七", entryDraw.Winner.Nickname);
        Assert.Equal("抱枕", entryDraw.Award.Prize);
        var pool = restarted.Lottery.GetHistoryDetails(7734200, poolId)!;
        Assert.Equal("开", pool.Lottery.StopWord);
        var poolDraw = Assert.Single(pool.PoolDraws);
        Assert.Equal(42, poolDraw.Uid);
        Assert.Equal("小明", poolDraw.Nickname);
        Assert.Equal("键盘", poolDraw.Prize.Name);
        Assert.Empty(restarted.Lottery.GetHistory(999));
        Assert.Null(restarted.Lottery.GetHistoryDetails(999, poolId));
        Assert.Null(restarted.Lottery.GetHistoryDetails(7734200, "missing"));
        Assert.Null(restarted.Lottery.State.RoomId);
    }
}
