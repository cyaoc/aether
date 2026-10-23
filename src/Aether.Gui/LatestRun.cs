namespace Aether.Gui;

internal sealed class LatestRun
{
    // Each run reports its own failure; it must not poison a replacement or shutdown.
    private CancellationTokenSource? stop;
    public Task Current { get; private set; } = Task.CompletedTask;

    public Task Start(Func<CancellationToken, Task> run)
    {
        stop?.Cancel();
        var previous = Current;
        var next = new CancellationTokenSource();
        stop = next;
        return Current = RunAsync();

        async Task RunAsync()
        {
            try
            {
                try { await previous; }
                catch { }
                await run(next.Token);
            }
            finally
            {
                if (ReferenceEquals(stop, next)) stop = null;
                next.Dispose();
            }
        }
    }

    public async Task StopAsync()
    {
        stop?.Cancel();
        try { await Current; }
        catch { }
    }
}
