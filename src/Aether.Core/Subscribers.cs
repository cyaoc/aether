using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Aether.Core;

internal sealed class Subscribers<T>(object gate)
{
    private readonly List<Channel<T>> subscribers = [];

    public async IAsyncEnumerable<T> SubscribeAsync(Func<T> first, [EnumeratorCancellation] CancellationToken token)
    {
        // shortcut: local readers use unbounded streams like WatchAsync; bound these if a stalled shell becomes a problem.
        var updates = Channel.CreateUnbounded<T>();
        try
        {
            lock (gate) { updates.Writer.TryWrite(first()); subscribers.Add(updates); }
            await foreach (var update in updates.Reader.ReadAllAsync(token)) yield return update;
        }
        finally { lock (gate) subscribers.Remove(updates); }
    }

    public void Publish(T update)
    {
        lock (gate) foreach (var subscriber in subscribers) subscriber.Writer.TryWrite(update);
    }

    public void Complete()
    {
        lock (gate)
        {
            foreach (var subscriber in subscribers) subscriber.Writer.TryComplete();
            subscribers.Clear();
        }
    }
}
