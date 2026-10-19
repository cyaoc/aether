using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Aether.Core;

public enum Grade { Grand, Second, Third }
public enum PoolLotteryPhase { Idle, Spinning }
public enum LotteryMode { Pool, Entry }
public enum EntryLotteryPhase { Preparing, Open, Closed, Ended }
public enum LotteryTheme { StarSummon, MechaLock, CapsuleGacha }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(PoolLottery), nameof(LotteryMode.Pool))]
[JsonDerivedType(typeof(EntryLottery), nameof(LotteryMode.Entry))]
public abstract record Lottery(string Id, long RoomId, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, DateTimeOffset? DrawUntil)
{
    // The 抽奖页 clears drawUntil locally when its countdown ends, so a pushed isDrawing would go stale.
    [JsonIgnore] public bool IsDrawing => DrawUntil is not null;
}

public abstract record LotteryDraw(string Id, DateTimeOffset DrawnAt);

public sealed record LotteryAward(string Name, string Prize, int Capacity, Grade Grade);
public sealed record LotteryEntry(long Uid, string Nickname, DateTimeOffset EnteredAt);
public sealed record EntryDraw(string Id, int AwardIndex, LotteryAward Award, LotteryEntry Winner,
    DateTimeOffset DrawnAt, ImmutableArray<string> Candidates) : LotteryDraw(Id, DrawnAt);
public sealed record EntryLottery(string Id, long RoomId, string Keyword, DateTimeOffset StartedAt,
    ImmutableArray<LotteryAward> Awards, [property: JsonIgnore] ImmutableArray<LotteryEntry> Entries, ImmutableArray<EntryDraw> Draws,
    EntryLotteryPhase Phase = EntryLotteryPhase.Preparing, DateTimeOffset? DrawUntil = null,
    DateTimeOffset? EndedAt = null, bool Hidden = false) : Lottery(Id, RoomId, StartedAt, EndedAt, DrawUntil)
{
    public int EntryCount => Entries.Length;
    public IEnumerable<LotteryEntry> LatestEntries => Entries.Reverse().Take(3);
    public long Remaining => Awards.Sum(award => (long)award.Capacity) - Draws.Length;
}

public sealed record PoolPrize(string Name, int Weight, Grade Grade);
public sealed record PoolDraw(string Id, PoolPrize Prize, long? Uid, string? Nickname, bool StoppedByHost,
    DateTimeOffset DrawnAt, ImmutableArray<PoolPrize> Candidates) : LotteryDraw(Id, DrawnAt);
/// <summary>A 奖池抽奖; <see cref="Viewers"/> is everyone who sent a danmaku in it, kept only in memory; the 抽奖页 never gets it.</summary>
public sealed record PoolLottery(string Id, long RoomId, string StopWord, DateTimeOffset StartedAt,
    ImmutableArray<PoolPrize> Prizes, ImmutableArray<PoolDraw> Draws,
    [property: JsonIgnore] ImmutableDictionary<long, string> Viewers, PoolLotteryPhase Phase = PoolLotteryPhase.Idle,
    long? Uid = null, DateTimeOffset? DrawUntil = null, DateTimeOffset? EndedAt = null)
    : Lottery(Id, RoomId, StartedAt, EndedAt, DrawUntil);
public sealed record LotteryState(long? RoomId, bool Connected, Lottery? Lottery)
{
    /// <summary>The room lock is still held, including while the transport reconnects.</summary>
    public bool CanControl { get; init; } = Connected;
    public LotteryTheme Theme { get; init; } = LotteryTheme.StarSummon;
    public bool IsExamplePlaying { get; init; }
}
public sealed record LotteryUpdate(LotteryState State, LotteryState? Example = null);
