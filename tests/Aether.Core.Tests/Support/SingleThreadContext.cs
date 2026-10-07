using System.Collections.Concurrent;

namespace Aether.Core.Tests.Support;

/// <summary>A shell's UI thread: everything posted here runs in order on one dedicated thread.</summary>
internal sealed class SingleThreadContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();
    private readonly Thread thread;

    public SingleThreadContext()
    {
        thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in queue.GetConsumingEnumerable()) callback(state);
        }) { IsBackground = true };
        thread.Start();
    }

    public int ThreadId => thread.ManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));

    /// <summary>Runs <paramref name="action"/> on this thread, with every await in it returning here.</summary>
    public Task RunAsync(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try
            {
                await action();
                done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
        }, null);
        return done.Task;
    }

    public void Dispose() => queue.CompleteAdding();
}
