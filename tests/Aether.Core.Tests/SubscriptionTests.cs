using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class SubscriptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Immediately_stopping_the_web_service_does_not_fault_and_releases_its_port(bool yieldBeforeStop)
    {
        await using var h = new WatchHarness();
        var service = h.Client.WebService;
        var port = WelcomeAnimationTests.FreePort();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await service.StartAsync(port);
            Assert.True(service.Status.Running);
            if (yieldBeforeStop) await Task.Yield();
            await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
            Assert.False(service.Status.Running);
        }
    }

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
        await lottery.SelectThemeAsync(LotteryTheme.MechaLock);
        await using var second = lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.Equal(LotteryTheme.MechaLock, (await h.NextAsync(second)).State.Theme);
        await lottery.SelectThemeAsync(LotteryTheme.CapsuleGacha);
        Assert.Equal(LotteryTheme.MechaLock, (await h.NextAsync(first)).State.Theme);
        Assert.Equal(LotteryTheme.CapsuleGacha, (await h.NextAsync(first)).State.Theme);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.MoveNextAsync().AsTask());
        await lottery.SelectThemeAsync(LotteryTheme.StarSummon);
        lottery.Dispose();
        Assert.Equal(LotteryTheme.CapsuleGacha, (await h.NextAsync(second)).State.Theme);
        Assert.Equal(LotteryTheme.StarSummon, (await h.NextAsync(second)).State.Theme);
        Assert.False(await second.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        await using var disposed = lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disposed.MoveNextAsync().AsTask());
    }
}
