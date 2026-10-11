using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room connection's 发送队列: blind box queries, announcements and welcomes, paced to avoid B站's rate limits.
/// Reconnects keep it, pacing included; the end of the room connection clears it.</summary>
internal sealed class SendQueue(TimeProvider time, ILogger logger, TimeSpan interval)
{
    private static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SameTextWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WelcomeLifetime = TimeSpan.FromSeconds(30);

    /// <summary>A queued 盲盒查询, a prepared 进场欢迎, or a 盲盒播报, which carries neither.</summary>
    internal sealed record Item(long Uid, string Nickname, Danmaku? Query = null, Welcome? Welcome = null);
    internal sealed record Welcome(string Text, bool Mention, long ReceivedAt);

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
    private readonly Dictionary<string, long> sendEndedByText = [];
    private bool rateLimitRetryOwed;

    /// <summary>Queues a 盲盒查询 reply to <paramref name="trigger"/>'s viewer, unless one is already waiting (the asks merge
    /// into the first).</summary>
    public void Enqueue(Danmaku trigger)
    {
        lock (gate)
            if (waitingQueries.Add(trigger.Uid)) pending.Writer.TryWrite(new(trigger.Uid, trigger.Nickname, trigger));
    }

    public void EnqueueWelcome(long uid, string nickname, string text, bool mention, long receivedAt)
    {
        lock (gate) pending.Writer.TryWrite(new(uid, nickname, Welcome: new(text, mention, receivedAt)));
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

    /// <summary>Counts and clears pending queries, welcomes and rounds, including rounds still waiting for silence.</summary>
    public int Clear()
    {
        lock (gate)
        {
            var dropped = waitingQueries.Count + rounds.Count;
            while (pending.Reader.TryRead(out var item))
                if (item.Welcome is not null) dropped++;
            waitingQueries.Clear();
            foreach (var round in rounds.Values) round.Timer.Dispose();
            rounds.Clear();
            return dropped;
        }
    }

    /// <summary>Sends queries' replies and announcements one at a time until cancelled. <paramref name="prepare"/> gets an
    /// announcement's round as it stands (null for a query) and runs again after each wait, so the text sent is current;
    /// it does the local reads (their failures end the room connection) and returns the text with the send itself, the
    /// only step that can fail it.</summary>
    public async Task SendAsync(Func<Item, BlindBoxTally?, (string Text, Func<CancellationToken, Task> Send)> prepare,
        CancellationToken cancellationToken)
    {
        while (await pending.Reader.WaitToReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Peek, and take the item off only once its send has ended: a reconnect keeps an interrupted one, the end of
            // the room connection counts it as dropped, and a first rate limit keeps it at the head for its one retry.
            if (!pending.Reader.TryPeek(out var item)) continue;
            if (DiscardExpiredWelcome(item)) continue;
            Reported? reported = null;
            if (item.Query is null && item.Welcome is null)
                lock (gate)
                {
                    var round = rounds[item.Uid];
                    (item, reported) = (round.Item, new Reported(round.Tally, round.Count));
                }
            var (text, send) = prepare(item, reported?.Tally);
            // Waiting can change a query's tally or an announcement's round; prepare and check the new text again.
            if (await WaitedForTurnAsync(text, cancellationToken)) continue;
            if (DiscardExpiredWelcome(item)) continue;
            var action = item.Welcome is not null ? "欢迎" : reported is null ? "回复" : "播报";
            Exception? failure = null;
            try { await send(cancellationToken); }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested) { failure = error; }
            // Even failed or interrupted requests may have reached B站; both pacing rules count them.
            finally
            {
                var ended = time.GetTimestamp();
                lastSendEnded = ended;
                foreach (var (previousText, previousEnd) in sendEndedByText.ToArray())
                    if (time.GetElapsedTime(previousEnd, ended) >= SameTextWindow) sendEndedByText.Remove(previousText);
                sendEndedByText[text] = ended;
            }
            // ponytail: one retry, no backoff; back off if B站's limits turn out to outlast both pacing rules.
            if (failure is DanmakuRateLimitedException && !rateLimitRetryOwed)
            {
                rateLimitRetryOwed = true;
                logger.LogWarning("{Error}，{Seconds} 秒后重试观众 {Nickname}（{Uid}）的{Action}",
                    failure.Message, Math.Max(interval.TotalSeconds, SameTextWindow.TotalSeconds), item.Nickname, item.Uid, action);
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

    private bool DiscardExpiredWelcome(Item item)
    {
        if (item.Welcome is not { } welcome || time.GetElapsedTime(welcome.ReceivedAt) < WelcomeLifetime) return false;
        rateLimitRetryOwed = false;
        Finish(item, null);
        logger.LogInformation("欢迎已过期，进场已满 30 秒，已丢弃：欢迎观众 {Nickname}（{Uid}）", item.Nickname, item.Uid);
        return true;
    }

    private async Task<bool> WaitedForTurnAsync(string text, CancellationToken cancellationToken)
    {
        var remaining = lastSendEnded is { } last ? interval - time.GetElapsedTime(last) : TimeSpan.Zero;
        if (sendEndedByText.TryGetValue(text, out var previousEnd))
        {
            var sameTextRemaining = SameTextWindow - time.GetElapsedTime(previousEnd);
            if (sameTextRemaining > remaining) remaining = sameTextRemaining;
        }
        if (remaining <= TimeSpan.Zero) return false;
        // A timer accepts at most uint.MaxValue - 1 milliseconds, even for a valid longer setting.
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(remaining.TotalMilliseconds, uint.MaxValue - 1)),
            time, cancellationToken);
        return true;
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
            if (item.Welcome is null) waitingQueries.Remove(item.Uid);
        }
    }
}
