using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room connection's 发送队列: blind box queries, announcements and welcomes, paced to avoid B站's rate limits.
/// Reconnects keep it, pacing included; the end of the room connection clears it.</summary>
internal sealed class SendQueue(TimeProvider time, ILogger logger, TimeSpan interval, ChannelWriter<WatchUpdate> output)
{
    private static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SameTextWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WelcomeLifetime = TimeSpan.FromSeconds(30);
    // Process-wide, so a shell that keeps items across room connections never sees an id twice.
    private static long nextId;

    /// <summary>One queued send, addressed to a viewer.</summary>
    internal abstract record Item(long Id, long Uid, string Nickname);
    /// <summary>A 盲盒查询 reply, pointing to the keyword danmaku it answers.</summary>
    internal sealed record Query(long Id, Danmaku Trigger) : Item(Id, Trigger.Uid, Trigger.Nickname);
    /// <summary>A 盲盒播报, whose text comes from the viewer's 一轮盲盒 as it stands when sent.</summary>
    internal sealed record Announcement(long Id, long Uid, string Nickname) : Item(Id, Uid, Nickname);
    /// <summary>A 进场欢迎, whose text was fixed when queued.</summary>
    internal sealed record Welcome(long Id, long Uid, string Nickname, long ReceivedAt,
        Aether.Core.Welcome.Result Result) : Item(Id, Uid, Nickname);

    /// <summary>The part of a 一轮盲盒 one announcement reports; a Count, since a box can be worth nothing.</summary>
    private readonly record struct Reported(BlindBoxTally Tally, long Count);

    private sealed class Round(Announcement item)
    {
        public Announcement Item = item;
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
            if (waitingQueries.Add(trigger.Uid)) Queue(new Query(Interlocked.Increment(ref nextId), trigger));
            else logger.LogDebug("盲盒查询：观众 {Nickname}（{Uid}）再次触发，合并到已有待发项", trigger.Nickname, trigger.Uid);
    }

    /// <summary>Reports a rejected 进场欢迎 or queues one whose text is fixed now; welcomes never merge.</summary>
    public void EnqueueWelcome(long uid, string nickname, long receivedAt, Aether.Core.Welcome.Result result)
    {
        var item = new Welcome(Interlocked.Increment(ref nextId), uid, nickname, receivedAt, result);
        if (result.RejectionReason is { } reason) Report(item, SendQueueItemState.NotQueued, result.Text, reason: reason);
        else lock (gate) Queue(item);
    }

    // Report before exposing the item to the sender, so Queued always precedes Sending.
    private void Queue(Item item)
    {
        Report(item, SendQueueItemState.Queued, (item as Welcome)?.Result.Text);
        pending.Writer.TryWrite(item);
    }

