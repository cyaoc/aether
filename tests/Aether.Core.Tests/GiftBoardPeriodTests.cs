using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>When the 礼物榜 moves on to a new 期 by itself: a room_init that says the room is live, or a LIVE message.</summary>
public sealed class GiftBoardPeriodTests
{
    // Unix times two shows began at; the second is more than a day after the first.
    private const long ShowA = 1700000000;
    private const long ShowB = 1700100000;

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
