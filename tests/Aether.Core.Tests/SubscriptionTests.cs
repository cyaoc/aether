using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class SubscriptionTests
{
    [Fact]
    public async Task Web_service_readers_receive_their_own_snapshot_and_drain_status_changes_on_disposal()
    {
        await using var h = new WatchHarness();
        var service = h.Client.WebService;
        await using var first = service.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.False(Assert.IsType<WebServiceStatus>(await h.NextAsync(first)).Running);
        await service.StartAsync(WelcomeAnimationTests.FreePort());
        await using var second = service.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(Assert.IsType<WebServiceStatus>(await h.NextAsync(second)).Running);
        await service.StopAsync();
        service.Dispose();
        Assert.True(Assert.IsType<WebServiceStatus>(await h.NextAsync(first)).Running);
        Assert.False(Assert.IsType<WebServiceStatus>(await h.NextAsync(first)).Running);
        Assert.False(Assert.IsType<WebServiceStatus>(await h.NextAsync(second)).Running);
        Assert.False(await first.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.False(await second.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
    }

    [Fact]
    public async Task Lottery_readers_receive_their_own_snapshot_and_updates_until_cancelled_or_disposed()
    {
        await using var h = new WatchHarness();
        var lottery = h.Client.Lottery;
        using var stop = new CancellationTokenSource();
        await using var first = lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(stop.Token);
        Assert.Equal(LotteryTheme.StarSummon, (await h.NextAsync(first)).State.Theme);
        lottery.SelectTheme(LotteryTheme.MechaLock);
        await using var second = lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.Equal(LotteryTheme.MechaLock, (await h.NextAsync(second)).State.Theme);
        lottery.SelectTheme(LotteryTheme.CapsuleGacha);
        Assert.Equal(LotteryTheme.MechaLock, (await h.NextAsync(first)).State.Theme);
        Assert.Equal(LotteryTheme.CapsuleGacha, (await h.NextAsync(first)).State.Theme);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.MoveNextAsync().AsTask());
        lottery.SelectTheme(LotteryTheme.StarSummon);
        lottery.Dispose();
        Assert.Equal(LotteryTheme.CapsuleGacha, (await h.NextAsync(second)).State.Theme);
        Assert.Equal(LotteryTheme.StarSummon, (await h.NextAsync(second)).State.Theme);
        Assert.False(await second.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        await using var disposed = lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => disposed.MoveNextAsync().AsTask());
    }
}
