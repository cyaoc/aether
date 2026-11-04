using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class GiftBoardPushTests
{
    [Fact]
    public async Task The_current_state_comes_first_and_every_change_follows()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await h.ApplySettingsAsync(("gift_board.enabled", "true"), ("gift_board.threshold", "1"), ("gift_board.mascot", "false"), ("gift_board.volume", "0"));
        await using var states = h.Client.GiftBoard.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);

        var first = await h.NextAsync(states);
        Assert.Equal((0, false, 0), (first.Rows.Length, first.Mascot, first.Volume));

        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
        await h.ReadUntilAsync(states, state => state.Rows.Length == 1);

        await h.ApplySettingsAsync(("gift_board.mascot", "true"), ("gift_board.volume", "30"));
        var changed = await h.ReadUntilAsync(states, state => state.Mascot);
        Assert.Equal((1, 30), (changed[^1].Rows.Length, changed[^1].Volume));
    }

    // The 礼物榜页 plays an animation only for what a gift changed; every other state is drawn as it is.
    [Fact]
    public async Task Only_the_state_a_received_gift_made_comes_from_a_gift()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await using var states = h.Client.GiftBoard.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.False((await h.NextAsync(states)).FromGift);

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
        Assert.True((await h.NextAsync(states)).FromGift);

        h.Client.GiftBoard.Clear();
        Assert.False((await h.NextAsync(states)).FromGift);
        h.Client.GiftBoard.Restore();
        Assert.False((await h.NextAsync(states)).FromGift);
        await h.ApplySettingsAsync(("gift_board.mascot", "false"));
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
    public async Task The_board_is_empty_when_it_is_off_or_there_is_no_room_connection()
    {
        await using var h = new WatchHarness();
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
            Assert.Single(await h.BoardRowsAsync());

            await h.ApplySettingsAsync(("gift_board.enabled", "false"));
            Assert.Empty(await h.BoardRowsAsync());
            await h.ApplySettingsAsync(("gift_board.enabled", "true"));
            Assert.Single(await h.BoardRowsAsync());
        }

        Assert.Empty(await h.BoardRowsAsync());
        await using var next = await h.WatchConnectedAsync();
        Assert.Single(await h.BoardRowsAsync());
    }

    [Fact]
    public async Task Turning_the_board_off_pushes_an_empty_board()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();
        await using var states = await SubscribeAfterAGiftAsync(h, updates);

        await h.ApplySettingsAsync(("gift_board.enabled", "false"));

        Assert.Empty((await h.NextAsync(states)).Rows);
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

    [Fact]
    public async Task Another_room_shows_its_own_board_and_the_first_comes_back_with_it()
    {
        await using var h = new WatchHarness();
        await using (var first = await h.WatchWithGiftBoardAsync())
            await h.PushAndSyncAsync(first, BlindGiftFixture.Ordinary(tid: "first-room"));

        h.Http.SetRoom(roomId: 999);
        await using (var other = await h.WatchConnectedAsync())
        {
            Assert.Empty(await h.BoardRowsAsync());
            await h.PushAndSyncAsync(other, BlindGiftFixture.Ordinary(uid: 20002, tid: "other-room"));
            Assert.Equal(["20002/gift:32698"], (await h.BoardRowsAsync()).Select(row => row.Id));
        }

        h.Http.SetRoom();
        await using var again = await h.WatchConnectedAsync();
        Assert.Equal(["10001/gift:32698"], (await h.BoardRowsAsync()).Select(row => row.Id));
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
