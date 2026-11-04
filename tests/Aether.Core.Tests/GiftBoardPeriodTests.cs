using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

/// <summary>When the 礼物榜 moves on to a new 期 by itself: a room_init that says the room is live, or a LIVE message.</summary>
public sealed class GiftBoardPeriodTests
{
    // Unix times two shows began at; the second is more than a day after the first.
    private const long ShowA = 1700000000;
    private const long ShowB = 1700100000;

    // The LIVE sample in bilibili-API-collect's message_stream.md, with this test room's id. The message itself is never
    // trusted for the start time (only its first packet carries one): room_init is read again instead.
    private const string Live = """
        {"cmd":"LIVE","live_key":"234304209915761953","voice_background":"","sub_session_key":"234304209915761953sub_time:1651036923",
         "live_platform":"pc","live_model":0,"live_time":1651036923,"roomid":7734200}
        """;

    [Fact]
    public async Task Connecting_to_a_show_that_began_at_another_time_opens_a_new_board_and_keeps_the_old_one()
    {
        await using var h = new WatchHarness();
        await ConnectWithAGiftAsync(h, ShowA);

        h.Http.SetRoom(liveStatus: 1, liveTime: ShowB);
        await using var next = await h.WatchConnectedAsync();

        Assert.Empty(await h.BoardRowsAsync());
        Assert.Equal([ShowA, ShowB], TestDatabase.GiftBoardPeriods(h.DataDirectory).Select(period => period.LiveTime));
    }

    // 0 hours is a plain reconnect; 30 hours later is on another calendar day wherever the streamer is.
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public async Task Connecting_again_to_the_same_show_keeps_the_board_even_past_midnight(int hoursLater)
    {
        await using var h = new WatchHarness();
        await ConnectWithAGiftAsync(h, ShowA);

        h.Time.Advance(TimeSpan.FromHours(hoursLater));
        await using var next = await h.WatchConnectedAsync();

        Assert.Single(await h.BoardRowsAsync());
        Assert.Single(TestDatabase.GiftBoardPeriods(h.DataDirectory));
    }

    // A rerun is live_status 2, and B站 gives the epoch-like live_time of an off-air room even while it says 1.
    [Theory]
    [InlineData(0, FakeBilibiliHttp.NotLiveTime)]
    [InlineData(2, ShowB)]
    [InlineData(1, FakeBilibiliHttp.NotLiveTime)]
    public async Task Connecting_to_a_room_that_is_not_live_keeps_the_board(int liveStatus, long liveTime)
    {
        await using var h = new WatchHarness();
        await ConnectWithAGiftAsync(h, ShowA);

        h.Http.SetRoom(liveStatus: liveStatus, liveTime: liveTime);
        await using var next = await h.WatchConnectedAsync();

        Assert.Single(await h.BoardRowsAsync());
        Assert.Single(TestDatabase.GiftBoardPeriods(h.DataDirectory));
    }

    [Fact]
    public async Task Without_the_per_show_switch_a_new_show_keeps_the_board()
    {
        await using var h = new WatchHarness();
        await ConnectWithAGiftAsync(h, ShowA);
        await h.ApplySettingsAsync(("gift_board.reset_on_live", "false"));

        h.Http.SetRoom(liveStatus: 1, liveTime: ShowB);
        await using var next = await h.WatchConnectedAsync();

        Assert.Single(await h.BoardRowsAsync());
        Assert.Single(TestDatabase.GiftBoardPeriods(h.DataDirectory));
    }

    [Fact]
    public async Task The_first_gift_opens_a_board_that_remembers_the_show_under_way()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await h.ApplySettingsAsync(("gift_board.enabled", "true"), ("gift_board.reset_on_live", "false"));
        h.Http.SetRoom(liveStatus: 1, liveTime: ShowA);
        await using var updates = await h.WatchConnectedAsync();

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());

        Assert.Equal<long?>([ShowA], TestDatabase.GiftBoardPeriods(h.DataDirectory).Select(period => period.LiveTime));
    }

    [Fact]
    public async Task Nothing_is_opened_while_the_board_is_off()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await h.ApplySettingsAsync(("gift_board.enabled", "false"));
        h.Http.SetRoom(liveStatus: 1, liveTime: ShowA);

        await using var updates = await h.WatchConnectedAsync();
        h.Http.SetRoom(liveStatus: 1, liveTime: ShowB);
        await h.PushAndSyncAsync(updates, Live);

        Assert.Empty(TestDatabase.GiftBoardPeriods(h.DataDirectory));
    }

    [Fact]
    public async Task A_LIVE_message_opens_a_new_board_when_the_room_is_live_since_another_time()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());

        h.Http.SetRoom(liveStatus: 1, liveTime: ShowA);
        await h.PushAndSyncAsync(updates, Live);

        Assert.Empty(await h.BoardRowsAsync());
        Assert.Equal<long?>([null, ShowA], TestDatabase.GiftBoardPeriods(h.DataDirectory).Select(period => period.LiveTime));
    }

    [Fact]
    public async Task Several_LIVE_messages_for_one_show_open_only_one_new_board()
    {
        await using var h = new WatchHarness();
        h.Http.SetRoom(liveStatus: 1, liveTime: ShowA);
        await using var updates = await h.WatchWithGiftBoardAsync();

        h.Http.SetRoom(liveStatus: 1, liveTime: ShowB);
        await h.PushAndSyncAsync(updates, Live, Live, Live);

        Assert.Equal<long?>([ShowA, ShowB], TestDatabase.GiftBoardPeriods(h.DataDirectory).Select(period => period.LiveTime));
    }

    [Fact]
    public async Task A_failed_read_after_a_LIVE_message_is_logged_and_the_room_connection_goes_on()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        h.Http.FailOnce(FakeBilibiliHttp.RoomInitUrl, new HttpRequestException("测试断网"));

        await h.PushAndSyncAsync(updates, Live);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());

        Assert.Contains("测试断网", Assert.Single(h.Logger.Entries, entry => entry.Level == LogLevel.Warning).Message);
        Assert.Single(await h.BoardRowsAsync());
    }

    /// <summary>Connects to a room that has been live since <paramref name="liveTime"/> with the board on, puts a gift on it
    /// and disconnects, so that the next connection meets a board with a gift on it.</summary>
    private static async Task ConnectWithAGiftAsync(WatchHarness h, long liveTime)
    {
        h.Http.SetRoom(liveStatus: 1, liveTime: liveTime);
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
        Assert.Single(await h.BoardRowsAsync());
    }
}
