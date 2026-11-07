using System.Runtime.CompilerServices;

namespace Aether.Core;

/// <summary>The 礼物榜页: read-only, it only shows what <see cref="GiftBoardFacade"/> reports.</summary>
public sealed class GiftBoardPage(GiftBoardFacade board) : WebPage("gift-board")
{
    /// <summary>SSE events: a state snapshot first, then one per change.</summary>
    internal override async IAsyncEnumerable<SseEvent> EventsAsync([EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var state in board.GetUpdatesAsync(token)) yield return new("state", state);
    }
}
