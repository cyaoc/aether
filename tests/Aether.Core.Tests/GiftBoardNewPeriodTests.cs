using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class GiftBoardNewPeriodTests
{
    [Fact]
    public async Task A_new_period_starts_empty_and_the_next_gift_starts_from_zero()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "before"));
        await using var states = h.Client.GiftBoard.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.Single((await h.NextAsync(states)).Rows);

        h.Client.GiftBoard.StartNewPeriod();
        Assert.Empty((await h.NextAsync(states)).Rows);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "after"));

        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
        Assert.Equal(2L, TestDatabase.GiftBoardPeriods(h.DataDirectory));
    }

    [Fact]
    public async Task A_new_period_leaves_the_boards_of_other_rooms_alone()
    {
        await using var h = new WatchHarness();
        h.Http.SetRoom(roomId: 999);
        await h.WatchAndLeaveAGiftAsync();
        h.Http.SetRoom();
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 20002));
            h.Client.GiftBoard.StartNewPeriod();
        }

        h.Http.SetRoom(roomId: 999);
        await using var again = await h.WatchConnectedAsync();

        Assert.Equal(["10001/gift:32698"], (await h.BoardRowsAsync()).Select(row => row.Id));
    }

    [Fact]
    public async Task A_new_period_needs_a_room_connection()
    {
        await using var h = new WatchHarness();

        Assert.Throws<InvalidOperationException>(h.Client.GiftBoard.StartNewPeriod);
    }
}
