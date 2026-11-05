using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>The 礼物榜 runs in 期 like a lottery (ADR 0014): the streamer starts, ends and continues one in the GUI, and only a
/// running 期 takes gifts and shows on the 礼物榜页.</summary>
public sealed class GiftBoardPeriodTests
{
    // Short enough that a step between messages stays inside the room connection's 60 s idle limit.
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Before_the_first_period_nothing_is_recorded_or_shown()
    {
        await using var h = new WatchHarness(enableLotteryAndGiftBoard: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());

        var state = await h.Client.GiftBoard.CurrentAsync(h.Stop.Token);
        Assert.True(state.Connected);
        Assert.False(state.Shown);
        Assert.Null(state.Period);
        Assert.Equal(0L, TestDatabase.GiftRecords(h.DataDirectory));
    }

    [Fact]
    public async Task A_started_period_records_and_shows_the_gifts_sent_from_then_on()
    {
        await using var h = new WatchHarness(enableLotteryAndGiftBoard: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "before"));
        h.Client.GiftBoard.SetOptions(GiftBoardOptions.Default with { Threshold = 1 });
        var started = h.Time.GetUtcNow();
        h.Client.GiftBoard.StartNewPeriod();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "after"));

        var state = await h.Client.GiftBoard.CurrentAsync(h.Stop.Token);
        Assert.True(state.Shown);
        Assert.Equal(new GiftBoardPeriod(started, null), state.Period);
        Assert.Equal(1, Assert.Single(state.Rows).Num);
    }

    [Fact]
    public async Task An_ended_period_records_nothing_more_and_leaves_the_page_but_keeps_its_rows()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        var started = h.Time.GetUtcNow();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "during"));
        h.Time.Advance(Step);
        var ended = h.Time.GetUtcNow();
        h.Client.GiftBoard.EndPeriod();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "after"));

        var state = await h.Client.GiftBoard.CurrentAsync(h.Stop.Token);
        Assert.False(state.Shown);
        Assert.Equal(new GiftBoardPeriod(started, ended), state.Period);
        Assert.Equal(1, Assert.Single(state.Rows).Num);
        Assert.Equal(1L, TestDatabase.GiftRecords(h.DataDirectory));
    }

    [Fact]
    public async Task A_continued_period_runs_on_from_what_it_had()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        var started = h.Time.GetUtcNow();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "before"));
        h.Client.GiftBoard.EndPeriod();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "while-ended"));

        h.Client.GiftBoard.ContinuePeriod();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "after"));

        var state = await h.Client.GiftBoard.CurrentAsync(h.Stop.Token);
        Assert.True(state.Shown);
        Assert.Equal(new GiftBoardPeriod(started, null), state.Period);
        Assert.Equal(2, Assert.Single(state.Rows).Num);
    }

    [Fact]
    public async Task A_new_period_starts_empty_with_the_options_of_the_last_one()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync(threshold: 5);
        await h.SetOptionsAsync(options => options with { Mascot = false, Volume = 30 });
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "before"));
        h.Client.GiftBoard.EndPeriod();

        h.Client.GiftBoard.StartNewPeriod();
        var state = await h.Client.GiftBoard.CurrentAsync(h.Stop.Token);
        Assert.Empty(state.Rows);
        Assert.Equal(GiftBoardOptions.Default with { Threshold = 5, Mascot = false, Volume = 30 }, state.Options);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "after"));

        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
        Assert.Equal(2L, TestDatabase.GiftBoardPeriods(h.DataDirectory));
    }

    // A gift goes on the board before it is written, so the streamer can start a new 期 in between; it was counted in the
    // 期 before, and that is the one its row names.
    [Fact]
    public async Task A_gift_counted_just_before_a_new_period_is_written_into_the_period_it_was_counted_in()
    {
        const long room = 7734200;
        await using var h = new WatchHarness();
        var board = h.Client.GiftBoard;
        Gift gift = new(10001, "测试观众", 1000) { GiftId = 1, GiftName = "礼物", Price = 1000, Num = 1, Value = 1000, Tid = "between" };
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            var counted = board.Take(room, [gift]);
            board.EndPeriod();
            board.StartNewPeriod();
            new GiftStore(new Database(h.DataDirectory), h.Logger).Save(room, gift, forBlindBox: false, counted);
            Assert.Empty(await h.BoardRowsAsync());
        }

        await using var next = await h.WatchConnectedAsync();
        Assert.Empty(await h.BoardRowsAsync());
        Assert.Equal(1L, TestDatabase.GiftRecords(h.DataDirectory));
    }

    [Fact]
    public async Task A_new_period_waits_for_the_running_one_to_end_and_only_an_ended_one_continues()
    {
        await using var h = new WatchHarness(enableLotteryAndGiftBoard: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var board = h.Client.GiftBoard;
        Assert.Equal("没有在统计的一期。", Assert.Throws<InvalidOperationException>(board.EndPeriod).Message);
        Assert.Equal("没有结束的一期可以继续。", Assert.Throws<InvalidOperationException>(board.ContinuePeriod).Message);

        board.StartNewPeriod();
        Assert.Equal("这一期还在统计，先结束再开始新一期。", Assert.Throws<InvalidOperationException>(board.StartNewPeriod).Message);
        Assert.Throws<InvalidOperationException>(board.ContinuePeriod);
        board.EndPeriod();
        Assert.Throws<InvalidOperationException>(board.EndPeriod);

        Assert.Equal(1L, TestDatabase.GiftBoardPeriods(h.DataDirectory));
    }

    [Fact]
    public async Task A_reconnect_a_new_room_connection_or_a_restart_leaves_the_period_as_it_was()
    {
        await using var h = new WatchHarness();
        await using (var updates = await h.WatchWithGiftBoardAsync(threshold: 5))
        {
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"));
            await h.Server.DisconnectAsync();
            Assert.IsType<Reconnecting>(await h.NextAsync(updates));
            await h.AdvanceRetryAsync(updates, 1);
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "b"));
            Assert.True((await h.Client.GiftBoard.CurrentAsync(h.Stop.Token)).Shown);
        }
        await using (var next = await h.WatchConnectedAsync())
        {
            Assert.True((await h.Client.GiftBoard.CurrentAsync(h.Stop.Token)).Shown);
            h.Client.GiftBoard.EndPeriod();
        }

        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        await using var again = await h.WatchConnectedAsync(restarted);

        var state = await restarted.GiftBoard.CurrentAsync(h.Stop.Token);
        Assert.False(state.Shown);
        Assert.Equal(5, state.Options.Threshold);
        Assert.Equal(2, Assert.Single(state.Rows).Num);
        Assert.Equal(1L, TestDatabase.GiftBoardPeriods(h.DataDirectory));
    }

    [Fact]
    public async Task Each_room_has_its_own_periods()
    {
        await using var h = new WatchHarness();
        h.Http.SetRoom(roomId: 999);
        await h.WatchAndLeaveAGiftAsync();
        h.Http.SetRoom();
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            Assert.Empty(await h.BoardRowsAsync());
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 20002));
            h.Client.GiftBoard.EndPeriod();
        }

        h.Http.SetRoom(roomId: 999);
        await using var again = await h.WatchConnectedAsync();

        var state = await h.Client.GiftBoard.CurrentAsync(h.Stop.Token);
        Assert.True(state.Shown);
        Assert.Equal(["10001/gift:32698"], state.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task A_room_connection_without_the_gift_board_records_nothing_for_it()
    {
        await using var h = new WatchHarness(enableLotteryAndGiftBoard: true);
        await h.LoginAsync();
        await using (var gui = await h.WatchConnectedAsync()) h.Client.GiftBoard.StartNewPeriod();

        h.EnableLotteryAndGiftBoard = false;
        await using var cli = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(cli, BlindGiftFixture.Ordinary());

        Assert.False((await h.Client.GiftBoard.CurrentAsync(h.Stop.Token)).Connected);
        Assert.Equal(0L, TestDatabase.GiftRecords(h.DataDirectory));
    }

    [Fact]
    public async Task Without_a_room_connection_no_period_starts_and_no_option_changes()
    {
        await using var h = new WatchHarness();

        Assert.Throws<InvalidOperationException>(h.Client.GiftBoard.StartNewPeriod);
        Assert.Throws<InvalidOperationException>(() => h.Client.GiftBoard.SetOptions(GiftBoardOptions.Default));
    }

    [Theory]
    [InlineData(0, GiftBoardOptions.ByUnitPrice, 60, "上榜门槛必须是正整数。")]
    [InlineData(1, 3, 60, "门槛比法只能是 1 或 2。")]
    [InlineData(1, GiftBoardOptions.ByTotalPrice, 101, "音量必须是 0 到 100 的整数。")]
    public async Task Options_that_cannot_be_used_are_refused(int threshold, int thresholdBy, int volume, string error)
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        var refused = Assert.Throws<ArgumentException>(() =>
            h.Client.GiftBoard.SetOptions(new(threshold, thresholdBy, true, volume)));

        Assert.StartsWith(error, refused.Message);
        Assert.Equal(1, (await h.Client.GiftBoard.CurrentAsync(h.Stop.Token)).Options.Threshold);
    }
}
