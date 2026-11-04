using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.GiftBoardStates;

namespace Aether.Core.Tests;

/// <summary>When the 礼物榜 moves on to a new 期 by itself: a room_init that says the room is live, or a LIVE message.</summary>
public sealed class GiftBoardPeriodTests
{
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
        h.Http.SetLive(ShowA);
        await h.WatchAndLeaveAGiftAsync();

        h.Http.SetLive(ShowB);
        await using var next = await h.WatchConnectedAsync();

        Assert.Empty(await h.BoardRowsAsync());
        Assert.Equal<long?>([ShowA, ShowB], TestDatabase.GiftBoardLiveTimes(h.DataDirectory));
    }

    // 0 hours is a plain reconnect; 30 hours later is on another calendar day wherever the streamer is.
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public async Task Connecting_again_to_the_same_show_keeps_the_board_even_past_midnight(int hoursLater)
    {
        await using var h = new WatchHarness();
        h.Http.SetLive(ShowA);
        await h.WatchAndLeaveAGiftAsync();

        h.Time.Advance(TimeSpan.FromHours(hoursLater));
        await using var next = await h.WatchConnectedAsync();

        Assert.Single(await h.BoardRowsAsync());
        Assert.Single(TestDatabase.GiftBoardLiveTimes(h.DataDirectory));
    }

    // A rerun is live_status 2, and B站 gives the epoch-like live_time of an off-air room even while it says 1.
    [Theory]
    [InlineData(0, FakeBilibiliHttp.NotLiveTime)]
    [InlineData(2, ShowB)]
    [InlineData(1, FakeBilibiliHttp.NotLiveTime)]
    public async Task Connecting_to_a_room_that_is_not_live_keeps_the_board(int liveStatus, long liveTime)
    {
        await using var h = new WatchHarness();
        h.Http.SetLive(ShowA);
        await h.WatchAndLeaveAGiftAsync();

        h.Http.SetRoom(liveStatus: liveStatus, liveTime: liveTime);
        await using var next = await h.WatchConnectedAsync();

        Assert.Single(await h.BoardRowsAsync());
        Assert.Single(TestDatabase.GiftBoardLiveTimes(h.DataDirectory));
    }

    [Fact]
    public async Task Without_the_per_show_switch_a_new_show_keeps_the_board()
    {
        await using var h = new WatchHarness();
        h.Http.SetLive(ShowA);
        await h.WatchAndLeaveAGiftAsync();
        await h.ApplySettingsAsync(("gift_board.reset_on_live", "false"));

        h.Http.SetLive(ShowB);
        await using var next = await h.WatchConnectedAsync();

        Assert.Single(await h.BoardRowsAsync());
        Assert.Single(TestDatabase.GiftBoardLiveTimes(h.DataDirectory));
    }

    // The room was connected while the board was off, or while it kept the board across shows, and a new show began meanwhile.
    [Theory]
    [InlineData("gift_board.enabled")]
    [InlineData("gift_board.reset_on_live")]
    public async Task Turning_a_switch_on_during_a_show_that_the_latest_board_does_not_belong_to_opens_a_board_for_it(string setting)
    {
        await using var h = new WatchHarness();
        h.Http.SetLive(ShowA);
        await h.WatchAndLeaveAGiftAsync();
        await h.ApplySettingsAsync((setting, "false"));
        h.Http.SetLive(ShowB);
        await using var updates = await h.WatchConnectedAsync();

        await h.ApplySettingsAsync((setting, "true"));
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 20002, tid: "this-show"));

        Assert.Equal<long?>([ShowA, ShowB], TestDatabase.GiftBoardLiveTimes(h.DataDirectory));
        Assert.Equal(["20002/gift:32698"], (await h.BoardRowsAsync()).Select(row => row.Id));
    }

    [Fact]
    public async Task The_first_gift_opens_a_board_that_remembers_the_show_under_way()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await h.ApplySettingsAsync(("gift_board.enabled", "true"), ("gift_board.reset_on_live", "false"));
        h.Http.SetLive(ShowA);
        await using var updates = await h.WatchConnectedAsync();

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());

        Assert.Equal<long?>([ShowA], TestDatabase.GiftBoardLiveTimes(h.DataDirectory));
    }

    [Fact]
    public async Task Nothing_is_opened_while_the_board_is_off()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await h.ApplySettingsAsync(("gift_board.enabled", "false"));
        h.Http.SetLive(ShowA);

        await using var updates = await h.WatchConnectedAsync();
        h.Http.SetLive(ShowB);
        await h.PushAndSyncAsync(updates, Live);

        Assert.Empty(TestDatabase.GiftBoardLiveTimes(h.DataDirectory));
    }

    [Fact]
    public async Task A_LIVE_message_opens_a_new_board_when_the_room_is_live_since_another_time()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());

        h.Http.SetLive(ShowA);
        await h.PushAndSyncAsync(updates, Live);

        Assert.Empty(await h.BoardRowsAsync());
        Assert.Equal<long?>([null, ShowA], TestDatabase.GiftBoardLiveTimes(h.DataDirectory));
    }

    [Fact]
    public async Task Several_LIVE_messages_for_one_show_open_only_one_new_board()
    {
        await using var h = new WatchHarness();
        h.Http.SetLive(ShowA);
        await using var updates = await h.WatchWithGiftBoardAsync();

        h.Http.SetLive(ShowB);
        await h.PushAndSyncAsync(updates, Live, Live, Live);

        Assert.Equal<long?>([ShowA, ShowB], TestDatabase.GiftBoardLiveTimes(h.DataDirectory));
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
}
