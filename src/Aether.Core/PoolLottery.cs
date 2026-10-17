using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room's active pool lottery, independent of the browser and the room transport lifetime.</summary>
public sealed class PoolLottery : IDisposable
{
    public static readonly TimeSpan DrawDuration = TimeSpan.FromSeconds(7);
    public const int MaxPrizes = 10;
    private readonly object gate = new();
    private readonly Func<LotteryStore> store;
    private readonly TimeProvider time;
    private readonly ILogger logger;
    private readonly Random random;
    private readonly List<Channel<PoolLotteryUpdate>> subscribers = [];
    private PoolLotteryState state = new(null, false, null);
    private ITimer? drawTimer;
    private bool disposed;

    internal PoolLottery(Func<LotteryStore> store, TimeProvider time, ILogger logger, Random random)
    {
        this.store = store;
        this.time = time;
        this.logger = logger;
        this.random = random;
    }

    public PoolLotteryState State { get { lock (gate) return state; } }

    public async IAsyncEnumerable<PoolLotteryUpdate> GetUpdatesAsync([EnumeratorCancellation] CancellationToken token = default)
    {
        // shortcut: local readers use unbounded streams like WatchAsync; bound these if a stalled shell becomes a problem.
        var updates = Channel.CreateUnbounded<PoolLotteryUpdate>();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            subscribers.Add(updates);
            updates.Writer.TryWrite(new(state));
        }
        try { await foreach (var update in updates.Reader.ReadAllAsync(token)) yield return update; }
        finally { lock (gate) subscribers.Remove(updates); }
    }

    internal void Connect(long roomId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var lottery = store().Load(roomId);
            drawTimer?.Dispose();
            // Recovery may write an expired draw; a failed write must leave the next danmaku able to retry.
            state = new(roomId, false, lottery) { CanControl = true };
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

    private static void Validate(IReadOnlyCollection<PoolPrize> prizes, string stopWord, long? uid)
    {
        if (prizes.Count is < 1 or > MaxPrizes) throw new ArgumentException($"奖品须为 1 至 {MaxPrizes} 件。");
        foreach (var prize in prizes)
            if (ValidatePrize(prize) is { } error) throw new ArgumentException(error);
        if (string.IsNullOrWhiteSpace(stopWord)) throw new ArgumentException("口令不能为空。");
        if (uid is <= 0) throw new ArgumentException("UID 必须是正整数或留空。");
    }

    public void Start(IReadOnlyCollection<PoolPrize> prizes, string stopWord, long? uid = null)
    {
        Validate(prizes, stopWord, uid);
        lock (gate)
        {
            RequireConnection();
            if (state.Lottery is { Phase: not PoolLotteryPhase.Idle }) throw new InvalidOperationException("请等本次抽奖结束。");
            var lottery = state.Lottery ?? NewLottery(prizes, stopWord);
            Save(lottery with { Prizes = prizes.ToImmutableArray(), StopWord = stopWord.Trim(), Uid = uid, Phase = PoolLotteryPhase.Spinning });
            logger.LogInformation("奖池抽奖开始：直播间 {RoomId}，抽奖 {LotteryId}，观众 {Uid}", state.RoomId, lottery.Id, uid);
        }
    }

    public void New(IReadOnlyCollection<PoolPrize> prizes, string stopWord)
    {
        Validate(prizes, stopWord, null);
        lock (gate)
        {
            RequireConnection();
            if (state.Lottery is { Phase: PoolLotteryPhase.Drawing }) throw new InvalidOperationException("请等本次开奖结束。");
            var next = NewLottery(prizes, stopWord);
            store().Save(next, endCurrentAt: time.GetUtcNow());
            drawTimer?.Dispose();
            state = state with { Lottery = next };
            Publish();
            logger.LogInformation("奖池抽奖新开一场：直播间 {RoomId}，抽奖 {LotteryId}", state.RoomId, next.Id);
        }
    }

    private Lottery NewLottery(IReadOnlyCollection<PoolPrize> prizes, string stopWord) =>
        new(Guid.NewGuid().ToString("N"), state.RoomId!.Value, stopWord.Trim(), time.GetUtcNow(),
            prizes.ToImmutableArray(), [], ImmutableDictionary<long, string>.Empty);

    private void RequireConnection()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!state.Connected) throw new InvalidOperationException("请先连接直播间。");
    }

    public void Cancel()
    {
        lock (gate)
        {
            if (!state.CanControl) throw new InvalidOperationException("请先连接直播间。");
            if (state.Lottery is not { Phase: PoolLotteryPhase.Spinning } lottery) throw new InvalidOperationException("当前没有转动中的抽奖。");
            Save(lottery with { Phase = PoolLotteryPhase.Idle });
            logger.LogInformation("奖池抽奖取消：抽奖 {LotteryId}", lottery.Id);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (!state.CanControl) throw new InvalidOperationException("请先连接直播间。");
            if (state.Lottery is not { Phase: PoolLotteryPhase.Spinning }) throw new InvalidOperationException("当前没有转动中的抽奖。");
            Draw(stoppedByHost: true);
        }
    }

    internal void Receive(long roomId, Danmaku danmaku)
    {
        lock (gate)
        {
            if (state.RoomId != roomId || state.Lottery is not { } lottery || danmaku.Uid <= 0) return;
            if (!lottery.Viewers.TryGetValue(danmaku.Uid, out var name) || name != danmaku.Nickname)
            {
                store().RememberViewer(lottery.Id, danmaku.Uid, danmaku.Nickname);
                lottery = lottery with { Viewers = lottery.Viewers.SetItem(danmaku.Uid, danmaku.Nickname) };
                state = state with { Lottery = lottery };
                Publish();
            }
            if (lottery.Phase == PoolLotteryPhase.Spinning && lottery.Uid == danmaku.Uid
                && danmaku.Content.Trim() == lottery.StopWord) Draw(stoppedByHost: false);
        }
    }

    private void Draw(bool stoppedByHost)
    {
        var lottery = state.Lottery!;
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
        for (var filled = cells.Length; filled < 10; filled++)
        {
            var next = 0;
            for (var index = 1; index < cells.Length; index++)
                if ((long)lottery.Prizes[index].Weight * (cells[next] + 1) > (long)lottery.Prizes[next].Weight * (cells[index] + 1)) next = index;
            cells[next]++;
        }
        var candidates = ImmutableArray.CreateBuilder<PoolPrize>(10);
        for (var index = 0; index < cells.Length; index++) candidates.AddRange(Enumerable.Repeat(lottery.Prizes[index], cells[index]));
        var now = time.GetUtcNow();
        var draw = new PoolDraw(Guid.NewGuid().ToString("N"), winner, lottery.Uid,
            lottery.Uid is { } uid ? lottery.Viewers.GetValueOrDefault(uid) : null, stoppedByHost, now, candidates.MoveToImmutable());
        Save(lottery with { Draws = lottery.Draws.Add(draw), Phase = PoolLotteryPhase.Drawing, DrawUntil = now + DrawDuration }, draw);
        ResumeDraw();
        logger.LogInformation("奖池抽奖开奖：抽奖 {LotteryId}，{Grade} {Prize}，观众 {Uid} {Nickname}，主播点停 {Host}",
            lottery.Id, winner.Grade, winner.Name, draw.Uid, draw.Nickname, stoppedByHost);
    }

    private void ResumeDraw()
    {
        if (state.Lottery is not { Phase: PoolLotteryPhase.Drawing, DrawUntil: { } until } lottery) return;
        var remaining = until - time.GetUtcNow();
        if (remaining <= TimeSpan.Zero) { FinishDraw(lottery.Id, until); return; }
        drawTimer?.Dispose();
        drawTimer = time.CreateTimer(_ =>
        {
            lock (gate)
            {
                try { FinishDraw(lottery.Id, until); }
                catch (Exception error) { logger.LogError(error, "保存开奖结束状态失败"); }
            }
        }, null, remaining, Timeout.InfiniteTimeSpan);
    }

    private void FinishDraw(string id, DateTimeOffset until)
    {
        if (!disposed && state.Lottery is { Phase: PoolLotteryPhase.Drawing } lottery && lottery.Id == id && lottery.DrawUntil == until)
        {
            var finished = lottery with { Phase = PoolLotteryPhase.Idle, DrawUntil = null };
            if (state.CanControl) Save(finished);
            // A different process may own the room now; its next load uses the persisted draw deadline.
            else { state = state with { Lottery = finished }; Publish(); }
        }
    }

    private void Save(Lottery lottery, PoolDraw? draw = null)
    {
        store().Save(lottery, draw: draw);
        state = state with { Lottery = lottery };
        Publish(draw);
    }

    private void Publish(PoolDraw? draw = null)
    {
        foreach (var subscriber in subscribers) subscriber.Writer.TryWrite(new(state, draw));
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            drawTimer?.Dispose();
            foreach (var subscriber in subscribers) subscriber.Writer.TryComplete();
            subscribers.Clear();
        }
    }
}
