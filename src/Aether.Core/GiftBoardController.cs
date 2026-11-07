using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

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
    private readonly Func<long, Task<string>> faceOf;
    private readonly ILogger logger;
    private readonly Subscribers<GiftBoardState> subscribers;
    private long? roomId;
    // The connected room's latest 期, and all its rows whatever the 上榜门槛 (which is applied when showing them), by row Id.
    private GiftBoardSnapshot board = Empty;
    private readonly Dictionary<string, (GiftBoardRow Row, long First)> totals = [];
    // Each tid the room's 收礼记录 has or its connection has seen, with the 期 the board counted it in, null for none: a push B站
    // repeats counts once, in that 期, written or not.
    private readonly Dictionary<string, long?> counted = [];
    // The order in which totals first appeared, after every 收礼记录 row read.
    private long sequence;
    // The latest 期's options, or those the room's first 期 will start with.
    private GiftBoardOptions options = GiftBoardOptions.Default;

    private static readonly GiftBoardSnapshot Empty = new(null, null, null, [], [], 0);

    /// <param name="faceOf">Asks B站 for a viewer's avatar by uid.</param>
    internal GiftBoardController(Func<GiftBoardStore> store, TimeProvider time, Func<long, Task<string>> faceOf, ILogger logger)
    {
        this.store = store;
        this.time = time;
        this.faceOf = faceOf;
        this.logger = logger;
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
            FillFaces(room, totals.Values.Select(total => total.Row.Uid));
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
    /// plays. Returns the 期 each gift belongs to, for its 收礼记录 row, null for one the board did not take. A tid seen before
    /// is B站 pushing it again: it counts once and belongs to the 期 it first came in, so its write reports the repeat, or makes
    /// up one that failed, there.</summary>
    internal Func<Gift, long?> Take(long room, IEnumerable<Gift> gifts)
    {
        lock (gate)
        {
            if (roomId != room) return _ => null;
            var period = Running ? board.PeriodId : null;
            Dictionary<string, long?> repeats = [];
            List<long> added = [];
            foreach (var gift in gifts)
            {
                // uid 0 is not recorded, which GiftStore reports.
                if (gift.Uid == 0) continue;
                if (gift.Tid is { } tid && !counted.TryAdd(tid, period)) repeats[tid] = counted[tid];
                else if (period is not null)
                {
                    Add(gift);
                    added.Add(gift.Uid);
                }
            }
            if (added.Count > 0) subscribers.Publish(State() with { FromGift = true });
            FillFaces(room, added);
            return gift => gift.Tid is { } tid && repeats.TryGetValue(tid, out var first) ? first : period;
        }
    }

    /// <summary>Starts a new 期 for <paramref name="room"/>, with the options the last one had; the old ones stay in the
    /// database. The running 期 has to be ended first.</summary>
    public void StartNewPeriod(long room) => Change(room, () =>
    {
        if (Running) throw new InvalidOperationException("这一期还在统计，先结束再开始新一期。");
        var at = time.GetUtcNow();
        board = board with { PeriodId = store().Start(room, at, options), Period = new(at, null) };
        totals.Clear();
    });

    /// <summary>Ends <paramref name="room"/>'s running 期: it takes no more gifts and the 礼物榜页 shows nothing.</summary>
    public void EndPeriod(long room) => Change(room, () =>
    {
        if (board.Period is not { EndedAt: null } running) throw new InvalidOperationException("没有在统计的一期。");
        End(room, running);
    });

    /// <summary>Ends <paramref name="room"/>'s 期 if it is running, because the streamer is stopping (断开, closing Aether or
    /// logging out; ADR 0014); one not running is left as it is.</summary>
    public void EndPeriodOnLeaving(long room) => Change(room, () =>
    {
        if (board.Period is { EndedAt: null } running) End(room, running);
    });

    private void End(long room, GiftBoardPeriod running)
    {
        var at = time.GetUtcNow();
        store().End(room, at);
        board = board with { Period = running with { EndedAt = at } };
    }

    /// <summary>Runs <paramref name="room"/>'s ended 期 again.</summary>
    public void ContinuePeriod(long room) => Change(room, () =>
    {
        if (board.Period is not { EndedAt: not null } ended) throw new InvalidOperationException("没有结束的一期可以继续。");
        store().Continue(room);
        board = board with { Period = ended with { EndedAt = null } };
    });

    /// <summary>Gives <paramref name="room"/>'s latest 期 these options, at once; a room without one starts its first with them.</summary>
    public void SetOptions(long room, GiftBoardOptions changed)
    {
        if (changed.Error is { } error) throw new ArgumentException(error, nameof(changed));
        Change(room, () =>
        {
            if (board.Period is not null) store().SetOptions(room, changed);
            options = changed;
        });
    }

    // Each change names the room it is for, so one asked for before the room was left or switched is refused, not made to
    // the room connected since. The database first: a change it refuses leaves the board as it was. The totals stay in
    // memory, gifts on their way to the database with them.
    private void Change(long room, Action change)
    {
        lock (gate)
        {
            if (roomId != room) throw new InvalidOperationException($"没有连着直播间 {room}。");
            change();
            Publish();
        }
    }

    private bool Running => GiftBoardPeriod.PhaseOf(board.Period) == GiftBoardPhase.Running;

    private void Add(Gift gift)
    {
        var key = GiftBoardRow.IdOf(gift.Uid, gift.BoardKey);
        // B站 often leaves the avatar out, of a 大航海 purchase most of all: the gift then keeps the one its viewer has.
        var face = gift.FaceUrl.Length > 0 ? gift.FaceUrl : FaceOf(gift.Uid);
        var guard = gift.ViewerGuardAfter(ViewerOf(gift.Uid)?.ViewerGuardLevel ?? 0);
        totals[key] = totals.TryGetValue(key, out var total)
            ? (total.Row with
            {
                Nickname = gift.Nickname, FaceUrl = face, GiftName = gift.GiftName, IconUrl = gift.IconUrl,
                Value = checked(total.Row.Value + gift.Value), Num = checked(total.Row.Num + gift.Num)
            }, total.First)
            : (new(key, gift.Uid, gift.Nickname, face, gift.GiftName, gift.IconUrl, gift.GuardLevel, guard, gift.Value, gift.Num),
                ++sequence);
        if (face.Length > 0) SetRows(gift.Uid, row => row with { FaceUrl = face });
        SetRows(gift.Uid, row => row with { ViewerGuardLevel = guard });
    }

    // A viewer's rows all show the latest avatar any of them was given and the 大航海身份 the latest gift told, as the database
    // gives them back.
    private GiftBoardRow? ViewerOf(long uid) => totals.Values.Select(total => total.Row).FirstOrDefault(row => row.Uid == uid);

    private string FaceOf(long uid) => ViewerOf(uid)?.FaceUrl ?? "";

    private bool SetRows(long uid, Func<GiftBoardRow, GiftBoardRow> change)
    {
        var changed = totals.Where(pair => pair.Value.Row.Uid == uid && change(pair.Value.Row) != pair.Value.Row).ToList();
        foreach (var (key, total) in changed) totals[key] = (change(total.Row), total.First);
        return changed.Count > 0;
    }

    // Asks B站 for the avatar of each of these viewers still without one, after the gifts are counted, so the board waits for
    // neither, and pushes it when it comes. shortcut: asked once per message or connection, not again on failure; retry if
    // avatars still go missing.
    private void FillFaces(long room, IEnumerable<long> uids)
    {
        foreach (var uid in uids.Distinct().Where(uid => FaceOf(uid).Length == 0).ToList())
            _ = Task.Run(async () =>
            {
                try
                {
                    var face = await faceOf(uid);
                    lock (gate)
                    {
                        // A room left or switched, or an avatar a gift brought meanwhile, keeps what it has.
                        if (roomId == room && FaceOf(uid).Length == 0 && SetRows(uid, row => row with { FaceUrl = face })) Publish();
                    }
                }
                catch (Exception error)
                { logger.LogWarning(error, "礼物榜读取观众 {Uid} 的头像失败，先显示昵称首字：{Error}", uid, error.Message); }
            });
    }

    private void Load(GiftBoardSnapshot snapshot)
    {
        board = snapshot with { Totals = [], Tids = [] };
        options = snapshot.Options ?? GiftBoardOptions.Default;
        totals.Clear();
        foreach (var total in snapshot.Totals) totals[total.Row.Id] = total;
        counted.Clear();
        foreach (var (tid, period) in snapshot.Tids) counted[tid] = period;
        sequence = snapshot.LastRecord;
    }

    private void Publish() => subscribers.Publish(State());

    private GiftBoardState State() => new(Rows(), options, roomId, board.Period);

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
