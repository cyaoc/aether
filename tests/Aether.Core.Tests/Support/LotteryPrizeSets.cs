using System.Collections.Immutable;

namespace Aether.Core.Tests.Support;

internal static class LotteryPrizeSets
{
    /// <summary>One 抽奖方式's 奖品方案, as the lottery reports them to a new reader.</summary>
    public static async Task<ImmutableArray<LotteryPrizeSet>> PrizeSetsAsync(this LotteryFacade lottery, LotteryMode mode)
    {
        var token = TestContext.Current.CancellationToken;
        await using var updates = lottery.GetPrizeSetUpdatesAsync(token).GetAsyncEnumerator(token);
        Assert.True(await updates.MoveNextAsync());
        return [.. updates.Current.Where(set => set.Mode == mode)];
    }
}
