using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room connection's 发送队列: blind box queries and announcements, paced so B站 does not mute the account.
/// Reconnects keep it, pacing included; the end of the room connection clears it.</summary>
internal sealed class SendQueue(TimeProvider time, ILogger logger, TimeSpan interval)
{
    private static readonly TimeSpan ViewerCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(3);

    internal sealed record Item(long Uid, string Nickname, Danmaku? Trigger = null);

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
    private readonly Channel<Item> triggers = Channel.CreateUnbounded<Item>(
        new UnboundedChannelOptions { SingleReader = true });
    // Receiving, quiet-window timers and sending share these under the gate.
    private readonly object gate = new();
    private readonly HashSet<long> waitingQueries = [];
    private readonly Dictionary<long, Round> rounds = [];
    /// <summary>Viewer uid → TimeProvider timestamp of that viewer's last successful reply.</summary>
    private readonly Dictionary<long, long> lastReplyAt = [];
    // Only the sender touches these.
    private long? lastSendEnded;
    private bool rateLimitRetryOwed;

    /// <summary>Queues a reply to <paramref name="trigger"/>'s viewer, unless one is already waiting (the asks merge into
    /// the first) or the viewer was replied to within the cooldown.</summary>
    public void Enqueue(Danmaku trigger)
    {
        lock (gate)
        {
            if (lastReplyAt.TryGetValue(trigger.Uid, out var repliedAt) && time.GetElapsedTime(repliedAt) < ViewerCooldown)
            {
                logger.LogDebug("观众 {Uid} 的回复仍在 {Seconds} 秒冷却内，忽略关键字", trigger.Uid, ViewerCooldown.TotalSeconds);
                return;
            }
            if (waitingQueries.Add(trigger.Uid)) triggers.Writer.TryWrite(new(trigger.Uid, trigger.Nickname, trigger));
        }
    }

    /// <summary>Records a whole gift message; the callback returns whether each new item should join a round.</summary>
    public void Record(BlindBox[] boxes, long receivedAt, Func<BlindBox, bool> recordForAnnouncement)
    {
        // A SQLite write can wait for another process; neither a deadline nor a send snapshot may split this message.
        lock (gate)
            foreach (var box in boxes)
                if (recordForAnnouncement(box)) Add(box, receivedAt);
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
            triggers.Writer.TryWrite(round.Item);
        }
    }

    /// <summary>Counts and clears pending queries and rounds, including rounds still waiting for silence.</summary>
    public int Clear()
    {
        lock (gate)
        {
            var dropped = waitingQueries.Count + rounds.Count;
            while (triggers.Reader.TryRead(out _)) { }
            waitingQueries.Clear();
            foreach (var round in rounds.Values) round.Timer.Dispose();
            rounds.Clear();
            return dropped;
        }
    }

    /// <summary>Sends replies one at a time until cancelled. <paramref name="prepare"/> does the local reads (their
    /// failures end the room connection) and returns the reply text with the send itself, the only step that can fail
    /// a reply.</summary>
    public async Task SendAsync(Func<Item, BlindBoxTally?, (string Reply, Func<CancellationToken, Task> Send)> prepare,
        CancellationToken cancellationToken)
    {
        while (await triggers.Reader.WaitToReadAsync(cancellationToken))
        {
            await WaitForTurnAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Peek, and take the reply off only once its send has ended: a reconnect keeps an interrupted one, the end of
            // the room connection counts it as dropped, and a first rate limit keeps it at the head for its one retry.
            if (!triggers.Reader.TryPeek(out var trigger)) continue;
            BlindBoxTally? tally = null;
            long count = 0;
            if (trigger.Trigger is null)
            {
                lock (gate)
                {
                    var round = rounds[trigger.Uid];
                    tally = round.Tally;
                    count = round.Count;
                    trigger = round.Item;
                }
            }
            var (reply, send) = prepare(trigger, tally);
            var action = trigger.Trigger is null ? "播报" : "回复";
            Exception? failure = null;
            try { await send(cancellationToken); }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested) { failure = error; }
            // Even an interrupted request may have reached B站, so the next send still waits its interval or pause.
            finally { lastSendEnded = time.GetTimestamp(); }
            if (failure is DanmakuRateLimitedException && !rateLimitRetryOwed)
            {
                rateLimitRetryOwed = true;
                // Shown by default: a rate limit means the account is close to being muted.
                logger.LogWarning("{Error}，发送队列暂停 {Seconds} 秒后重试观众 {Nickname}（{Uid}）的{Action}",
                    failure.Message, RateLimitPause.TotalSeconds, trigger.Nickname, trigger.Uid, action);
                continue;
            }
            rateLimitRetryOwed = false;
            Finish(trigger, tally, count, succeeded: failure is null);
            if (failure is null)
                logger.LogInformation("发送弹幕成功：{Action}观众 {Nickname}（{Uid}）：{Message}", action, trigger.Nickname, trigger.Uid, reply);
            else
                logger.LogWarning(failure, "发送弹幕失败，已丢弃：{Action}观众 {Nickname}（{Uid}）：{Message}；{Error}",
                    action, trigger.Nickname, trigger.Uid, reply, failure.Message);
        }
    }

    private async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        if (lastSendEnded is not { } last) return;
        // While a rate limit retry is owed, the pause stands in for any shorter interval.
        var wait = rateLimitRetryOwed && interval < RateLimitPause ? RateLimitPause : interval;
        while (wait - time.GetElapsedTime(last) is var remaining && remaining > TimeSpan.Zero)
        {
            // A timer accepts at most uint.MaxValue - 1 milliseconds, even for a valid longer setting.
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(remaining.TotalMilliseconds, uint.MaxValue - 1)),
                time, cancellationToken);
        }
    }

    private void Finish(Item trigger, BlindBoxTally? tally, long count, bool succeeded)
    {
        lock (gate)
        {
            triggers.Reader.TryRead(out _);
            if (tally is not null)
            {
                var round = rounds[trigger.Uid];
                round.Count -= count;
                if (round.Count == 0)
                {
                    round.Timer.Dispose();
                    rounds.Remove(trigger.Uid);
                }
                else
                {
                    // Only the amounts in this request are finished; gifts received in flight start the next round.
                    round.Tally = new(round.Tally.Spend - tally.Spend, round.Tally.OpenedValue - tally.OpenedValue);
                    round.Queued = false;
                    QueueWhenQuiet(round);
                }
                return;
            }
            waitingQueries.Remove(trigger.Uid);
            if (!succeeded) return;
            // Forget expired cooldowns on the way; removing while enumerating is allowed since .NET Core 3.0.
            foreach (var (uid, repliedAt) in lastReplyAt)
                if (time.GetElapsedTime(repliedAt) >= ViewerCooldown) lastReplyAt.Remove(uid);
            lastReplyAt[trigger.Uid] = lastSendEnded!.Value;
        }
    }
}
