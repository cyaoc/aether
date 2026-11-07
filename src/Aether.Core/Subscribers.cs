using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Aether.Core;

/// <summary>The readers of one kind of update. It has no lock of its own: its owner adds, removes and publishes where it changes
/// its state, which for a region is in its steps.</summary>
internal sealed class Subscribers<T>
{
    private readonly List<ChannelWriter<T>> subscribers = [];

    public void Add(ChannelWriter<T> subscriber, T current)
    {
        subscriber.TryWrite(current);
        subscribers.Add(subscriber);
    }

    public void Remove(ChannelWriter<T> subscriber) => subscribers.Remove(subscriber);

    public void Publish(T update)
    {
        foreach (var subscriber in subscribers) subscriber.TryWrite(update);
    }

    public void Complete()
    {
        foreach (var subscriber in subscribers) subscriber.TryComplete();
        subscribers.Clear();
    }

    /// <summary>One reader's updates: <paramref name="add"/> and <paramref name="remove"/> run where the owner changes the list.</summary>
    public static async IAsyncEnumerable<T> ReadAsync(Func<ChannelWriter<T>, Task> add, Action<ChannelWriter<T>> remove,
        [EnumeratorCancellation] CancellationToken token)
    {
        // shortcut: local readers use unbounded streams like WatchAsync; bound these if a stalled shell becomes a problem.
        var updates = Channel.CreateUnbounded<T>();
        await add(updates.Writer);
        try
        {
            await foreach (var update in updates.Reader.ReadAllAsync(token)) yield return update;
        }
        finally { remove(updates.Writer); }
    }
}
