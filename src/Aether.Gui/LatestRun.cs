namespace Aether.Gui;

internal sealed class LatestRun
{
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
                await previous;
                await run(next.Token);
            }
            finally
            {
                if (ReferenceEquals(stop, next)) stop = null;
                next.Dispose();
            }
        }
    }

    public Task StopAsync()
    {
        stop?.Cancel();
        return Current;
    }
}
