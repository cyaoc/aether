using System.Collections.Immutable;

namespace Aether.Core;

/// <summary>The lottery region's facade (ADR 0017): the GUI, the 抽奖页 and the room connection reach the lottery only through
/// these commands, queries and reports.</summary>
public sealed class LotteryFacade
{
    private readonly Region<LotteryController> region;
    private readonly Func<LotteryState> state;

    internal LotteryFacade(Regions regions, Func<LotteryStore> store, Random random)
    {
        region = regions.Create(timers => new LotteryController(store, timers, regions.Logger, random));
        state = region.Snapshot(s => s.State);
    }

    /// <summary>The state as the last step left it (ADR 0017 convention 6).</summary>
    public LotteryState State => state();

    /// <summary>The current state first, then one per change.</summary>
    public IAsyncEnumerable<LotteryUpdate> GetUpdatesAsync(CancellationToken token = default) =>
        region.SubscribeAsync(s => s.Subscribers, s => s.Update, token);

    /// <summary>Every 奖品方案 first, 奖池抽奖's before 报名抽奖's, then all of them again after each save or delete.</summary>
    public IAsyncEnumerable<ImmutableArray<LotteryPrizeSet>> GetPrizeSetUpdatesAsync(CancellationToken token = default) =>
        region.SubscribeAsync(s => s.PrizeSetSubscribers, s => s.PrizeSets, token);

    /// <summary>Read-only, including unfinished lotteries; no room connection is required.</summary>
    public Task<ImmutableArray<LotteryHistory>> GetHistoryAsync(long roomId) => region.Run(s => s.GetHistory(roomId));

    /// <summary>The room whose history to show without a room connection: where the last lottery was held, null before any.</summary>
    public Task<long?> GetLastLotteryRoomAsync() => region.Run(s => s.GetLastLotteryRoom());

    public Task<Lottery?> GetHistoryDetailsAsync(long roomId, string id) => region.Run(s => s.GetHistoryDetails(roomId, id));

    public Task SetPinnedResultsAsync(bool enabled) => region.Run(s => s.SetPinnedResults(enabled));

    public Task SelectThemeAsync(LotteryTheme theme) => region.Run(s => s.SelectTheme(theme));

    public Task SavePrizeSetAsync(LotteryMode mode, string name, IReadOnlyCollection<PoolPrize> prizes,
        IReadOnlyCollection<LotteryAward> awards) => region.Run(s => s.SavePrizeSet(mode, name, prizes, awards));

    public Task DeletePrizeSetAsync(LotteryMode mode, string name) => region.Run(s => s.DeletePrizeSet(mode, name));

    public Task NewAsync(LotteryMode mode) => region.Run(s => s.New(mode));

    public Task StartAsync(IReadOnlyCollection<PoolPrize> prizes, string stopWord, long? uid = null) =>
        region.Run(s => s.Start(prizes, stopWord, uid));

    /// <summary>The host stops the spinning 奖池抽奖, which draws.</summary>
    public Task StopAsync() => region.Run(s => s.Stop());

    public Task CancelAsync() => region.Run(s => s.Cancel());

    public Task SetDrawHiddenFromStreamAsync(string drawId, bool hidden) => region.Run(s => s.SetDrawHiddenFromStream(drawId, hidden));

    public Task HideAllDrawsFromStreamAsync() => region.Run(s => s.HideAllDrawsFromStream());

    public Task StartEntryAsync(IReadOnlyCollection<LotteryAward> awards, string keyword) => region.Run(s => s.StartEntry(awards, keyword));

    public Task CloseEntriesAsync() => region.Run(s => s.CloseEntries());

    public Task DrawEntryAsync(int awardIndex) => region.Run(s => s.DrawEntry(awardIndex));

    public Task EndEntryAsync() => region.Run(s => s.EndEntry());

    public Task SetEntryHiddenAsync(bool hidden) => region.Run(s => s.SetEntryHidden(hidden));

    internal Task PlayExampleAsync() => region.Run(s => s.PlayExample());

    internal void StopExample() => region.Post(s => s.StopExample());

    // The room connection never waits for the lottery's database.
    internal void Connect(long roomId) => region.Post(s => s.FollowRoom(roomId, null));

    internal void Receive(long roomId, Danmaku danmaku) => region.Post(s => s.FollowRoom(roomId, danmaku));

    internal void Disconnect(long roomId) => region.Post(s => s.Disconnect(roomId));

    internal void EndRoom(long roomId) => region.Post(s => s.EndRoom(roomId));

    internal void Stop() => region.Stop();
}
