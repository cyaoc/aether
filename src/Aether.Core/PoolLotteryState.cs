using System.Collections.Immutable;

namespace Aether.Core;

public enum PrizeGrade { Grand, Second, Third }
public enum PoolLotteryPhase { Idle, Spinning, Revealing }

public sealed record PoolPrize(string Name, int Weight, PrizeGrade Grade);
public sealed record PoolDraw(string Id, PoolPrize Prize, long? Uid, string? Nickname, bool StoppedByHost,
    DateTimeOffset DrawnAt, ImmutableArray<PoolPrize> Candidates);
public sealed record PoolLotterySession(string Id, long RoomId, string StopWord, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, ImmutableArray<PoolPrize> Prizes, ImmutableArray<PoolDraw> Draws,
    ImmutableDictionary<long, string> Viewers, PoolLotteryPhase Phase = PoolLotteryPhase.Idle,
    long? Uid = null, DateTimeOffset? RevealUntil = null);
public sealed record PoolLotteryState(long? RoomId, bool Connected, PoolLotterySession? Lottery)
{
    /// <summary>The room lock is still held, including while the transport reconnects.</summary>
    public bool CanControl { get; init; } = Connected;
}
public sealed record PoolLotteryUpdate(PoolLotteryState State, PoolDraw? Draw = null);
