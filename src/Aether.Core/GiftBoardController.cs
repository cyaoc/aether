using System.Collections.Immutable;

namespace Aether.Core;

/// <summary>The 礼物榜 of the room the GUI is connected to, run in 期 like a lottery (ADR 0014): the streamer starts a 期, ends
/// it and may continue it, and only a running 期 takes gifts and shows on the 礼物榜页. The latest 期's totals are kept here:
/// read from the 收礼记录 on connecting, then added to as gifts arrive, before <see cref="GiftRecording"/> writes them, so what
/// the board shows neither waits for the database nor depends on it.</summary>
public sealed class GiftBoardController : IDisposable
{
    private readonly object gate = new();
    private readonly Func<GiftBoardStore> store;
    private readonly TimeProvider time;
    private readonly Subscribers<GiftBoardState> subscribers;
    private long? roomId;
    // The connected room's latest 期, and all its rows whatever the 上榜门槛 (which is applied when showing them), by row Id.
    private GiftBoardSnapshot board = Empty;
    private readonly Dictionary<string, (GiftBoardRow Row, long First)> totals = [];
    // The tids counted since the room connected, and those of its latest 期 before: a push B站 repeats counts once, written or not.
    private readonly HashSet<string> counted = [];
    // The order in which totals first appeared, after every 收礼记录 row read.
    private long sequence;
    // The latest 期's options, or those the room's first 期 will start with.
    private GiftBoardOptions options = GiftBoardOptions.Default;

    private static readonly GiftBoardSnapshot Empty = new(null, null, null, [], [], 0);

    internal GiftBoardController(Func<GiftBoardStore> store, TimeProvider time)
    {
        this.store = store;
        this.time = time;
        subscribers = new(gate);
    }

    /// <summary>The current state first, then one per change.</summary>
    public IAsyncEnumerable<GiftBoardState> GetUpdatesAsync(CancellationToken token = default) =>
        subscribers.SubscribeAsync(State, token);

    internal void Connect(long room)
    {
        lock (gate)
        {
            // A reconnect of the same room connection comes through here too and changes nothing.
            if (roomId == room) return;
            // Read first: a room whose board could not be read stays unconnected, and the next reconnect tries again.
            var snapshot = store().Load(room);
            roomId = room;
            Load(snapshot);
            Publish();
        }
    }

    internal void EndRoom(long room)
    {
        lock (gate)
        {
            if (roomId != room) return;
            roomId = null;
            Load(Empty);
            Publish();
        }
    }

    /// <summary>Counts gifts of <paramref name="room"/> while its 期 runs and pushes the state they made, the one the 礼物榜页
    /// plays. Returns the 期 they belong to, for their 收礼记录 rows, or null when the board takes none. A tid already counted
    /// is B站 pushing it again and counts once; it still belongs to the 期, so its write can report the repeat.</summary>
    internal long? Take(long room, IEnumerable<Gift> gifts)
    {
        lock (gate)
        {
            if (roomId != room || !Running) return null;
            var added = false;
            foreach (var gift in gifts)
            {
                // uid 0 is not recorded, which GiftStore reports.
                if (gift.Uid == 0 || gift.Tid is { } tid && !counted.Add(tid)) continue;
                Add(gift);
                added = true;
            }
            if (added) subscribers.Publish(State() with { FromGift = true });
            return board.PeriodId;
        }
    }

    /// <summary>Starts a new 期 for the connected room, with the options the last one had; the old ones stay in the database.
    /// The running 期 has to be ended first.</summary>
    public void StartNewPeriod() => Change(room =>
    {
        if (Running) throw new InvalidOperationException("这一期还在统计，先结束再开始新一期。");
        var at = time.GetUtcNow();
        board = board with { PeriodId = store().Start(room, at, options), Period = new(at, null) };
        totals.Clear();
    });

    /// <summary>Ends the connected room's running 期: it takes no more gifts and the 礼物榜页 shows nothing.</summary>
    public void EndPeriod() => Change(room =>
    {
        if (board.Period is not { EndedAt: null } running) throw new InvalidOperationException("没有在统计的一期。");
        var at = time.GetUtcNow();
        store().End(room, at);
        board = board with { Period = running with { EndedAt = at } };
    });

    /// <summary>Runs the connected room's ended 期 again.</summary>
    public void ContinuePeriod() => Change(room =>
    {
        if (board.Period is not { EndedAt: not null } ended) throw new InvalidOperationException("没有结束的一期可以继续。");
        store().Continue(room);
        board = board with { Period = ended with { EndedAt = null } };
    });

    /// <summary>Gives the connected room's latest 期 these options, at once; a room without one starts its first with them.</summary>
    public void SetOptions(GiftBoardOptions changed)
    {
        if (changed.Error is { } error) throw new ArgumentException(error, nameof(changed));
        lock (gate)
        {
            var room = ConnectedRoom();
            if (board.Period is not null) store().SetOptions(room, changed);
            options = changed;
            Publish();
        }
    }

    // The database first: a change it refuses leaves the board as it was. The totals stay in memory, gifts on their way to
    // the database with them.
    private void Change(Action<long> change)
    {
        lock (gate)
        {
            change(ConnectedRoom());
            Publish();
        }
    }

    private bool Running => GiftBoardPeriod.PhaseOf(board.Period) == GiftBoardPhase.Running;

    private long ConnectedRoom() => roomId ?? throw new InvalidOperationException("没有连着直播间。");

    private void Add(Gift gift)
    {
        var key = GiftBoardRow.IdOf(gift.Uid, gift.BoardKey);
        totals[key] = totals.TryGetValue(key, out var total)
            ? (total.Row with
            {
                Nickname = gift.Nickname, FaceUrl = gift.FaceUrl, GiftName = gift.GiftName, IconUrl = gift.IconUrl,
                Value = checked(total.Row.Value + gift.Value), Num = checked(total.Row.Num + gift.Num)
            }, total.First)
            : (new(key, gift.Uid, gift.Nickname, gift.FaceUrl, gift.GiftName, gift.IconUrl, gift.GuardLevel, gift.Value, gift.Num), ++sequence);
    }

    private void Load(GiftBoardSnapshot snapshot)
    {
        board = snapshot with { Totals = [], Tids = [] };
        options = snapshot.Options ?? GiftBoardOptions.Default;
        totals.Clear();
        foreach (var total in snapshot.Totals) totals[total.Row.Id] = total;
        counted.Clear();
        counted.UnionWith(snapshot.Tids);
        sequence = snapshot.LastRecord;
    }

    private void Publish() => subscribers.Publish(State());

    private GiftBoardState State() => new(Rows(), options, roomId is not null, board.Period);

    // From the highest 价值 down, equal values by who was sent first.
    private ImmutableArray<GiftBoardRow> Rows()
    {
        var minimum = Battery.ToCoins(options.Threshold);
        var byTotal = options.ThresholdBy == GiftBoardOptions.ByTotalPrice;
        return [.. totals.Values.Where(total => (byTotal ? total.Row.Value : total.Row.Price) >= minimum)
            .OrderByDescending(total => total.Row.Value).ThenBy(total => total.First).Select(total => total.Row)];
    }

    public void Dispose()
    {
        lock (gate) subscribers.Complete();
    }
}
