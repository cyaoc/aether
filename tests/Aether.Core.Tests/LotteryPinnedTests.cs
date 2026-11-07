using System.Text.Json;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.ServerSentEvents;

namespace Aether.Core.Tests;

public sealed class LotteryPinnedTests
{
    [Fact]
    public async Task Pinned_results_are_shared_across_connections_but_not_saved_or_used_by_examples()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        var lottery = h.Client.Lottery;
        await using var updates = lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.False((await h.NextAsync(updates)).State.PinnedResults);
        await lottery.SetPinnedResultsAsync(true);
        Assert.True((await h.NextAsync(updates)).State.PinnedResults);
        await h.LoginAsync();
        var watch = await h.WatchConnectedAsync();
        await h.Client.IdleAsync();
        Assert.True(lottery.State.PinnedResults);
        await watch.DisposeAsync();
        await using var next = await h.WatchConnectedAsync();
        await h.Client.IdleAsync();
        Assert.True(lottery.State.PinnedResults);
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await h.Client.WebService.Lottery.PlayExampleAsync();
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
            { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var response = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
        var (_, json) = await Event(reader);
        Assert.True(json.GetProperty("isExamplePlaying").GetBoolean());
        Assert.False(json.GetProperty("pinnedResults").GetBoolean());
        Assert.True(lottery.State.PinnedResults);
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        Assert.False(restarted.Lottery.State.PinnedResults);
    }

    [Fact]
    public async Task Live_page_state_contains_the_pinned_results_switch()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.Client.Lottery.SetPinnedResultsAsync(true);
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
            { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var response = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
        var (_, json) = await Event(reader);
        Assert.True(json.GetProperty("pinnedResults").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lottery").ValueKind);
    }
}
