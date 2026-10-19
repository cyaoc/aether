using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room's lottery, independent of the browser and the room transport lifetime.</summary>
public sealed partial class LotteryController : IDisposable
{
    public static readonly TimeSpan DrawDuration = TimeSpan.FromSeconds(7);
    public const int MaxPrizes = 10;
    public const string DefaultStopWord = "停";
    public static PoolPrize DefaultPrize { get; } = new("", 1, Grade.Grand);
    public static IReadOnlyList<string> GradeNames { get; } = ["大奖", "二奖", "三奖"];
    // Matches the number of cells in the lottery page's carousel.
    internal const int DrawCells = 10;
    private const string ConnectionRequired = "请先连接直播间。";
    private readonly object gate = new();
    private readonly Func<LotteryStore> store;
    private readonly TimeProvider time;
    private readonly ILogger logger;
    private readonly Random random;
    private readonly List<Channel<LotteryUpdate>> subscribers = [];
    private LotteryState state = new(null, false, null);
    private ITimer? drawTimer;
    private bool disposed;

    internal LotteryController(Func<LotteryStore> store, TimeProvider time, ILogger logger, Random random)
    {
        this.store = store;
        this.time = time;
        this.logger = logger;
        this.random = random;
    }

    public LotteryState State { get { lock (gate) return state; } }

    /// <summary>Read-only, including unfinished lotteries; no room connection is required.</summary>
    public ImmutableArray<LotteryHistory> GetHistory(long roomId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return store().History(roomId);
    }

    public LotteryHistoryDetails? GetHistoryDetails(long roomId, string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return store().HistoryDetails(roomId, id);
    }

