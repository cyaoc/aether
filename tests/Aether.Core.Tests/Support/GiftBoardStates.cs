using System.Collections.Immutable;

namespace Aether.Core.Tests.Support;

internal static class GiftBoardStates
{
    /// <summary>The room <see cref="FakeBilibiliHttp.SetRoom"/> answers with unless told otherwise.</summary>
    public const long RoomId = 7734200;

    /// <summary>The board as it is now: a new subscription starts with the current state.</summary>
    public static async Task<GiftBoardState> CurrentAsync(this GiftBoardController board, CancellationToken token)
    {
        await foreach (var state in board.GetUpdatesAsync(token)) return state;
        throw new InvalidOperationException("The gift board ended without a state.");
    }

    public static async Task<ImmutableArray<GiftBoardRow>> BoardRowsAsync(this WatchHarness h) =>
        (await h.Client.GiftBoard.CurrentAsync(h.Stop.Token)).Rows;

    /// <summary>Logs in, watches room 6 as the GUI does until it is connected, and runs a 期 there at <paramref name="threshold"/>
    /// 电池 compared as <paramref name="thresholdBy"/> (see <see cref="GiftBoardOptions.ByUnitPrice"/>), the other options at
    /// their defaults. A 期 already running goes on.</summary>
    public static async Task<IAsyncEnumerator<WatchUpdate>> WatchWithGiftBoardAsync(this WatchHarness h, int threshold = 1,
        int thresholdBy = GiftBoardOptions.ByUnitPrice)
    {
        await h.LoginAsync();
        h.EnableStreamOverlay = true;
        var updates = await h.WatchConnectedAsync();
        var board = h.Client.GiftBoard;
        var state = await board.CurrentAsync(h.Stop.Token);
        var room = state.RoomId ?? throw new InvalidOperationException("The gift board did not connect.");
        board.SetOptions(room, GiftBoardOptions.Default with { Threshold = threshold, ThresholdBy = thresholdBy });
        if (!state.Running) board.StartNewPeriod(room);
        return updates;
    }

    /// <summary>Changes some of the board's options, keeping the others.</summary>
    public static async Task SetOptionsAsync(this WatchHarness h, Func<GiftBoardOptions, GiftBoardOptions> change) =>
        h.Client.GiftBoard.SetOptions(RoomId, change((await h.Client.GiftBoard.CurrentAsync(h.Stop.Token)).Options));

    /// <summary>Like <see cref="WatchWithGiftBoardAsync"/> for the room as <see cref="FakeBilibiliHttp.SetRoom"/> has it, then puts
    /// one gift on the board and disconnects, so that the next connection meets a board with a gift on it.</summary>
    public static async Task WatchAndLeaveAGiftAsync(this WatchHarness h)
    {
        await using var updates = await h.WatchWithGiftBoardAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
    }

    /// <summary>Reads pushed states until one satisfies <paramref name="match"/>; returns all read, that one last.</summary>
    public static async Task<List<GiftBoardState>> ReadUntilAsync(this WatchHarness h, IAsyncEnumerator<GiftBoardState> states,
        Func<GiftBoardState, bool> match)
    {
        List<GiftBoardState> read = [];
        do read.Add(await h.NextAsync(states)); while (!match(read[^1]));
        return read;
    }
}
