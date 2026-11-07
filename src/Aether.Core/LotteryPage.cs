using System.Runtime.CompilerServices;

namespace Aether.Core;

/// <summary>The 抽奖页: read-only, it only shows what <see cref="LotteryFacade"/> reports.</summary>
public sealed class LotteryPage(LotteryFacade lottery) : WebPage("lottery")
{
    /// <summary>SSE events: a state snapshot first, then one per change.</summary>
    internal override async IAsyncEnumerable<SseEvent> EventsAsync([EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var update in lottery.GetUpdatesAsync(token))
        {
            // Once the room connection ends nobody can stop or hide what is on air, so the page clears until it reconnects.
            var live = update.State.CanControl ? update.State : update.State with { Lottery = null };
            var state = (update.Example ?? live) with { Theme = update.State.Theme, IsExamplePlaying = update.State.IsExamplePlaying };
            yield return new("state", state);
        }
    }

    /// <summary>Plays the example while the web service serves this page; refused with why not, or while one plays. The
    /// service stopping stops it.</summary>
    public Task PlayExampleAsync() => lottery.PlayExampleAsync();
}
