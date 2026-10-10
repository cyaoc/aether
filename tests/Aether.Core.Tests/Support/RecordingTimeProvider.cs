using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Core.Tests.Support;

internal sealed class RecordingTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly ConcurrentQueue<TimeSpan> timerDelays = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        timerDelays.Enqueue(dueTime);
        return timer;
    }

    // A background loop can report its status before it arms its delay; advancing sooner shifts the timer's deadline.
    // count includes earlier timers of the same length, such as each room connection's 60 s idle timeout.
    public Task WaitForTimerAsync(TimeSpan dueTime, int count = 1) =>
        Eventually.TrueAsync(() => timerDelays.Count(delay => delay == dueTime) >= count);
}
