namespace Aether.Core;

public abstract record WatchUpdate;

public sealed record WatchQrCode(string Content) : WatchUpdate;

public sealed record Connecting : WatchUpdate;

public sealed record Connected : WatchUpdate;

public sealed record Reconnecting : WatchUpdate;

public sealed record Danmaku(DateTimeOffset ReceivedAt, string Nickname, string Content) : WatchUpdate
{
    public long Uid { get; init; }
    public string Id { get; init; } = "";
    public long? FanClubLevel { get; init; }
    public int GuardTier { get; init; }
    public string GuardName => Guard.Name(GuardTier);
    public long WealthLevel { get; init; }
    public string ReplyName { get; init; } = "";
}

public enum SendQueueItemKind { BlindBoxQuery, BlindBoxAnnouncement, Welcome }

public enum SendQueueItemState { Queued, Sending, Sent, WaitingToRetry, Discarded, NotQueued }

/// <summary>The 欢迎条件 a welcome matched, in the order they are checked.</summary>
public enum WelcomeCondition { Uid, FanClubLevel, Guard, WealthLevel }

/// <summary>One item's progress in a room connection. Id survives reconnects and is never reused in this process, so a
/// shell can keep items across room connections. A connection ending leaves unfinished items for the shell to mark; it
/// emits no individual discards.</summary>
public sealed record SendQueueUpdate(long Id, DateTimeOffset OccurredAt, SendQueueItemKind Kind,
    SendQueueItemState State, long Uid, string Nickname) : WatchUpdate
{
    /// <summary>Present on Queued/NotQueued welcomes and on every Sending/Sent update; otherwise null, not a text reset.</summary>
    public string? Text { get; init; }
    /// <summary>Present on welcomes only.</summary>
    public WelcomeCondition? WelcomeCondition { get; init; }
    /// <summary>The viewer's matched level or guard name; null for UID matches and non-welcomes.</summary>
    public string? WelcomeValue { get; init; }
    /// <summary>Present on WaitingToRetry, Discarded and NotQueued.</summary>
    public string? Reason { get; init; }
    /// <summary>The delay stated in the rate-limit warning; present only on WaitingToRetry.</summary>
    public double? RetrySeconds { get; init; }
}
