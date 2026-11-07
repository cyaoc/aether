namespace Aether.Core;

/// <summary>Part of its owner's region state (ADR 0017): when due, the timer only posts a check, which reads the latest receive
/// time in a step of that region; a region already stopped drops it.</summary>
internal sealed class QuietTimer : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
    private readonly TimeProvider time;
    private readonly Action quiet;
    private readonly ITimer timer;
    private long lastReceived;
    private bool disposed;

    public QuietTimer(TimeProvider time, Action<Action> post, Action quiet)
    {
        this.time = time;
        this.quiet = quiet;
        timer = time.CreateTimer(_ => post(Check), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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
        var remaining = Window - time.GetElapsedTime(lastReceived);
        if (remaining > TimeSpan.Zero) timer.Change(remaining, Timeout.InfiniteTimeSpan);
        else quiet();
    }

    public void Dispose()
    {
        disposed = true;
        timer.Dispose();
    }
}
