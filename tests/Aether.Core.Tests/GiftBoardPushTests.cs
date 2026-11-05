using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.GiftBoardStates;

namespace Aether.Core.Tests;

public sealed class GiftBoardPushTests
{
    [Fact]
    public async Task The_current_state_comes_first_and_every_change_follows()
    {
        await using var h = new WatchHarness();
        await using var states = h.Client.GiftBoard.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);

        var first = await h.NextAsync(states);
        Assert.Equal((0, false, false), (first.Rows.Length, first.Running, first.Connected));

        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
        await h.ReadUntilAsync(states, state => state.Rows.Length == 1);

        await h.SetOptionsAsync(options => options with { Mascot = false, Volume = 30 });
        var changed = await h.ReadUntilAsync(states, state => !state.Options.Mascot);
        Assert.Equal((1, 30), (changed[^1].Rows.Length, changed[^1].Options.Volume));
    }

    // The 礼物榜页 plays an animation only for what a gift changed; every other state is drawn as it is.
    [Fact]
    public async Task Only_the_state_a_received_gift_made_comes_from_a_gift()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        var board = h.Client.GiftBoard;
        await using var states = board.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.False((await h.NextAsync(states)).FromGift);

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
        Assert.True((await h.NextAsync(states)).FromGift);

        foreach (var change in new Action[] { () => board.EndPeriod(RoomId), () => board.ContinuePeriod(RoomId), () => board.EndPeriod(RoomId), () => board.StartNewPeriod(RoomId) })
        {
            change();
            Assert.False((await h.NextAsync(states)).FromGift);
        }
        await h.SetOptionsAsync(options => options with { Mascot = false });
        Assert.False((await h.NextAsync(states)).FromGift);
        await updates.DisposeAsync();
        Assert.False((await h.NextAsync(states)).FromGift);
    }

    [Fact]
    public async Task A_subscriber_after_a_gift_starts_with_a_state_that_is_not_from_one()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());

        var state = await h.Client.GiftBoard.CurrentAsync(h.Stop.Token);

        Assert.Single(state.Rows);
        Assert.False(state.FromGift);
    }

    [Fact]
    public async Task The_board_is_empty_without_a_room_connection()
    {
        await using var h = new WatchHarness();
        await h.WatchAndLeaveAGiftAsync();

        Assert.Empty(await h.BoardRowsAsync());
        await using var next = await h.WatchConnectedAsync();
        Assert.Single(await h.BoardRowsAsync());
    }

    [Fact]
    public async Task Ending_the_period_pushes_its_rows_as_no_longer_shown()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await using var states = await SubscribeAfterAGiftAsync(h, updates);

        h.Client.GiftBoard.EndPeriod(RoomId);

        var ended = await h.NextAsync(states);
        Assert.False(ended.Running);
        Assert.Single(ended.Rows);
    }

    [Fact]
    public async Task Ending_the_room_connection_pushes_an_empty_board()
    {
        await using var h = new WatchHarness();
        var updates = await h.WatchWithGiftBoardAsync();
        await using var states = await SubscribeAfterAGiftAsync(h, updates);

        await updates.DisposeAsync();

        Assert.Empty((await h.NextAsync(states)).Rows);
    }

    [Fact]
    public async Task A_reconnect_leaves_the_board_as_it_is()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await using var states = await SubscribeAfterAGiftAsync(h, updates);

        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        await h.AdvanceRetryAsync(updates, 1);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "another"));

        // The next state pushed is the one the new gift made: nothing, least of all an empty board, came between.
        Assert.Equal(2, Assert.Single((await h.NextAsync(states)).Rows).Num);
    }

    /// <summary>Puts one gift on the board and subscribes, so the first state read is the board with that gift on it.</summary>
    private static async Task<IAsyncEnumerator<GiftBoardState>> SubscribeAfterAGiftAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates)
    {
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
        var states = h.Client.GiftBoard.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.Single((await h.NextAsync(states)).Rows);
        return states;
    }
}
