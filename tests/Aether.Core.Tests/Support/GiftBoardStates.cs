using System.Collections.Immutable;

namespace Aether.Core.Tests.Support;

internal static class GiftBoardStates
{
    /// <summary>When two shows began (Unix seconds); the second is more than a day after the first.</summary>
    public const long ShowA = 1700000000, ShowB = 1700100000;

    /// <summary>The board as it is now: a new subscription starts with the current state.</summary>
    public static async Task<GiftBoardState> CurrentAsync(this GiftBoardController board, CancellationToken token)
    {
        await foreach (var state in board.GetUpdatesAsync(token)) return state;
        throw new InvalidOperationException("The gift board ended without a state.");
    }

    public static async Task<ImmutableArray<GiftBoardRow>> BoardRowsAsync(this WatchHarness h) =>
        (await h.Client.GiftBoard.CurrentAsync(h.Stop.Token)).Rows;

    /// <summary>Logs in, turns the gift board on at <paramref name="threshold"/> 电池 compared as <paramref name="thresholdBy"/>
    /// (see <see cref="Settings.GiftBoardByUnitPrice"/>), and watches room 6 until it is connected.</summary>
    public static async Task<IAsyncEnumerator<WatchUpdate>> WatchWithGiftBoardAsync(this WatchHarness h, int threshold = 1,
        int thresholdBy = Settings.GiftBoardByUnitPrice)
    {
        await h.LoginAsync();
        await h.ApplySettingsAsync(("gift_board.enabled", "true"), ("gift_board.threshold", threshold.ToString()),
            ("gift_board.threshold_by", thresholdBy.ToString()));
        return await h.WatchConnectedAsync();
    }

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
