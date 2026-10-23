using System.Collections.Immutable;

namespace Aether.Core;

public sealed record LotteryPrizeSet(LotteryMode Mode, string Name, DateTimeOffset SavedAt,
    ImmutableArray<PoolPrize> Prizes, ImmutableArray<LotteryAward> Awards);
