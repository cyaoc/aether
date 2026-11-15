namespace Aether.Core;

/// <summary>Part of its owner's region state (ADR 0017): when due, the timer only posts a check, which reads the latest receive
/// time in a step of that region; a region already stopped drops it.</summary>
internal sealed class QuietTimer : IDisposable
{
    // The quiet that ends a 一轮送礼, and a 一轮盲盒.
    private static readonly TimeSpan RoundWindow = TimeSpan.FromSeconds(3);
    private readonly TimeProvider time;
    private readonly TimeSpan window;
    private readonly Action quiet;
    private readonly ITimer timer;
    private long lastReceived;
    private bool disposed;

    public QuietTimer(StepTimers timers, Action quiet) : this(timers, RoundWindow, quiet) { }

    /// <summary>Runs <paramref name="quiet"/> once nothing was received for <paramref name="window"/>.</summary>
    public QuietTimer(StepTimers timers, TimeSpan window, Action quiet)
    {
        time = timers.Time;
        this.window = window;
        this.quiet = quiet;
        timer = timers.Start(Timeout.InfiniteTimeSpan, Check);
    }

    public void Reset(long receivedAt)
    {
        lastReceived = receivedAt;
        Check();
    }

    public void Check()
    {
        // A check posted before its owner disposed it still runs while the region does.
        if (disposed) return;
        var remaining = window - time.GetElapsedTime(lastReceived);
        if (remaining > TimeSpan.Zero) timer.Change(remaining, Timeout.InfiniteTimeSpan);
        else quiet();
    }

    public void Dispose()
    {
        disposed = true;
        timer.Dispose();
    }
}
