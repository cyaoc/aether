using System.Collections.Immutable;

namespace Aether.Core;

/// <summary>The 礼物榜 of the room the GUI is connected to, run in 期 like a lottery (ADR 0014): the streamer starts a 期, ends
/// it and may continue it, and only a running 期 takes gifts and shows on the 礼物榜页. <see cref="GiftRecording"/> writes the
/// gifts to the 收礼记录; the latest 期's totals are kept here, read from the 收礼记录 on connecting and when a 期 starts, and
/// added to as gifts arrive.</summary>
public sealed class GiftBoardController : IDisposable
{
    private readonly object gate = new();
    private readonly Func<GiftBoardStore> store;
    private readonly TimeProvider time;
    private readonly Subscribers<GiftBoardState> subscribers;
    private long? roomId;
    // The connected room's latest 期, all its rows whatever the 上榜门槛 (which is applied when showing them), by row Id.
    private GiftBoardSnapshot board = Empty;
    private readonly Dictionary<string, (GiftBoardRow Row, long First)> totals = [];
    // The latest 期's options, or those the room's first 期 will start with.
    private GiftBoardOptions options = GiftBoardOptions.Default;

    private static readonly GiftBoardSnapshot Empty = new(null, null, [], 0);

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

    /// <summary>Whether the board takes the gifts of <paramref name="room"/> now: that room is connected and its 期 running.</summary>
    internal bool Wants(long room)
    {
        lock (gate) return roomId == room && Running;
    }

    /// <summary>Gifts of <paramref name="room"/> were written to the 收礼记录 for the board, each with its row's id: adds them to
    /// the latest 期 and pushes the state they made, the one the 礼物榜页 plays.</summary>
    internal void ShowGifts(long room, IEnumerable<(Gift Gift, long Id)> written)
    {
        lock (gate)
        {
            if (roomId != room) return;
            var added = false;
            foreach (var (gift, id) in written)
            {
                // Already read with the 期, or written before it began.
                if (id <= board.Through) continue;
                Add(gift, id);
                board = board with { Through = id };
                added = true;
            }
            if (added) subscribers.Publish(State() with { FromGift = true });
        }
    }

    /// <summary>Starts a new 期 for the connected room, with the options the last one had; the old ones stay in the database.
    /// The running 期 has to be ended first.</summary>
    public void StartNewPeriod() => Change(room =>
    {
        if (Running) throw new InvalidOperationException("这一期还在统计，先结束再开始新一期。");
        store().Start(room, time.GetUtcNow(), options);
    });

    /// <summary>Ends the connected room's running 期: it takes no more gifts and the 礼物榜页 shows nothing.</summary>
    public void EndPeriod() => Change(room =>
    {
        if (!Running) throw new InvalidOperationException("没有在统计的一期。");
        store().End(room, time.GetUtcNow());
    });

    /// <summary>Runs the connected room's ended 期 again.</summary>
    public void ContinuePeriod() => Change(room =>
    {
        if (board.Period is not { EndedAt: not null }) throw new InvalidOperationException("没有结束的一期可以继续。");
        store().Continue(room);
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

    private void Change(Action<long> change)
    {
        lock (gate)
        {
            var room = ConnectedRoom();
            change(room);
            Load(store().Load(room));
            Publish();
        }
    }

    private bool Running => board.Period is { EndedAt: null };

    private long ConnectedRoom() => roomId ?? throw new InvalidOperationException("没有连着直播间。");

    private void Add(Gift gift, long id)
    {
        var key = $"{gift.Uid}/{gift.BoardKey}";
        totals[key] = totals.TryGetValue(key, out var total)
            ? (total.Row with
            {
                Nickname = gift.Nickname, FaceUrl = gift.FaceUrl, GiftName = gift.GiftName, IconUrl = gift.IconUrl,
                Value = checked(total.Row.Value + gift.Value), Num = checked(total.Row.Num + gift.Num)
            }, total.First)
            : (new(key, gift.Uid, gift.Nickname, gift.FaceUrl, gift.GiftName, gift.IconUrl, gift.GuardLevel, gift.Value, gift.Num), id);
    }

    private void Load(GiftBoardSnapshot snapshot)
    {
        board = snapshot with { Totals = [] };
        options = snapshot.Options ?? GiftBoardOptions.Default;
        totals.Clear();
        foreach (var total in snapshot.Totals) totals[total.Row.Id] = total;
    }

    private void Publish() => subscribers.Publish(State());

    private GiftBoardState State() => new(Rows(), Running, options, roomId is not null, board.Period);

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
