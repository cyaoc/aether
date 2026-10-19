using System.Collections.Immutable;

namespace Aether.Core;

public sealed record LotteryHistory(string Id, long RoomId, LotteryMode Mode, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, string Keyword, string StopWord, int EntryCount, int DrawCount,
    ImmutableArray<LotteryAward> Awards, ImmutableArray<PoolPrize> Prizes);
