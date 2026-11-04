using Aether.Core;

namespace Aether.Gui.Tests.Support;

/// <summary>Puts a 礼物榜 on a harness's client the way a room connection would, without one.</summary>
internal static class GiftBoardFixture
{
    public const long Room = 1768506153;

    /// <summary>The board as it is now: a new subscription starts with the current state.</summary>
    public static async Task<GiftBoardState> CurrentAsync(this GiftBoardController board)
    {
        await foreach (var state in board.GetUpdatesAsync(TestContext.Current.CancellationToken)) return state;
        throw new InvalidOperationException("The gift board ended without a state.");
    }
}
