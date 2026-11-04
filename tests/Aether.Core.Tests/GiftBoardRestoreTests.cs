using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class GiftBoardRestoreTests
{
    private const long ShowA = 1700000000;
    private const long ShowB = 1700100000;
    // Short enough that two steps between messages stay inside the room connection's 60 s idle limit.
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Restoring_merges_the_board_into_the_one_before_by_viewer_and_gift()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(nickname: "旧昵称", num: 2, tid: "a"),
            BlindGiftFixture.Ordinary(uid: 10002, nickname: "乙", tid: "b"), UserToastFixture.Message(uid: 10003, price: 198000, payflowId: "p1"));
        h.Time.Advance(Step);
        h.Client.GiftBoard.Clear();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(nickname: "新昵称", num: 3, tid: "c"),
            UserToastFixture.Message(uid: 10003, price: 534000, num: 3, payflowId: "p2"), BlindGiftFixture.Ordinary(uid: 10004, nickname: "丁", tid: "d"));

        h.Client.GiftBoard.Restore();

        // 舰长 cost 198000 for one month and 534000 for three, so four months at 183000 each.
        Assert.Equal([("10003/guard:3", 183000L, 4L), ("10001/gift:32698", 1500L, 5L), ("10002/gift:32698", 1500L, 1L), ("10004/gift:32698", 1500L, 1L)],
            (await h.BoardRowsAsync()).Select(row => (row.Id, row.Price, row.Num)));
        Assert.Equal("新昵称", (await h.BoardRowsAsync()).Single(row => row.Id == "10001/gift:32698").Nickname);
        Assert.Single(TestDatabase.GiftBoardPeriods(h.DataDirectory));
    }

    [Fact]
    public async Task A_merged_row_spans_from_the_earliest_first_time_to_the_latest_last_time()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        var first = h.Time.GetUtcNow();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"));
        h.Time.Advance(Step);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "b"));
        h.Time.Advance(Step);
        h.Client.GiftBoard.Clear();
        h.Time.Advance(Step);
        var last = h.Time.GetUtcNow();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "c"));

        h.Client.GiftBoard.Restore();

        Assert.Equal((first, last), Assert.Single(TestDatabase.GiftBoardTimes(h.DataDirectory)));
    }

    [Fact]
    public async Task Restoring_again_walks_back_one_board_at_a_time()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        foreach (var uid in new[] { 1, 2, 3 })
        {
            if (uid > 1) h.Client.GiftBoard.Clear();
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: uid, tid: null));
            h.Time.Advance(Step);
        }

        h.Client.GiftBoard.Restore();
        Assert.Equal(["2/gift:32698", "3/gift:32698"], (await h.BoardRowsAsync()).Select(row => row.Id));
        h.Client.GiftBoard.Restore();
        Assert.Equal(["1/gift:32698", "2/gift:32698", "3/gift:32698"], (await h.BoardRowsAsync()).Select(row => row.Id));
    }

    [Fact]
    public async Task Nothing_can_be_restored_without_an_earlier_board()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());

        Assert.Throws<InvalidOperationException>(h.Client.GiftBoard.Restore);

        Assert.Single(await h.BoardRowsAsync());
    }

    [Fact]
    public async Task The_boards_of_other_rooms_are_not_earlier_boards_of_this_one()
    {
        await using var h = new WatchHarness();
        h.Http.SetRoom(roomId: 999);
        await using (var other = await h.WatchWithGiftBoardAsync())
            await h.PushAndSyncAsync(other, BlindGiftFixture.Ordinary());
        h.Http.SetRoom();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 20002));

        Assert.Throws<InvalidOperationException>(h.Client.GiftBoard.Restore);
    }

    [Fact]
    public async Task Restoring_after_a_new_show_opened_a_board_brings_the_old_one_back_and_remembers_the_show()
    {
        await using var h = new WatchHarness();
        h.Http.SetRoom(liveStatus: 1, liveTime: ShowA);
        await using (var updates = await h.WatchWithGiftBoardAsync())
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"));
        h.Http.SetRoom(liveStatus: 1, liveTime: ShowB);
        await using (var next = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(next, BlindGiftFixture.Ordinary(uid: 20002, tid: "b"));
            h.Client.GiftBoard.Restore();
        }

        await using var again = await h.WatchConnectedAsync();

        Assert.Equal(2, (await h.BoardRowsAsync()).Length);
        Assert.Equal<long?>([ShowB], TestDatabase.GiftBoardPeriods(h.DataDirectory).Select(period => period.LiveTime));
    }

    [Fact]
    public async Task Restoring_does_not_touch_the_blind_box_statistics()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Json);
        h.Client.GiftBoard.Clear();
        var before = TestDatabase.BlindBoxRows(h.DataDirectory);
        Assert.NotEmpty(before);

        h.Client.GiftBoard.Restore();

        Assert.Equal(before, TestDatabase.BlindBoxRows(h.DataDirectory));
    }

    [Fact]
    public async Task The_state_tells_when_the_board_began_and_how_many_viewers_are_on_it()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        Assert.Equal((null, null), await Periods(h));

        var began = h.Time.GetUtcNow();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 1, tid: "a"), BlindGiftFixture.Ordinary(uid: 2, tid: "b"),
            BlindGiftFixture.Ordinary(uid: 2, giftId: 7, tid: "c"));

        Assert.Equal((new GiftBoardPeriod(began, 2), null), await Periods(h));
    }

    [Fact]
    public async Task The_state_tells_what_a_restore_would_merge_back_and_whether_the_board_is_empty()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        var first = h.Time.GetUtcNow();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 1, tid: "a"), BlindGiftFixture.Ordinary(uid: 2, tid: "b"));
        h.Time.Advance(Step);
        var cleared = h.Time.GetUtcNow();

        h.Client.GiftBoard.Clear();
        Assert.Equal((new GiftBoardPeriod(cleared, 0), new GiftBoardPeriod(first, 2)), await Periods(h));

        h.Client.GiftBoard.Restore();
        Assert.Equal((new GiftBoardPeriod(first, 2), null), await Periods(h));
    }

    private static async Task<(GiftBoardPeriod? Period, GiftBoardPeriod? Previous)> Periods(WatchHarness h)
    {
        var state = await h.Client.GiftBoard.CurrentAsync(h.Stop.Token);
        return (state.Period, state.Previous);
    }
}
