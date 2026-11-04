using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class GiftBoardClearTests
{
    private const long ShowA = 1700000000;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Clearing_opens_a_new_board_that_the_next_gift_starts_from_zero(bool resetOnLive)
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.ApplySettingsAsync(("gift_board.reset_on_live", resetOnLive ? "true" : "false"));
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "before"));
        await using var states = h.Client.GiftBoard.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.Single((await h.NextAsync(states)).Rows);

        h.Client.GiftBoard.Clear();
        Assert.Empty((await h.NextAsync(states)).Rows);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "after"));

        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
        Assert.Equal(2, TestDatabase.GiftBoardPeriods(h.DataDirectory).Count);
    }

    [Fact]
    public async Task Clearing_while_live_remembers_the_show_so_connecting_again_does_not_open_another_board()
    {
        await using var h = new WatchHarness();
        h.Http.SetRoom(liveStatus: 1, liveTime: ShowA);
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            h.Client.GiftBoard.Clear();
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
        }

        await using var next = await h.WatchConnectedAsync();

        Assert.Single(await h.BoardRowsAsync());
        Assert.Equal<long?>([ShowA, ShowA], TestDatabase.GiftBoardPeriods(h.DataDirectory).Select(period => period.LiveTime));
    }

    [Fact]
    public async Task Clearing_leaves_the_boards_of_other_rooms_alone()
    {
        await using var h = new WatchHarness();
        h.Http.SetRoom(roomId: 999);
        await using (var other = await h.WatchWithGiftBoardAsync())
            await h.PushAndSyncAsync(other, BlindGiftFixture.Ordinary());
        h.Http.SetRoom();
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 20002));
            h.Client.GiftBoard.Clear();
        }

        h.Http.SetRoom(roomId: 999);
        await using var again = await h.WatchConnectedAsync();

        Assert.Equal(["10001/gift:32698"], (await h.BoardRowsAsync()).Select(row => row.Id));
    }

    [Fact]
    public async Task Clearing_does_not_touch_the_blind_box_statistics()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Json);
        var before = TestDatabase.BlindBoxRows(h.DataDirectory);
        Assert.NotEmpty(before);

        h.Client.GiftBoard.Clear();

        Assert.Equal(before, TestDatabase.BlindBoxRows(h.DataDirectory));
    }

    [Fact]
    public async Task Clearing_needs_a_room_connection()
    {
        await using var h = new WatchHarness();

        Assert.Throws<InvalidOperationException>(h.Client.GiftBoard.Clear);
    }
}
