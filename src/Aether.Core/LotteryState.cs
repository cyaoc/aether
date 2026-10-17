using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Aether.Core;

public enum PrizeGrade { Grand, Second, Third }
public enum PoolLotteryPhase { Idle, Spinning, Drawing }
public enum LotteryMode { Pool, Entry }
public enum EntryLotteryPhase { Preparing, Open, Closed, Ended }
public enum LotteryTheme { StarSummon }

public sealed record LotteryAward(string Name, string Prize, int Capacity, PrizeGrade Grade);
public sealed record LotteryEntry(long Uid, string Nickname, DateTimeOffset EnteredAt);
public sealed record EntryDraw(string Id, int AwardIndex, LotteryAward Award, LotteryEntry Winner,
    DateTimeOffset DrawnAt, ImmutableArray<string> Candidates);
public sealed record EntryLottery(string Id, long RoomId, string Keyword, DateTimeOffset StartedAt,
    ImmutableArray<LotteryAward> Awards, [property: JsonIgnore] ImmutableArray<LotteryEntry> Entries, ImmutableArray<EntryDraw> Draws,
    EntryLotteryPhase Phase = EntryLotteryPhase.Preparing, DateTimeOffset? DrawUntil = null,
    DateTimeOffset? EndedAt = null, bool Hidden = false)
{
    public int EntryCount => Entries.Length;
    public IEnumerable<LotteryEntry> LatestEntries => Entries.Reverse().Take(3);
    public long Remaining => Awards.Sum(award => (long)award.Capacity) - Draws.Length;
}

public sealed record PoolPrize(string Name, int Weight, PrizeGrade Grade);
public sealed record PoolDraw(string Id, PoolPrize Prize, long? Uid, string? Nickname, bool StoppedByHost,
    DateTimeOffset DrawnAt, ImmutableArray<PoolPrize> Candidates);
/// <summary>一场抽奖 that has not ended; <see cref="Viewers"/> is everyone who sent a danmaku in it; the 抽奖页 never gets it.</summary>
public sealed record Lottery(string Id, long RoomId, string StopWord, DateTimeOffset StartedAt,
    ImmutableArray<PoolPrize> Prizes, ImmutableArray<PoolDraw> Draws,
    [property: JsonIgnore] ImmutableDictionary<long, string> Viewers, PoolLotteryPhase Phase = PoolLotteryPhase.Idle,
    long? Uid = null, DateTimeOffset? DrawUntil = null);
public sealed record LotteryState(long? RoomId, bool Connected, Lottery? Lottery, EntryLottery? EntryLottery = null)
{
    /// <summary>The room lock is still held, including while the transport reconnects.</summary>
    public bool CanControl { get; init; } = Connected;
    public LotteryTheme Theme { get; init; } = LotteryTheme.StarSummon;
    public bool IsExamplePlaying { get; init; }
}
public sealed record LotteryUpdate(LotteryState State, PoolDraw? Draw = null, EntryDraw? EntryDraw = null,
    LotteryState? Example = null);
