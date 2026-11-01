namespace Aether.Core;

/// <summary>Checks the latest receive time under its owner's gate, including callbacks already in flight.</summary>
internal sealed class QuietTimer(TimeProvider time, object gate, Action quiet) : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
    private ITimer? timer;
    private long lastReceived;
    private bool disposed;

    public void Reset(long receivedAt)
    {
        lastReceived = receivedAt;
        timer ??= time.CreateTimer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Check();
    }

    public void Check()
    {
        lock (gate)
        {
            if (disposed) return;
            var remaining = Window - time.GetElapsedTime(lastReceived);
            if (remaining > TimeSpan.Zero) timer!.Change(remaining, Timeout.InfiniteTimeSpan);
            else quiet();
        }
    }

    public void Dispose()
    {
        disposed = true;
        timer?.Dispose();
    }
}
