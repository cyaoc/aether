using System.Collections.Immutable;
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
    private const string DrawInProgress = "请等本次开奖结束。";
    private const string InvalidUid = "UID 必须是正整数或留空";
    private readonly object gate = new();
    private readonly Func<LotteryStore> store;
    private readonly TimeProvider time;
    private readonly ILogger logger;
    private readonly Random random;
    private readonly Subscribers<LotteryUpdate> subscribers;
    private LotteryState state = new(null, false, null);
    private ITimer? drawTimer;
    private bool disposed;

    internal LotteryController(Func<LotteryStore> store, TimeProvider time, ILogger logger, Random random)
    {
        this.store = store;
        this.time = time;
        this.logger = logger;
        this.random = random;
        subscribers = new(gate);
    }

    public LotteryState State { get { lock (gate) return state; } }

    public void SetPinnedResults(bool enabled)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            state = state with { PinnedResults = enabled };
            Broadcast();
        }
    }

    /// <summary>Read-only, including unfinished lotteries; no room connection is required.</summary>
    public ImmutableArray<LotteryHistory> GetHistory(long roomId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return store().History(roomId);
    }

    public Lottery? GetHistoryDetails(long roomId, string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return store().Load(roomId, id: id);
    }

    public ImmutableArray<LotteryPrizeSet> GetPrizeSets(long roomId, LotteryMode mode)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return store().PrizeSets(roomId, mode);
    }

    public void SavePrizeSet(long roomId, LotteryMode mode, string name,
        IReadOnlyCollection<PoolPrize> prizes, IReadOnlyCollection<LotteryAward> awards)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("请给方案起个名字。");
        if (!Enum.IsDefined(mode)) throw new ArgumentException("请选择抽奖方式。");
        if (mode == LotteryMode.Pool) Validate(prizes);
        else Validate(awards);
        store().SavePrizeSet(roomId, new(mode, name.Trim(), time.GetUtcNow(),
            mode == LotteryMode.Pool ? prizes.ToImmutableArray() : [],
            mode == LotteryMode.Entry ? awards.ToImmutableArray() : []));
    }

    public void DeletePrizeSet(long roomId, LotteryMode mode, string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        store().DeletePrizeSet(roomId, mode, name);
    }

    public IAsyncEnumerable<LotteryUpdate> GetUpdatesAsync(CancellationToken token = default) =>
        subscribers.SubscribeAsync(() =>
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new(state, Example: example);
        }, token);

    internal void Connect(long roomId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var lottery = store().Load(roomId);
            // Who has spoken lives only in memory, so a transport reconnect keeps it and a restart starts over.
            if (lottery is PoolLottery pool && state.Lottery is PoolLottery previous && previous.Id == pool.Id)
                lottery = pool with { Viewers = previous.Viewers };
            drawTimer?.Dispose();
            // Recovery may write an expired draw; a failed write must leave the next danmaku able to retry.
            state = new(roomId, false, lottery) { CanControl = true, Theme = state.Theme, PinnedResults = state.PinnedResults };
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
        ? null : InvalidUid;

    private static void Validate(IReadOnlyCollection<PoolPrize> prizes)
    {
        if (prizes.Count is < 1 or > MaxPrizes) throw new ArgumentException($"奖品须为 1 至 {MaxPrizes} 件。");
        foreach (var prize in prizes)
            if (ValidatePrize(prize) is { } error) throw new ArgumentException(error);
    }

    public void Start(IReadOnlyCollection<PoolPrize> prizes, string stopWord, long? uid = null)
    {
        Validate(prizes);
        if (ValidateStopWord(stopWord) is { } stopWordError) throw new ArgumentException(stopWordError);
        if (uid <= 0) throw new ArgumentException(InvalidUid);
        lock (gate)
        {
            RequireConnection();
            if (state.Lottery is EntryLottery) throw new InvalidOperationException("请先新开一场奖池抽奖。");
            RequireNotDrawing();
            if (state.Lottery is PoolLottery { Phase: PoolLotteryPhase.Spinning }) throw new InvalidOperationException(DrawInProgress);
            var lottery = state.Lottery is PoolLottery current ? current : NewPoolLottery(prizes, stopWord);
            Save(lottery with { Prizes = prizes.ToImmutableArray(), StopWord = stopWord.Trim(), Uid = uid, Phase = PoolLotteryPhase.Spinning });
            logger.LogInformation("奖池抽奖开始：直播间 {RoomId}，抽奖 {LotteryId}，观众 {Uid}", state.RoomId, lottery.Id, uid);
        }
    }

    private PoolLottery NewPoolLottery(IReadOnlyCollection<PoolPrize> prizes, string stopWord) =>
        new(Ids.New(), state.RoomId!.Value, stopWord.Trim(), time.GetUtcNow(),
            prizes.ToImmutableArray(), [], ImmutableDictionary<long, string>.Empty);

    private void RequireConnection()
    {
        RequireControl();
        if (!state.Connected) throw new InvalidOperationException(ConnectionRequired);
    }

    private void RequireControl()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!state.CanControl) throw new InvalidOperationException(ConnectionRequired);
    }

    private void RequireNotDrawing()
    {
        if (state.Lottery is { IsDrawing: true }) throw new InvalidOperationException(DrawInProgress);
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

    public void SetDrawHiddenFromStream(string drawId, bool hidden)
    {
        ArgumentException.ThrowIfNullOrEmpty(drawId);
        ChangeDrawVisibility(drawId, hidden);
    }

    public void HideAllDrawsFromStream() => ChangeDrawVisibility(null, true);

    private void ChangeDrawVisibility(string? drawId, bool hidden)
    {
        lock (gate)
        {
            RequireControl();
            Lottery lottery = state.Lottery switch
            {
                PoolLottery pool => pool with { Draws = [.. pool.Draws.Select(draw =>
                    drawId is null || draw.Id == drawId ? draw with { HiddenFromStream = hidden } : draw)] },
                EntryLottery entry => entry with { Draws = [.. entry.Draws.Select(draw =>
                    drawId is null || draw.Id == drawId ? draw with { HiddenFromStream = hidden } : draw)] },
                _ => throw new InvalidOperationException("当前没有抽奖。"),
            };
            store().SetDrawHiddenFromStream(lottery.Id, drawId, hidden);
            state = state with { Lottery = lottery };
            Publish();
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
        var draw = new PoolDraw(Ids.New(), winner, lottery.Uid,
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
        Save(finished);
    }

    private void Save(Lottery lottery, LotteryDraw? draw = null)
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

    private void Broadcast() => subscribers.Publish(new(state, example));

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            drawTimer?.Dispose();
            ClearExample();
            subscribers.Complete();
        }
    }
}
