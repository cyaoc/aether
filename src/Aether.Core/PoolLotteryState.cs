using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Aether.Core;

public enum PrizeGrade { Grand, Second, Third }
public enum PoolLotteryPhase { Idle, Spinning, Drawing }

public sealed record PoolPrize(string Name, int Weight, PrizeGrade Grade);
public sealed record PoolDraw(string Id, PoolPrize Prize, long? Uid, string? Nickname, bool StoppedByHost,
    DateTimeOffset DrawnAt, ImmutableArray<PoolPrize> Candidates);
/// <summary>一场抽奖 that has not ended; <see cref="Viewers"/> is everyone who sent a danmaku in it; the 抽奖页 never gets it.</summary>
public sealed record Lottery(string Id, long RoomId, string StopWord, DateTimeOffset StartedAt,
    ImmutableArray<PoolPrize> Prizes, ImmutableArray<PoolDraw> Draws,
    [property: JsonIgnore] ImmutableDictionary<long, string> Viewers, PoolLotteryPhase Phase = PoolLotteryPhase.Idle,
    long? Uid = null, DateTimeOffset? DrawUntil = null);
public sealed record PoolLotteryState(long? RoomId, bool Connected, Lottery? Lottery)
{
    /// <summary>The room lock is still held, including while the transport reconnects.</summary>
    public bool CanControl { get; init; } = Connected;
}
public sealed record PoolLotteryUpdate(PoolLotteryState State, PoolDraw? Draw = null);
