using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room connection's 发送队列: blind box replies waiting to be sent, paced so B站 does not mute the account.
/// Reconnects keep it, pacing included; the end of the room connection clears it.</summary>
internal sealed class SendQueue(TimeProvider time, ILogger logger, TimeSpan interval)
{
    private static readonly TimeSpan ViewerCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromSeconds(60);

    // ponytail: unbounded in memory; add a capacity policy if keyword floods become a problem.
    private readonly Channel<Danmaku> replies = Channel.CreateUnbounded<Danmaku>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    // The receive loop enqueues while the sender takes, so these two are only touched under the gate.
    private readonly object gate = new();
    private readonly HashSet<long> waitingViewers = [];
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
            if (waitingViewers.Add(trigger.Uid)) replies.Writer.TryWrite(trigger);
        }
    }

    /// <summary>Empties the send queue when the room connection ends, returning how many replies were dropped.</summary>
    public int Clear()
    {
        lock (gate)
        {
            var dropped = 0;
            while (replies.Reader.TryRead(out _)) dropped++;
            waitingViewers.Clear();
            return dropped;
        }
    }

    /// <summary>Sends replies one at a time until cancelled. <paramref name="prepare"/> does the local reads (their
    /// failures end the room connection) and returns the reply text with the send itself, the only step that can fail
    /// a reply.</summary>
    public async Task SendAsync(Func<Danmaku, (string Reply, Func<CancellationToken, Task> Send)> prepare,
        CancellationToken cancellationToken)
    {
        while (await replies.Reader.WaitToReadAsync(cancellationToken))
        {
            await WaitForTurnAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Peek, and take the reply off only once its send has ended: a reconnect keeps an interrupted one, the end of
            // the room connection counts it as dropped, and a first rate limit keeps it at the head for its one retry.
            if (!replies.Reader.TryPeek(out var trigger)) continue;
            var (reply, send) = prepare(trigger);
            Exception? failure = null;
            try { await send(cancellationToken); }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested) { failure = error; }
            // Even an interrupted request may have reached B站, so the next send still waits its interval or pause.
            finally { lastSendEnded = time.GetTimestamp(); }
            if (failure is DanmakuRateLimitedException && !rateLimitRetryOwed)
            {
                rateLimitRetryOwed = true;
                // Shown by default: a rate limit means the account is close to being muted.
                logger.LogWarning("{Error}，发送队列暂停 {Seconds} 秒后重试观众 {Nickname}（{Uid}）的回复",
                    failure.Message, RateLimitPause.TotalSeconds, trigger.Nickname, trigger.Uid);
                continue;
            }
            rateLimitRetryOwed = false;
            Finish(trigger, succeeded: failure is null);
            if (failure is null)
                logger.LogInformation("发送弹幕成功：回复观众 {Nickname}（{Uid}）：{Message}", trigger.Nickname, trigger.Uid, reply);
            else
                logger.LogWarning("发送弹幕失败，已丢弃：回复观众 {Nickname}（{Uid}）：{Message}；{Error}",
                    trigger.Nickname, trigger.Uid, reply, failure.Message);
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

    private void Finish(Danmaku trigger, bool succeeded)
    {
        lock (gate)
        {
            replies.Reader.TryRead(out _);
            waitingViewers.Remove(trigger.Uid);
            if (!succeeded) return;
            // Forget expired cooldowns on the way; removing while enumerating is allowed since .NET Core 3.0.
            foreach (var (uid, repliedAt) in lastReplyAt)
                if (time.GetElapsedTime(repliedAt) >= ViewerCooldown) lastReplyAt.Remove(uid);
            lastReplyAt[trigger.Uid] = time.GetTimestamp();
        }
    }
}
