using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room connection's 发送队列: blind box queries and announcements, paced to avoid B站's rate limits.
/// Reconnects keep it, pacing included; the end of the room connection clears it.</summary>
internal sealed class SendQueue(TimeProvider time, ILogger logger, TimeSpan interval)
{
    private static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(3);

    /// <summary>A queued 盲盒查询, carrying the keyword danmaku it answers, or a 盲盒播报, which has none.</summary>
    internal sealed record Item(long Uid, string Nickname, Danmaku? Query = null);

    /// <summary>The part of a 一轮盲盒 one announcement reports; a Count, since a box can be worth nothing.</summary>
    private readonly record struct Reported(BlindBoxTally Tally, long Count);

    private sealed class Round(Item item)
    {
        public Item Item = item;
        public BlindBoxTally Tally = new(0, 0);
        public long Count;
        public long LastReceived;
        public ITimer Timer = null!;
        public bool Queued;
    }

    // ponytail: unbounded in memory; add a capacity policy if keyword floods become a problem.
    private readonly Channel<Item> pending = Channel.CreateUnbounded<Item>(
        new UnboundedChannelOptions { SingleReader = true });
    // Receiving, quiet-window timers and sending share these under the gate.
    private readonly object gate = new();
    private readonly HashSet<long> waitingQueries = [];
    private readonly Dictionary<long, Round> rounds = [];
    // Only the sender touches these.
    private long? lastSendEnded;
    private bool rateLimitRetryOwed;

    /// <summary>Queues a 盲盒查询 reply to <paramref name="trigger"/>'s viewer, unless one is already waiting (the asks merge
    /// into the first).</summary>
    public void Enqueue(Danmaku trigger)
    {
        lock (gate)
            if (waitingQueries.Add(trigger.Uid)) pending.Writer.TryWrite(new(trigger.Uid, trigger.Nickname, trigger));
    }

    /// <summary>Records a whole gift message; <paramref name="save"/> returns whether a box was newly recorded and joins
    /// its viewer's 一轮盲盒.</summary>
    public void Record(BlindBox[] boxes, long receivedAt, Func<BlindBox, bool> save)
    {
        // A SQLite write can wait for another process; neither a deadline nor a send snapshot may split this message.
        // ponytail: the sender's snapshot and the quiet-window timers wait with the receive loop, up to SQLite's 30 s
        // busy timeout; mark the message as arriving under the gate and save outside it if that stall ever matters.
        lock (gate)
            foreach (var box in boxes)
                if (save(box)) Add(box, receivedAt);
    }

    private void Add(BlindBox box, long receivedAt)
    {
        if (!rounds.TryGetValue(box.Uid, out var round))
        {
            round = new(new(box.Uid, box.Nickname));
            rounds.Add(box.Uid, round);
            round.Timer = time.CreateTimer(_ =>
            {
                lock (gate)
                    if (rounds.TryGetValue(box.Uid, out var current) && current == round) QueueWhenQuiet(round);
            }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        round.Item = round.Item with { Nickname = box.Nickname };
        round.Tally = new(checked(round.Tally.Spend + box.Spend), checked(round.Tally.OpenedValue + box.OpenedValue));
        round.Count++;
        round.LastReceived = receivedAt;
        QueueWhenQuiet(round);
    }

    // Called under the gate, including by timers that may have fired just before a new gift reset the deadline.
    private void QueueWhenQuiet(Round round)
    {
        if (round.Queued) return;
        var remaining = QuietWindow - time.GetElapsedTime(round.LastReceived);
        if (remaining > TimeSpan.Zero) round.Timer.Change(remaining, Timeout.InfiniteTimeSpan);
        else
        {
            round.Queued = true;
            pending.Writer.TryWrite(round.Item);
        }
    }

    /// <summary>Counts and clears pending queries and rounds, including rounds still waiting for silence.</summary>
    public int Clear()
    {
        lock (gate)
        {
            var dropped = waitingQueries.Count + rounds.Count;
            while (pending.Reader.TryRead(out _)) { }
            waitingQueries.Clear();
            foreach (var round in rounds.Values) round.Timer.Dispose();
            rounds.Clear();
            return dropped;
        }
    }

    /// <summary>Sends queries' replies and announcements one at a time until cancelled. <paramref name="prepare"/> gets an
    /// announcement's round as it stands at this attempt (null for a query), does the local reads (their failures end the
    /// room connection) and returns the text with the send itself, the only step that can fail it.</summary>
    public async Task SendAsync(Func<Item, BlindBoxTally?, (string Text, Func<CancellationToken, Task> Send)> prepare,
        CancellationToken cancellationToken)
    {
        while (await pending.Reader.WaitToReadAsync(cancellationToken))
        {
            await WaitForTurnAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Peek, and take the item off only once its send has ended: a reconnect keeps an interrupted one, the end of
            // the room connection counts it as dropped, and a first rate limit keeps it at the head for its one retry.
            if (!pending.Reader.TryPeek(out var item)) continue;
            Reported? reported = null;
            if (item.Query is null)
                lock (gate)
                {
                    var round = rounds[item.Uid];
                    (item, reported) = (round.Item, new Reported(round.Tally, round.Count));
                }
            var (text, send) = prepare(item, reported?.Tally);
            var action = reported is null ? "回复" : "播报";
            Exception? failure = null;
            try { await send(cancellationToken); }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested) { failure = error; }
            // Even an interrupted request may have reached B站, so the next send still waits its interval.
            finally { lastSendEnded = time.GetTimestamp(); }
            // ponytail: one retry after one send interval, no backoff; back off if B站's limits turn out to outlast the interval.
            if (failure is DanmakuRateLimitedException && !rateLimitRetryOwed)
            {
                rateLimitRetryOwed = true;
                logger.LogWarning("{Error}，{Seconds} 秒后重试观众 {Nickname}（{Uid}）的{Action}",
                    failure.Message, interval.TotalSeconds, item.Nickname, item.Uid, action);
                continue;
            }
            rateLimitRetryOwed = false;
            Finish(item, reported);
            if (failure is null)
                logger.LogInformation("发送弹幕成功：{Action}观众 {Nickname}（{Uid}）：{Message}", action, item.Nickname, item.Uid, text);
            else
                logger.LogWarning(failure, "发送弹幕失败，已丢弃：{Action}观众 {Nickname}（{Uid}）：{Message}；{Error}",
                    action, item.Nickname, item.Uid, text, failure.Message);
        }
    }

    private async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        if (lastSendEnded is not { } last) return;
        while (interval - time.GetElapsedTime(last) is var remaining && remaining > TimeSpan.Zero)
        {
            // A timer accepts at most uint.MaxValue - 1 milliseconds, even for a valid longer setting.
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(remaining.TotalMilliseconds, uint.MaxValue - 1)),
                time, cancellationToken);
        }
    }

    private void Finish(Item item, Reported? reported)
    {
        lock (gate)
        {
            pending.Reader.TryRead(out _);
            if (reported is { } done)
            {
                // Sent or given up, what this attempt reported is over either way; nothing carries into the next round.
                var round = rounds[item.Uid];
                round.Count -= done.Count;
                if (round.Count == 0)
                {
                    round.Timer.Dispose();
                    rounds.Remove(item.Uid);
                }
                else
                {
                    // Gifts received while the request was in flight start the next round.
                    round.Tally = new(round.Tally.Spend - done.Tally.Spend, round.Tally.OpenedValue - done.Tally.OpenedValue);
                    round.Queued = false;
                    QueueWhenQuiet(round);
                }
                return;
            }
            waitingQueries.Remove(item.Uid);
        }
    }
}
