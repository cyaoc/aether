using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryVisibilityTests
{
    [Fact]
    public async Task Hidden_entry_winners_remain_ineligible_and_bulk_hiding_does_not_change_the_draw()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var watch = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry([new("幸运奖", "抱枕", 2, Grade.Second)], "参加");
        await h.PushAndCollectAsync(watch,
            """{"cmd":"DANMU_MSG","info":[[],"参加",[7,"七七"]]}""",
            """{"cmd":"DANMU_MSG","info":[[],"参加",[42,"小明"]]}""");
        lottery.CloseEntries();
        lottery.DrawEntry(0);
        var first = Assert.Single(Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws);
        lottery.SetDrawHiddenFromStream(first.Id, true);
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.DrawEntry(0);
        var before = Assert.IsType<EntryLottery>(lottery.State.Lottery);
        Assert.NotEqual(first.Winner.Uid, before.Draws[1].Winner.Uid);
        lottery.HideAllDrawsFromStream();
        var after = Assert.IsType<EntryLottery>(lottery.State.Lottery);
        Assert.Equal(before.DrawUntil, after.DrawUntil);
        Assert.All(after.Draws, draw => Assert.True(draw.HiddenFromStream));
        Assert.Equal(before.Draws.Select(draw => (draw.Id, draw.Winner, draw.Award, draw.Candidates)),
            after.Draws.Select(draw => (draw.Id, draw.Winner, draw.Award, draw.Candidates)));
        Assert.Equal(2, Assert.Single(lottery.GetHistory(7734200)).DrawCount);
        Assert.Equal(2, Assert.IsType<EntryLottery>(lottery.GetHistoryDetails(7734200, after.Id)).Draws.Length);
        h.Time.Advance(LotteryController.DrawDuration);
        Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(0));
    }

    [Fact]
    public async Task Visibility_requires_the_room_lock_and_cannot_change_another_lottery()
    {
        await using var h = new WatchHarness(enableLottery: true);
        var lottery = h.Client.Lottery;
        Assert.Throws<InvalidOperationException>(() => lottery.SetDrawHiddenFromStream("missing", true));
        Assert.Throws<InvalidOperationException>(lottery.HideAllDrawsFromStream);
        await h.LoginAsync();
        var watch = await h.WatchConnectedAsync();
        lottery.Start([new("贴纸", 1, Grade.Third)], "停");
        lottery.Stop();
        var original = Assert.Single(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        h.Time.Advance(LotteryController.DrawDuration);
        var oldId = lottery.State.Lottery!.Id;
        lottery.New(LotteryMode.Pool);
        Assert.Throws<ArgumentException>(() => lottery.SetDrawHiddenFromStream(original.Id, true));
        Assert.Equivalent(original, Assert.Single(Assert.IsType<PoolLottery>(lottery.GetHistoryDetails(7734200, oldId)).Draws));
        lottery.HideAllDrawsFromStream();
        await watch.DisposeAsync();
        Assert.Throws<InvalidOperationException>(lottery.HideAllDrawsFromStream);
        Assert.Throws<InvalidOperationException>(() => lottery.SetDrawHiddenFromStream(original.Id, true));
        lottery.Dispose();
        Assert.Throws<ObjectDisposedException>(lottery.HideAllDrawsFromStream);
    }

    [Fact]
    public async Task Pool_draw_visibility_is_published_restorable_and_persisted_without_removing_history()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        var watch = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start([new("抱枕", 1, Grade.Grand)], "停");
        lottery.Stop();
        var original = Assert.Single(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        Assert.False(original.HiddenFromStream);
        await using (var updates = lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token))
        {
            await h.NextAsync(updates);
            lottery.SetDrawHiddenFromStream(original.Id, true);
            var hidden = Assert.Single(Assert.IsType<PoolLottery>((await h.NextAsync(updates)).State.Lottery).Draws);
            Assert.Equal(original with { HiddenFromStream = true }, hidden);
            lottery.SetDrawHiddenFromStream(original.Id, false);
            Assert.Equal(original, Assert.Single(Assert.IsType<PoolLottery>((await h.NextAsync(updates)).State.Lottery).Draws));
        }
        lottery.HideAllDrawsFromStream();
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.Start([new("贴纸", 1, Grade.Third)], "停");
        lottery.Stop();
        var pool = Assert.IsType<PoolLottery>(lottery.State.Lottery);
        Assert.Equal(new[] { true, false }, pool.Draws.Select(draw => draw.HiddenFromStream));
        Assert.Equal(2, Assert.Single(lottery.GetHistory(7734200)).DrawCount);
        Assert.Equal(2, Assert.IsType<PoolLottery>(lottery.GetHistoryDetails(7734200, pool.Id)).Draws.Length);
        await watch.DisposeAsync();
        h.Client.Dispose();
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        await using var next = await h.WatchConnectedAsync(restarted);
        Assert.Equal(new[] { true, false }, Assert.IsType<PoolLottery>(restarted.Lottery.State.Lottery).Draws.Select(draw => draw.HiddenFromStream));
    }
}