    private void Report(Item item, SendQueueItemState state, string? text = null, string? reason = null,
        double? retrySeconds = null, Exception? failure = null)
    {
        var welcome = (item as Welcome)?.Result;
        var kind = item switch { Query => "盲盒查询", Welcome => "进场欢迎", _ => "盲盒播报" };
        var action = item switch { Query => "回复", Welcome => "欢迎", _ => "播报" };
        switch (state)
        {
            case SendQueueItemState.Queued when welcome is not null:
                logger.LogInformation("[发送队列 {ItemId}，{Kind}] 进场欢迎：{Nickname}（{Uid}），命中{Condition}{Value}",
                    item.Id, kind, item.Nickname, item.Uid, welcome.ConditionName, welcome.Value ?? "");
                break;
            case SendQueueItemState.Queued when welcome is null:
                logger.LogInformation("[发送队列 {ItemId}，{Kind}] 进队：观众 {Nickname}（{Uid}）",
                    item.Id, kind, item.Nickname, item.Uid);
                break;
            case SendQueueItemState.NotQueued when welcome?.RejectedBecause == Aether.Core.Welcome.Rejection.EmptyText:
                logger.LogWarning("[发送队列 {ItemId}，{Kind}] 进场欢迎：{Nickname}（{Uid}），命中{Condition}{Value}，但欢迎语拼出来是空的，未发送",
                    item.Id, kind, item.Nickname, item.Uid, welcome.ConditionName, welcome.Value ?? "");
                break;
            case SendQueueItemState.NotQueued when welcome is not null:
                logger.LogWarning("[发送队列 {ItemId}，{Kind}] 进场欢迎：{Nickname}（{Uid}），命中{Condition}{Value}，但欢迎语固定部分计数为 {Length}，超过上限 {Limit}，未发送",
                    item.Id, kind, item.Nickname, item.Uid, welcome.ConditionName, welcome.Value ?? "", welcome.Length, welcome.DanmakuLengthLimit);
                break;
            case SendQueueItemState.Sending:
                logger.LogDebug("[发送队列 {ItemId}，{Kind}] 开始发送：{Action}观众 {Nickname}（{Uid}）：{Message}",
                    item.Id, kind, action, item.Nickname, item.Uid, text);
                break;
            case SendQueueItemState.Sent:
                logger.LogInformation("[发送队列 {ItemId}，{Kind}] 发送弹幕成功：{Action}观众 {Nickname}（{Uid}）：{Message}",
                    item.Id, kind, action, item.Nickname, item.Uid, text);
                break;
            case SendQueueItemState.WaitingToRetry:
                logger.LogWarning("[发送队列 {ItemId}，{Kind}] {Error}，{Seconds} 秒后重试观众 {Nickname}（{Uid}）的{Action}",
                    item.Id, kind, reason, retrySeconds, item.Nickname, item.Uid, action);
                break;
            case SendQueueItemState.Discarded when failure is not null:
                logger.LogWarning(failure, "[发送队列 {ItemId}，{Kind}] 发送弹幕失败，已丢弃：{Action}观众 {Nickname}（{Uid}）：{Message}；{Error}",
                    item.Id, kind, action, item.Nickname, item.Uid, text, reason);
                break;
            case SendQueueItemState.Discarded:
                logger.LogInformation("[发送队列 {ItemId}，{Kind}] {Reason}，已丢弃：欢迎观众 {Nickname}（{Uid}）",
                    item.Id, kind, reason, item.Nickname, item.Uid);
                break;
        }
        output.TryWrite(new SendQueueUpdate(item.Id, time.GetLocalNow(),
            item switch { Query => SendQueueItemKind.BlindBoxQuery, Welcome => SendQueueItemKind.Welcome,
                _ => SendQueueItemKind.BlindBoxAnnouncement }, state, item.Uid, item.Nickname)
        {
            Text = state == SendQueueItemState.Discarded ? null : text,
            WelcomeCondition = welcome?.Condition, WelcomeValue = welcome?.Value,
            Reason = reason, RetrySeconds = retrySeconds
        });
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
            round = new(new(Interlocked.Increment(ref nextId), box.Uid, box.Nickname));
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
            Queue(round.Item);
        }
    }

    /// <summary>Counts and clears pending queries, welcomes and rounds, including rounds still waiting for silence.</summary>
    public int Clear()
    {
        lock (gate)
        {
            var dropped = waitingQueries.Count + rounds.Count;
            while (pending.Reader.TryRead(out var item))
                if (item is Welcome) dropped++;
            waitingQueries.Clear();
            foreach (var round in rounds.Values) round.Timer.Dispose();
            rounds.Clear();
            return dropped;
        }
    }

    /// <summary>Sends queries' replies, announcements and welcomes one at a time until cancelled. <paramref name="prepare"/>
    /// gets an announcement's round as it stands (null otherwise) and runs again after each wait, so the text sent is current;
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
            if (item is Announcement)
                lock (gate)
                {
                    var round = rounds[item.Uid];
                    (item, reported) = (round.Item, new Reported(round.Tally, round.Count));
                }
            var (text, send) = prepare(item, reported?.Tally);
            // Waiting can change a query's tally or an announcement's round; prepare and check the new text again.
            if (await WaitedForTurnAsync(text, cancellationToken)) continue;
            if (DiscardExpiredWelcome(item)) continue;
            Exception? failure = null;
            Report(item, SendQueueItemState.Sending, text);
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
                var retrySeconds = Math.Max(interval.TotalSeconds, SameTextWindow.TotalSeconds);
                Report(item, SendQueueItemState.WaitingToRetry, reason: failure.Message, retrySeconds: retrySeconds);
                continue;
            }
            rateLimitRetryOwed = false;
            if (failure is null)
                Finish(item, reported, SendQueueItemState.Sent, text);
            else
                Finish(item, reported, SendQueueItemState.Discarded, text, failure.Message, failure);
        }
    }

    private bool DiscardExpiredWelcome(Item item)
    {
        if (item is not Welcome welcome || time.GetElapsedTime(welcome.ReceivedAt) < WelcomeLifetime) return false;
        rateLimitRetryOwed = false;
        var reason = FormattableString.Invariant($"欢迎已过期，进场已满 {WelcomeLifetime.TotalSeconds} 秒");
        Finish(item, null, SendQueueItemState.Discarded, reason: reason);
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

    private void Finish(Item item, Reported? reported, SendQueueItemState outcome, string? text = null, string? reason = null,
        Exception? failure = null)
    {
        lock (gate)
        {
            pending.Reader.TryRead(out _);
            // Under the gate, so the outcome precedes the next round's Queued and a later ask gets an item of its own.
            Report(item, outcome, text, reason, failure: failure);
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
                    round.Item = round.Item with { Id = Interlocked.Increment(ref nextId) };
                    round.Queued = false;
                    QueueWhenQuiet(round);
                }
                return;
            }
            if (item is Query) waitingQueries.Remove(item.Uid);
        }
    }
}