    public async IAsyncEnumerable<LotteryUpdate> GetUpdatesAsync([EnumeratorCancellation] CancellationToken token = default)
    {
        // shortcut: local readers use unbounded streams like WatchAsync; bound these if a stalled shell becomes a problem.
        var updates = Channel.CreateUnbounded<LotteryUpdate>();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            subscribers.Add(updates);
            updates.Writer.TryWrite(new(state, Example: example));
        }
        try { await foreach (var update in updates.Reader.ReadAllAsync(token)) yield return update; }
        finally { lock (gate) subscribers.Remove(updates); }
    }

    internal void Connect(long roomId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Lottery? lottery = store().LoadEntry(roomId);
            lottery ??= store().LoadPool(roomId);
            // Who has spoken lives only in memory, so a transport reconnect keeps it and a restart starts over.
            if (lottery is PoolLottery pool && state.Lottery is PoolLottery previous && previous.Id == pool.Id)
                lottery = pool with { Viewers = previous.Viewers };
            drawTimer?.Dispose();
            // Recovery may write an expired draw; a failed write must leave the next danmaku able to retry.
            state = new(roomId, false, lottery) { CanControl = true, Theme = state.Theme };
            ResumeDraw();
            state = state with { Connected = true };
            Publish();
        }
    }

    internal void Disconnect(long roomId)
    {
        lock (gate)
            if (state.RoomId == roomId) { state = state with { Connected = false }; Publish(); }
    }

    internal void EndRoom(long roomId)
    {
        lock (gate)
            if (state.RoomId == roomId) { state = state with { Connected = false, CanControl = false }; Publish(); }
    }

    public static string? ValidatePrize(PoolPrize prize) => string.IsNullOrWhiteSpace(prize.Name) ? "请填写奖品名称。"
        : prize.Weight <= 0 ? "占比必须是正整数。" : !Enum.IsDefined(prize.Grade) ? "请选择档次。" : null;

    public static string? ValidateStopWord(string? text) => string.IsNullOrWhiteSpace(text) ? "口令不能为空" : null;

    public static string? ValidateUid(string? text) => string.IsNullOrWhiteSpace(text) || Viewer.TryParseUid(text, out _)
        ? null : "UID 必须是正整数或留空";

    private static void Validate(IReadOnlyCollection<PoolPrize> prizes, string stopWord, long? uid)
    {
        if (prizes.Count is < 1 or > MaxPrizes) throw new ArgumentException($"奖品须为 1 至 {MaxPrizes} 件。");
        foreach (var prize in prizes)
            if (ValidatePrize(prize) is { } error) throw new ArgumentException(error);
        if (ValidateStopWord(stopWord) is { } stopWordError) throw new ArgumentException(stopWordError);
        if (ValidateUid(uid?.ToString(CultureInfo.InvariantCulture)) is { } uidError) throw new ArgumentException(uidError);
    }

    public void Start(IReadOnlyCollection<PoolPrize> prizes, string stopWord, long? uid = null)
    {
        Validate(prizes, stopWord, uid);
        lock (gate)
        {
            RequireConnection();
            if (state.Lottery is EntryLottery) throw new InvalidOperationException("请先新开一场奖池抽奖。");
            RequireNotDrawing(includeSpinning: true);
            var lottery = state.Lottery is PoolLottery current ? current : NewPoolLottery(prizes, stopWord);
            Save(lottery with { Prizes = prizes.ToImmutableArray(), StopWord = stopWord.Trim(), Uid = uid, Phase = PoolLotteryPhase.Spinning });
            logger.LogInformation("奖池抽奖开始：直播间 {RoomId}，抽奖 {LotteryId}，观众 {Uid}", state.RoomId, lottery.Id, uid);
        }
    }

    private PoolLottery NewPoolLottery(IReadOnlyCollection<PoolPrize> prizes, string stopWord) =>
        new(Guid.NewGuid().ToString("N"), state.RoomId!.Value, stopWord.Trim(), time.GetUtcNow(),
            prizes.ToImmutableArray(), [], ImmutableDictionary<long, string>.Empty);

    private void RequireConnection()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!state.Connected) throw new InvalidOperationException(ConnectionRequired);
    }

    private void RequireControl()
    {
        if (!state.CanControl) throw new InvalidOperationException(ConnectionRequired);
    }

    private void RequireNotDrawing(bool includeSpinning = false)
    {
        if (state.Lottery is { IsDrawing: true } || includeSpinning && state.Lottery is PoolLottery { Phase: PoolLotteryPhase.Spinning })
            throw new InvalidOperationException("请等本次开奖结束。");
    }

    public void Cancel()
    {
        lock (gate)
        {
            RequireControl();
            if (state.Lottery is not PoolLottery { Phase: PoolLotteryPhase.Spinning } lottery) throw new InvalidOperationException("当前没有转动中的抽奖。");
            Save(lottery with { Phase = PoolLotteryPhase.Idle });
            logger.LogInformation("奖池抽奖取消：抽奖 {LotteryId}", lottery.Id);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            RequireControl();
            if (state.Lottery is not PoolLottery { Phase: PoolLotteryPhase.Spinning }) throw new InvalidOperationException("当前没有转动中的抽奖。");
            Draw(stoppedByHost: true);
        }
    }

    internal void Receive(long roomId, Danmaku danmaku)
    {
        lock (gate)
        {
            if (state.RoomId == roomId && state.Lottery is EntryLottery) { ReceiveEntry(danmaku); return; }
            if (state.RoomId != roomId || state.Lottery is not PoolLottery lottery || danmaku.Uid <= 0) return;
            if (!lottery.Viewers.TryGetValue(danmaku.Uid, out var name) || name != danmaku.Nickname)
            {
                lottery = lottery with { Viewers = lottery.Viewers.SetItem(danmaku.Uid, danmaku.Nickname) };
                state = state with { Lottery = lottery };
                Broadcast();
            }
            if (lottery.Phase == PoolLotteryPhase.Spinning && lottery.Uid == danmaku.Uid
                && danmaku.Content.Trim() == lottery.StopWord) Draw(stoppedByHost: false);
        }
    }

    private void Draw(bool stoppedByHost)
    {
        var lottery = (PoolLottery)state.Lottery!;
        var total = lottery.Prizes.Sum(prize => (long)prize.Weight);
        var pick = random.NextInt64(total);
        var winner = lottery.Prizes[^1];
        foreach (var prize in lottery.Prizes)
        {
            if (pick < prize.Weight) { winner = prize; break; }
            pick -= prize.Weight;
        }
        // Every prize gets a cell, so a theme can always stop on the winner; the other cells go by weight (D'Hondt).
        // The cells only show the pool and never change the actual draw odds.
        var cells = lottery.Prizes.Select(_ => 1).ToArray();
        for (var filled = cells.Length; filled < DrawCells; filled++)
        {
            var next = 0;
            for (var index = 1; index < cells.Length; index++)
                if ((long)lottery.Prizes[index].Weight * (cells[next] + 1) > (long)lottery.Prizes[next].Weight * (cells[index] + 1)) next = index;
            cells[next]++;
        }
        var candidates = ImmutableArray.CreateBuilder<PoolPrize>(DrawCells);
        for (var index = 0; index < cells.Length; index++) candidates.AddRange(Enumerable.Repeat(lottery.Prizes[index], cells[index]));
        var now = time.GetUtcNow();
        var draw = new PoolDraw(Guid.NewGuid().ToString("N"), winner, lottery.Uid,
            lottery.Uid is { } uid ? lottery.Viewers.GetValueOrDefault(uid) : null, stoppedByHost, now, candidates.MoveToImmutable());
        Save(lottery with { Draws = lottery.Draws.Add(draw), Phase = PoolLotteryPhase.Idle, DrawUntil = now + DrawDuration }, draw);
        ResumeDraw();
        logger.LogInformation("奖池抽奖开奖：抽奖 {LotteryId}，{Grade} {Prize}，观众 {Uid} {Nickname}，主播点停 {Host}",
            lottery.Id, winner.Grade, winner.Name, draw.Uid, draw.Nickname, stoppedByHost);
    }

    private void ResumeDraw()
    {
        if (state.Lottery is not { DrawUntil: { } until } lottery) return;
        var id = lottery.Id;
        var remaining = until - time.GetUtcNow();
        if (remaining <= TimeSpan.Zero) { FinishDraw(id, until); return; }
        drawTimer?.Dispose();
        drawTimer = time.CreateTimer(_ =>
        {
            lock (gate)
            {
                try { FinishDraw(id, until); }
                catch (Exception error)
                {
                    logger.LogError(error, "保存开奖结束状态失败");
                    // A temporary storage failure must not leave the host stuck in 开奖中.
                    drawTimer?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
                }
            }
        }, null, remaining, Timeout.InfiniteTimeSpan);
    }

    private void FinishDraw(string id, DateTimeOffset until)
    {
        if (disposed || state.Lottery is not { IsDrawing: true } lottery || lottery.Id != id || lottery.DrawUntil != until) return;
        var finished = lottery with { DrawUntil = null };
        if (!state.CanControl)
        {
            // A different process may own the room now; its next load uses the persisted draw deadline.
            state = state with { Lottery = finished };
            Publish();
            return;
        }
        switch (finished)
        {
            case PoolLottery pool: Save(pool); break;
            case EntryLottery entry: SaveEntry(entry); break;
        }
    }

    private void Save(PoolLottery lottery, PoolDraw? draw = null)
    {
        store().Save(lottery, draw: draw);
        state = state with { Lottery = lottery };
        Publish();
    }

    private void Publish()
    {
        // Live changes take the screen back immediately, without ever saving example data.
        ClearExample();
        Broadcast();
    }

    private void Broadcast()
    {
        foreach (var subscriber in subscribers) subscriber.Writer.TryWrite(new(state, example));
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            drawTimer?.Dispose();
            ClearExample();
            foreach (var subscriber in subscribers) subscriber.Writer.TryComplete();
            subscribers.Clear();
        }
    }
}
