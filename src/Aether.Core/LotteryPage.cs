using System.Runtime.CompilerServices;

namespace Aether.Core;

/// <summary>The 抽奖页: read-only, it only shows what <see cref="LotteryFacade"/> reports.</summary>
public sealed class LotteryPage(WebService service, LotteryFacade lottery) : WebPage("lottery")
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

    public async Task PlayExampleAsync()
    {
        var status = service.Status;
        if (!status.Running)
            throw new InvalidOperationException(status.Error ?? "网页服务没有运行，请先在通用设置中打开网页服务。");
        await lottery.PlayExampleAsync();
    }

    internal override async Task RunAsync(CancellationToken token)
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { lottery.StopExample(); }
    }
}
