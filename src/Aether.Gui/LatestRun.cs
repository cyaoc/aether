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

    /// <summary>Applies each update Core reports until the next run or a stop; a failure goes to <paramref name="failed"/> and
    /// ends the run.</summary>
    public Task Follow<T>(Func<CancellationToken, IAsyncEnumerable<T>> updates, Action<T> apply, Action<Exception> failed) => Start(async token =>
    {
        try { await foreach (var update in updates(token)) apply(update); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { failed(error); }
    });

    public async Task StopAsync()
    {
        stop?.Cancel();
        try { await Current; }
        catch { }
    }
}
