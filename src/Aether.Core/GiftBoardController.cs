using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The 礼物榜 of the room this client is connected to: records the room's gifts into its latest 期 and tells the
/// 礼物榜页 and the GUI what to show. It follows the settings last applied, so a save needs no new room connection.</summary>
public sealed class GiftBoardController : IDisposable
{
    // 1 电池 = 100 金瓜子 (GLOSSARY).
    private const long CoinsPerBattery = 100;
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
    // When the 一场直播 the room is in began (Unix seconds); null off air, and while no room is connected.
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
            Publish();
        }
    }

    internal void Connect(long room, long? live)
    {
        lock (gate)
        {
            // A reconnect of the same room connection comes through here too and changes nothing.
            if (roomId == room) return;
            roomId = room;
            EnterLive(room, live);
        }
    }

    /// <summary>The room's 一场直播 as room_init says after a LIVE message.</summary>
    internal void UpdateLiveTime(long room, long? live)
    {
        lock (gate)
        {
            if (roomId == room) EnterLive(room, live);
        }
    }

    internal void EndRoom(long room)
    {
        lock (gate)
        {
            if (roomId != room) return;
            roomId = null;
            liveTime = null;
            seen.Clear();
            Publish();
        }
    }

    /// <summary>Takes the room's 一场直播 as just read from room_init: a different show than the latest 期 belongs to opens a new 期.</summary>
    private void EnterLive(long room, long? live)
    {
        liveTime = live;
        if (live is { } began && settings.GiftBoardEnabled.Value && settings.GiftBoardResetOnLive.Value)
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
            store().Add(room, accepted, time.GetUtcNow());
            Publish();
        }
    }

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

    private GiftBoardState State() => new(Rows(), settings.GiftBoardMascot.Value, settings.GiftBoardVolume.Value);

    private ImmutableArray<GiftBoardRow> Rows()
    {
        if (!settings.GiftBoardEnabled.Value || roomId is not { } room) return [];
        var minimum = settings.GiftBoardThreshold.Value * CoinsPerBattery;
        var byTotal = settings.GiftBoardThresholdBy.Value == Settings.GiftBoardByTotalPrice;
        // shortcut: reads the whole 期 on every change; filter in SQL if a long-running 总榜 slows it.
        return [.. store().Rows(room).Where(row => (byTotal ? row.Value : row.Price) >= minimum)];
    }

    public void Dispose()
    {
        lock (gate) subscribers.Complete();
    }
}
