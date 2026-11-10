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
            var state = (update.Example ?? update.Live) with { Theme = update.State.Theme, IsExamplePlaying = update.State.IsExamplePlaying };
            yield return new("state", state);
        }
    }

    /// <summary>Plays the example while the web service serves this page; refused with why not, or while one plays. The
    /// service stopping stops it.</summary>
    public Task PlayExampleAsync() => lottery.PlayExampleAsync();
}
