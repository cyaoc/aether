using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>ADR 0017: one region's state, run one step at a time in the order the steps came. A step is a synchronous function
/// that gets the state only while it runs; it touches no other region's state, and waits for nothing.</summary>
internal sealed class Region<TState>
{
    private const string NoAwait = "一步里不能 await：要等的放在外面，碰状态时再交一步（ADR 0017）";
    private readonly Channel<Action<TState>> steps = Channel.CreateUnbounded<Action<TState>>(new() { SingleReader = true });
    private readonly PendingSteps pending;
    private readonly ILogger logger;

    public Region(TState state, PendingSteps pending, ILogger logger)
    {
        this.pending = pending;
        this.logger = logger;
        _ = RunAsync(state);
    }

    /// <summary>The step's result, once the steps before it have run; what it throws is thrown here. Cancelled once the region
    /// has stopped.</summary>
    public Task<T> Run<T>(Func<TState, T> step)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Enqueue(state =>
        {
            try { result.SetResult(step(state)); }
            catch (Exception error) { result.SetException(error); }
        })) result.SetCanceled();
        return result.Task;
    }

    public Task Run(Action<TState> step) => Run(state =>
    {
        step(state);
        return true;
    });

    /// <summary>Queues the step without waiting for it; what it throws is logged. Dropped once the region has stopped.</summary>
    public void Post(Action<TState> step) => Enqueue(step);

    [Obsolete(NoAwait, error: true)]
    public Task Run(Func<TState, Task> step) => throw new NotSupportedException(NoAwait);

    [Obsolete(NoAwait, error: true)]
    public Task<T> Run<T>(Func<TState, Task<T>> step) => throw new NotSupportedException(NoAwait);

    /// <summary>The steps already queued still run; later ones are dropped or cancelled.</summary>
    public void Stop() => steps.Writer.TryComplete();

    /// <summary>One reader's updates (ADR 0017 convention 11): a step writes it the current state and adds it to the list, so it
    /// neither misses a change nor sees one twice; once it stops reading, a posted step takes it off.</summary>
    public IAsyncEnumerable<T> SubscribeAsync<T>(Func<TState, Subscribers<T>> subscribers, Func<TState, T> current,
        CancellationToken token) => Subscribers<T>.ReadAsync(
            subscriber => Run(state => subscribers(state).Add(subscriber, current(state))),
            subscriber => Post(state => subscribers(state).Remove(subscriber)), token);

    private bool Enqueue(Action<TState> step)
    {
        // Counted before it is queued, so a step posting to another region keeps the count above zero throughout.
        pending.Add();
        if (steps.Writer.TryWrite(step)) return true;
        pending.Remove();
        return false;
    }

    private async Task RunAsync(TState state)
    {
        await foreach (var step in steps.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try { step(state); }
            catch (Exception error) { logger.LogError(error, "{Region}区域投递的一步出错：{Error}", typeof(TState).Name, error.Message); }
            finally { pending.Remove(); }
        }
    }
}

/// <summary>How many steps a client's regions have still to run, counted together: closing and tests wait for none
/// (<see cref="AetherClient.IdleAsync"/>). It waits for no outside flow, such as a network request.</summary>
internal sealed class PendingSteps
{
    private Pending pending = new(0, new());

    public void Add() => Change(1);

    public void Remove()
    {
        if (Change(-1) is { Count: 0, Idle: var idle }) idle.TrySetResult();
    }

    public Task IdleAsync() => Volatile.Read(ref pending) is { Count: > 0, Idle: var idle } ? idle.Task : Task.CompletedTask;

    // The count and the wait for its next zero change together, so no wait misses a zero or ends early on the last one.
    private Pending Change(int by)
    {
        while (true)
        {
            var before = Volatile.Read(ref pending);
            var after = new Pending(before.Count + by,
                before.Count == 0 ? new(TaskCreationOptions.RunContinuationsAsynchronously) : before.Idle);
            if (ReferenceEquals(Interlocked.CompareExchange(ref pending, after, before), before)) return after;
        }
    }

    private sealed record Pending(int Count, TaskCompletionSource Idle);
}
