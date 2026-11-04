using Aether.Core;

namespace Aether.Gui.Tests.Support;

internal static class GiftBoardStates
{
    /// <summary>The board as it is now: a new subscription starts with the current state.</summary>
    public static async Task<GiftBoardState> CurrentAsync(this GiftBoardController board)
    {
        await foreach (var state in board.GetUpdatesAsync(TestContext.Current.CancellationToken)) return state;
        throw new InvalidOperationException("The gift board ended without a state.");
    }
}
