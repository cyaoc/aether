using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryVisibilityTests
{
    [Fact]
    public async Task Hidden_entry_winners_remain_ineligible_and_bulk_hiding_does_not_change_the_draw()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var watch = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync([new("幸运奖", "抱枕", 2, Grade.Second)], "参加");
        await h.PushAndCollectAsync(watch,
            """{"cmd":"DANMU_MSG","info":[[],"参加",[7,"七七"]]}""",
            """{"cmd":"DANMU_MSG","info":[[],"参加",[42,"小明"]]}""");
        await lottery.CloseEntriesAsync();
        await lottery.DrawEntryAsync(0);
        var first = Assert.Single(Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws);
        await lottery.SetDrawHiddenFromStreamAsync(first.Id, true);
        h.Time.Advance(LotteryController.DrawDuration);
        await lottery.DrawEntryAsync(0);
        var before = Assert.IsType<EntryLottery>(lottery.State.Lottery);
        Assert.NotEqual(first.Winner.Uid, before.Draws[1].Winner.Uid);
        await lottery.HideAllDrawsFromStreamAsync();
        var after = Assert.IsType<EntryLottery>(lottery.State.Lottery);
        Assert.Equal(before.DrawUntil, after.DrawUntil);
        Assert.All(after.Draws, draw => Assert.True(draw.HiddenFromStream));
        Assert.Equal(before.Draws.Select(draw => (draw.Id, draw.Winner, draw.Award, draw.Candidates)),
            after.Draws.Select(draw => (draw.Id, draw.Winner, draw.Award, draw.Candidates)));
        Assert.Equal(2, Assert.Single(await lottery.GetHistoryAsync(7734200)).DrawCount);
        Assert.Equal(2, Assert.IsType<EntryLottery>(await lottery.GetHistoryDetailsAsync(7734200, after.Id)).Draws.Length);
        h.Time.Advance(LotteryController.DrawDuration);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(0));
    }

    [Fact]
    public async Task Visibility_requires_the_room_lock_and_cannot_change_another_lottery()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        var lottery = h.Client.Lottery;
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.SetDrawHiddenFromStreamAsync("missing", true));
        await Assert.ThrowsAsync<InvalidOperationException>(lottery.HideAllDrawsFromStreamAsync);
        await h.LoginAsync();
        var watch = await h.WatchConnectedAsync();
        await lottery.StartAsync([new("贴纸", 1, Grade.Third)], "停");
        await lottery.StopAsync();
        var original = Assert.Single(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        h.Time.Advance(LotteryController.DrawDuration);
        var oldId = lottery.State.Lottery!.Id;
        await lottery.NewAsync(LotteryMode.Pool);
        await Assert.ThrowsAsync<ArgumentException>(() => lottery.SetDrawHiddenFromStreamAsync(original.Id, true));
        Assert.Equivalent(original, Assert.Single(Assert.IsType<PoolLottery>(await lottery.GetHistoryDetailsAsync(7734200, oldId)).Draws));
        await lottery.HideAllDrawsFromStreamAsync();
        await watch.DisposeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(lottery.HideAllDrawsFromStreamAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.SetDrawHiddenFromStreamAsync(original.Id, true));
        lottery.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(lottery.HideAllDrawsFromStreamAsync);
    }

    [Fact]
    public async Task Pool_draw_visibility_is_published_restorable_and_persisted_without_removing_history()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        var watch = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.StartAsync([new("抱枕", 1, Grade.Grand)], "停");
        await lottery.StopAsync();
        var original = Assert.Single(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        Assert.False(original.HiddenFromStream);
        await using (var updates = lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token))
        {
            await h.NextAsync(updates);
            await lottery.SetDrawHiddenFromStreamAsync(original.Id, true);
            var hidden = Assert.Single(Assert.IsType<PoolLottery>((await h.NextAsync(updates)).State.Lottery).Draws);
            Assert.Equal(original with { HiddenFromStream = true }, hidden);
            await lottery.SetDrawHiddenFromStreamAsync(original.Id, false);
            Assert.Equal(original, Assert.Single(Assert.IsType<PoolLottery>((await h.NextAsync(updates)).State.Lottery).Draws));
        }
        await lottery.HideAllDrawsFromStreamAsync();
        h.Time.Advance(LotteryController.DrawDuration);
        await lottery.StartAsync([new("贴纸", 1, Grade.Third)], "停");
        await lottery.StopAsync();
        var pool = Assert.IsType<PoolLottery>(lottery.State.Lottery);
        Assert.Equal(new[] { true, false }, pool.Draws.Select(draw => draw.HiddenFromStream));
        Assert.Equal(2, Assert.Single(await lottery.GetHistoryAsync(7734200)).DrawCount);
        Assert.Equal(2, Assert.IsType<PoolLottery>(await lottery.GetHistoryDetailsAsync(7734200, pool.Id)).Draws.Length);
        await watch.DisposeAsync();
        h.Client.Dispose();
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        await using var next = await h.WatchConnectedAsync(restarted);
        await restarted.IdleAsync();
        Assert.Equal(new[] { true, false }, Assert.IsType<PoolLottery>(restarted.Lottery.State.Lottery).Draws.Select(draw => draw.HiddenFromStream));
    }
}
