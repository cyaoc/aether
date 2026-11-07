using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The lottery region's facade (ADR 0017): the GUI, the 抽奖页 and the room connection reach the lottery only through
/// these commands, queries and reports.</summary>
public sealed class LotteryFacade
{
    private readonly Region<LotteryController> region;
    private LotteryState snapshot;

    internal LotteryFacade(Func<LotteryStore> store, TimeProvider time, Random random, PendingSteps steps, ILogger logger)
    {
        var lottery = new LotteryController(store, time, logger, random, Post);
        snapshot = lottery.State;
        region = new(lottery, steps, logger);
    }

    /// <summary>The state as the last step left it (ADR 0017 convention 6).</summary>
    public LotteryState State => Volatile.Read(ref snapshot);

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

    public Task SetPinnedResultsAsync(bool enabled) => Run(s => s.SetPinnedResults(enabled));

    public Task SelectThemeAsync(LotteryTheme theme) => Run(s => s.SelectTheme(theme));

    public Task SavePrizeSetAsync(LotteryMode mode, string name, IReadOnlyCollection<PoolPrize> prizes,
        IReadOnlyCollection<LotteryAward> awards) => Run(s => s.SavePrizeSet(mode, name, prizes, awards));

    public Task DeletePrizeSetAsync(LotteryMode mode, string name) => Run(s => s.DeletePrizeSet(mode, name));

    public Task NewAsync(LotteryMode mode) => Run(s => s.New(mode));

    public Task StartAsync(IReadOnlyCollection<PoolPrize> prizes, string stopWord, long? uid = null) =>
        Run(s => s.Start(prizes, stopWord, uid));

    /// <summary>The host stops the spinning 奖池抽奖, which draws.</summary>
    public Task StopAsync() => Run(s => s.Stop());

    public Task CancelAsync() => Run(s => s.Cancel());

    public Task SetDrawHiddenFromStreamAsync(string drawId, bool hidden) => Run(s => s.SetDrawHiddenFromStream(drawId, hidden));

    public Task HideAllDrawsFromStreamAsync() => Run(s => s.HideAllDrawsFromStream());

    public Task StartEntryAsync(IReadOnlyCollection<LotteryAward> awards, string keyword) => Run(s => s.StartEntry(awards, keyword));

    public Task CloseEntriesAsync() => Run(s => s.CloseEntries());

    public Task DrawEntryAsync(int awardIndex) => Run(s => s.DrawEntry(awardIndex));

    public Task EndEntryAsync() => Run(s => s.EndEntry());

    public Task SetEntryHiddenAsync(bool hidden) => Run(s => s.SetEntryHidden(hidden));

    internal Task PlayExampleAsync() => Run(s => s.PlayExample());

    internal void StopExample() => Post(s => s.StopExample());

    // The room connection never waits for the lottery's database.
    internal void Connect(long roomId) => Post(s => s.FollowRoom(roomId, null));

    internal void Receive(long roomId, Danmaku danmaku) => Post(s => s.FollowRoom(roomId, danmaku));

    internal void Disconnect(long roomId) => Post(s => s.Disconnect(roomId));

    internal void EndRoom(long roomId) => Post(s => s.EndRoom(roomId));

    /// <summary>Every reader's updates end, and the timers stop; later commands are cancelled.</summary>
    internal void Dispose()
    {
        region.Post(s => s.Dispose());
        region.Stop();
    }

    private Task Run(Action<LotteryController> step) => region.Run(Snapshotting(step));

    private void Post(Action<LotteryController> step) => region.Post(Snapshotting(step));

    // Every step that can change the state leaves its snapshot, also one that fails halfway, as reading under the old lock did.
    private Action<LotteryController> Snapshotting(Action<LotteryController> step) => s =>
    {
        try { step(s); }
        finally { Volatile.Write(ref snapshot, s.State); }
    };
}
