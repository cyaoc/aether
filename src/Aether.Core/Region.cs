using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>Where a client's regions come from (ADR 0017): they share its clock and its logger, and count the steps they have
/// still to run together, which <see cref="IdleAsync"/> waits on.</summary>
internal sealed class Regions(TimeProvider time, ILogger logger)
{
    private readonly PendingSteps pending = new();

    public TimeProvider Time => time;

    public ILogger Logger => logger;

    /// <param name="state">Makes the region's state, given the timers it may post its steps with.</param>
    public Region<TState> Create<TState>(Func<StepTimers, TState> state) => new(state, time, pending, logger);

    /// <summary>Waits until every region has run the steps queued so far, and those they queue for each other. It does not wait
    /// for outside flows, such as a network request.</summary>
    public Task IdleAsync() => pending.IdleAsync();
}

/// <summary>ADR 0017: one region's state, run one step at a time in the order the steps came. A step is a synchronous function
/// that gets the state only while it runs; it touches no other region's state, and waits for nothing.</summary>
internal sealed class Region<TState>
{
    private const string NoAwait = "一步里不能 await：要等的放在外面，碰状态时再交一步（ADR 0017）";
    // A step returns what to do once the snapshots show it, such as completing its Run.
    private readonly Channel<Func<TState, Action?>> steps = Channel.CreateUnbounded<Func<TState, Action?>>(new() { SingleReader = true });
    private readonly List<Action> snapshots = [];
    // Each kind of update a reader subscribed to, which the stop ends.
    private readonly HashSet<Action> readers = [];
    private readonly TState state;
    private readonly PendingSteps pending;
    private readonly ILogger logger;

    public Region(Func<StepTimers, TState> state, TimeProvider time, PendingSteps pending, ILogger logger)
    {
        this.pending = pending;
        this.logger = logger;
        this.state = state(new(time, step => Post(_ => step())));
        _ = RunAsync();
    }

    /// <summary>The step's result, once the steps before it have run; what it throws is thrown here. Cancelled once the region
    /// has stopped.</summary>
    public Task<T> Run<T>(Func<TState, T> step)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Enqueue(state =>
        {
            try
            {
                var value = step(state);
                return () => result.SetResult(value);
            }
            catch (Exception error) { return () => result.SetException(error); }
        })) result.SetCanceled();
        return result.Task;
    }

    public Task Run(Action<TState> step) => Run(state =>
    {
        step(state);
        return true;
    });

    /// <summary>Queues the step without waiting for it; what it throws is logged. Dropped once the region has stopped.</summary>
    public void Post(Action<TState> step) => Enqueue(state =>
    {
        step(state);
        return null;
    });

    [Obsolete(NoAwait, error: true)]
    public Task Run(Func<TState, Task> step) => throw new NotSupportedException(NoAwait);

    [Obsolete(NoAwait, error: true)]
    public Task<T> Run<T>(Func<TState, Task<T>> step) => throw new NotSupportedException(NoAwait);

    [Obsolete(NoAwait, error: true)]
    public void Post(Func<TState, Task> step) => throw new NotSupportedException(NoAwait);

    /// <summary>A last step ends every reader's updates, after what was reported so far; the steps queued before it still run,
    /// and later ones are dropped or cancelled.</summary>
    public void Stop()
    {
        Post(_ =>
        {
            foreach (var end in readers) end();
        });
        steps.Writer.TryComplete();
    }

    /// <summary>What <paramref name="read"/> gives as the last step left the state (ADR 0017 convention 6), for anyone to read at
    /// any time; a step's Run completes once it shows. Made with the region, before its first step.</summary>
    public Func<T> Snapshot<T>(Func<TState, T> read) where T : class?
    {
        var latest = read(state);
        snapshots.Add(() => Volatile.Write(ref latest, read(state)));
        return () => Volatile.Read(ref latest);
    }

    /// <summary>One reader's updates (ADR 0017 convention 11): a step writes it the current state and adds it to the list, so it
    /// neither misses a change nor sees one twice; once it stops reading, a posted step takes it off.</summary>
    public IAsyncEnumerable<T> SubscribeAsync<T>(Func<TState, Subscribers<T>> subscribers, Func<TState, T> current,
        CancellationToken token) => Subscribers<T>.ReadAsync(
            subscriber => Run(state =>
            {
                readers.Add(subscribers(state).Complete);
                subscribers(state).Add(subscriber, current(state));
            }),
            subscriber => Post(state => subscribers(state).Remove(subscriber)), token);

    private bool Enqueue(Func<TState, Action?> step)
    {
        // Counted before it is queued, so a step posting to another region keeps the count above zero throughout.
        pending.Add();
        if (steps.Writer.TryWrite(step)) return true;
        pending.Remove();
        return false;
    }

    private async Task RunAsync()
    {
        await foreach (var step in steps.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Action? done = null;
            try { done = step(state); }
            catch (Exception error) { logger.LogError(error, "{Region}区域投递的一步出错：{Error}", typeof(TState).Name, error.Message); }
            finally
            {
                foreach (var snapshot in snapshots) snapshot();
                done?.Invoke();
                pending.Remove();
            }
        }
    }
}

/// <summary>A region state's timers (ADR 0017): when one is due it only posts its step to the region, which runs it like any
/// other and drops it once stopped.</summary>
internal sealed class StepTimers(TimeProvider time, Action<Action> post)
{
    public TimeProvider Time => time;

    /// <summary>Posts <paramref name="step"/> once <paramref name="due"/>; Change arms it again, Dispose stops it.</summary>
    public ITimer Start(TimeSpan due, Action step) => time.CreateTimer(_ => post(step), null, due, Timeout.InfiniteTimeSpan);
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
