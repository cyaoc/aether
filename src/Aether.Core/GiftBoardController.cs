using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The 礼物榜 of the room this client is connected to: records the room's gifts into its latest 期 and tells the
/// 礼物榜页 and the GUI what to show. It follows the settings last applied, so a save needs no new room connection.</summary>
public sealed class GiftBoardController : IDisposable
{
    private readonly object gate = new();
    private readonly Func<GiftBoardStore> store;
    private readonly TimeProvider time;
    private readonly ILogger logger;
    private readonly Subscribers<GiftBoardState> subscribers;
    // The tids this room connection has seen; the next room connection starts over.
    // shortcut: never pruned, so a very long room connection keeps every tid; prune it if that grows too large.
    private readonly HashSet<string> seen = [];
    // The defaults keep the board off until a shell applies its settings file.
    private Settings settings = new();
    private long? roomId;
    // When the 一场直播 the connected room is in began (Unix seconds), as last read from room_init; null off air.
    private long? liveTime;

    internal GiftBoardController(Func<GiftBoardStore> store, TimeProvider time, ILogger logger)
    {
        this.store = store;
        this.time = time;
        this.logger = logger;
        subscribers = new(gate);
    }

    /// <summary>The current state first, then one per change.</summary>
    public IAsyncEnumerable<GiftBoardState> GetUpdatesAsync(CancellationToken token = default) =>
        subscribers.SubscribeAsync(State, token);

    internal void Apply(Settings snapshot)
    {
        lock (gate)
        {
            settings = snapshot;
            // The board may just have been switched on during a show that began after its latest 期.
            if (roomId is { } room) EnterLive(room, liveTime);
            else Publish();
        }
    }

    /// <param name="liveTime">When the room's 一场直播 began as room_init says, null off air.</param>
    internal void Connect(long room, long? liveTime)
    {
        lock (gate)
        {
            // A reconnect of the same room connection comes through here too and changes nothing.
            if (roomId == room) return;
            roomId = room;
            EnterLive(room, liveTime);
        }
    }

    /// <summary>The room's 一场直播 as room_init says after a LIVE message.</summary>
    internal void UpdateLiveTime(long room, long? liveTime)
    {
        lock (gate)
        {
            if (roomId == room) EnterLive(room, liveTime);
        }
    }

    internal void EndRoom(long room)
    {
        lock (gate)
        {
            if (roomId != room) return;
            roomId = null;
            seen.Clear();
            Publish();
        }
    }

    /// <summary>Takes the room's 一场直播 as just read from room_init: a different show than the latest 期 belongs to opens a new 期.</summary>
    private void EnterLive(long room, long? liveTime)
    {
        this.liveTime = liveTime;
        if (liveTime is { } began && settings.GiftBoardEnabled.Value && settings.GiftBoardResetOnLive.Value)
            store().OpenForLive(room, time.GetUtcNow(), began);
        Publish();
    }

    internal void Receive(long room, IEnumerable<Gift> gifts)
    {
        lock (gate)
        {
            if (!settings.GiftBoardEnabled.Value || roomId != room) return;
            var accepted = gifts.Where(Accept).ToArray();
            if (accepted.Length == 0) return;
            // shortcut: reads and writes SQLite on the receive loop, as the blind box store does; move off it if a busy room lags.
            store().Add(room, accepted, time.GetUtcNow(), liveTime);
            subscribers.Publish(State() with { FromGift = true });
        }
    }

    /// <summary>Opens a new, empty 期 for the connected room. The old ones stay in the database, and a show under way is
    /// remembered, so connecting again during it does not open yet another.</summary>
    public void Clear()
    {
        lock (gate)
        {
            store().Open(ConnectedRoom(), time.GetUtcNow(), liveTime);
            Publish();
        }
    }

    /// <summary>Merges the connected room's latest 期 back into the one before it, as if the board had never moved on. A show
    /// under way is remembered, as with <see cref="Clear"/>.</summary>
    public void Restore()
    {
        lock (gate)
        {
            if (!store().Restore(ConnectedRoom(), liveTime)) throw new InvalidOperationException("没有上一期可以恢复。");
            Publish();
        }
    }

    private long ConnectedRoom() => roomId ?? throw new InvalidOperationException("没有连着直播间。");

    private bool Accept(Gift gift)
    {
        if (gift.Uid == 0)
        {
            // Shown at Information: whether 神秘人 gifts arrive with uid 0 is unverified, and these stay off the board.
            logger.LogInformation("uid 为 0 的礼物未上礼物榜（可能是神秘人），礼物 {Gift}，个数 {Num}，tid：{Tid}", gift.GiftName, gift.Num, gift.Tid);
            return false;
        }
        if (gift.Tid is null || seen.Add(gift.Tid)) return true;
        logger.LogWarning("礼物榜：重复推送的礼物，已忽略，tid：{Tid}，礼物 {Gift}，个数 {Num}", gift.Tid, gift.GiftName, gift.Num);
        return false;
    }

    private void Publish() => subscribers.Publish(State());

    private GiftBoardState State()
    {
        var periods = roomId is { } room ? store().Periods(room) : [];
        return new(Rows(), settings.GiftBoardMascot.Value, settings.GiftBoardVolume.Value,
            periods.ElementAtOrDefault(0), periods.ElementAtOrDefault(1));
    }

    private ImmutableArray<GiftBoardRow> Rows()
    {
        if (!settings.GiftBoardEnabled.Value || roomId is not { } room) return [];
        var minimum = Battery.ToCoins(settings.GiftBoardThreshold.Value);
        var byTotal = settings.GiftBoardThresholdBy.Value == Settings.GiftBoardByTotalPrice;
        // shortcut: reads the whole 期 on every change; filter in SQL if a long-running 总榜 slows it.
        return [.. store().Rows(room).Where(row => (byTotal ? row.Value : row.Price) >= minimum)];
    }

    public void Dispose()
    {
        lock (gate) subscribers.Complete();
    }
}
