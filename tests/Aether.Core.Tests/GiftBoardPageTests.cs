using System.Text.Json;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.ServerSentEvents;

namespace Aether.Core.Tests;

public sealed class GiftBoardPageTests
{
    [Fact]
    public async Task The_page_gets_the_full_state_first_and_then_every_change()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await h.ApplySettingsAsync(("gift_board.enabled", "true"), ("gift_board.threshold", "1"));
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var response = await http.GetAsync("gift-board/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));

        var (name, first) = await Event(reader);
        Assert.Equal("state", name);
        Assert.Equal(0, first.GetProperty("rows").GetArrayLength());
        Assert.True(first.GetProperty("mascot").GetBoolean());
        Assert.Equal(60, first.GetProperty("volume").GetInt32());
        Assert.False(first.GetProperty("fromGift").GetBoolean());

        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(num: 2));
        JsonElement state;
        do state = (await Event(reader)).Data; while (state.GetProperty("rows").GetArrayLength() == 0);

        Assert.True(state.GetProperty("fromGift").GetBoolean());
        var row = Assert.Single(state.GetProperty("rows").EnumerateArray());
        Assert.Equal(new GiftBoardRow("10001/gift:32698", "测试观众", "https://i0.hdslb.com/bfs/face/test-v1.jpg", "小蛋糕",
            "https://s1.hdslb.com/bfs/live/test-v1.png", 0, 1500, 2), JsonSerializer.Deserialize<GiftBoardRow>(row, WebService.Json));
        Assert.False(row.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task The_web_service_counts_and_logs_the_connections_of_the_gift_board_page()
    {
        await using var h = new WatchHarness();
        await h.ApplySettingsAsync();
        Assert.Equal(0, h.Client.WebService.Status.GiftBoardConnections);
        Assert.Equal($"{h.Client.WebService.Status.Origin}/gift-board", h.Client.WebService.Status.GiftBoardAddress);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };

        using var response = await http.GetAsync("gift-board/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);

        Assert.Equal((1, 0, 0), (h.Client.WebService.Status.GiftBoardConnections, h.Client.WebService.Status.VideoConnections,
            h.Client.WebService.Status.LotteryConnections));
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("礼物榜页 1 个推流软件"));
    }
}
