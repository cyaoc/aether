using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One room's active pool lottery, independent of the browser and the room transport lifetime.</summary>
public sealed class PoolLottery : IDisposable
{
    public static readonly TimeSpan RevealDuration = TimeSpan.FromSeconds(7);
    private readonly object gate = new();
    private readonly Lazy<LotteryStore> store;
    private readonly TimeProvider time;
    private readonly ILogger logger;
    private readonly Random random;
    private readonly List<Channel<PoolLotteryUpdate>> subscribers = [];
    private PoolLotteryState state = new(null, false, null);
    private ITimer? revealTimer;
    private bool disposed;

    internal PoolLottery(string dataDirectory, TimeProvider time, ILogger logger, Random random)
    {
        store = new(() => new LotteryStore(new Database(dataDirectory)), LazyThreadSafetyMode.PublicationOnly);
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
            revealTimer?.Dispose();
            state = new(roomId, true, store.Value.Load(roomId));
            ResumeReveal();
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
        if (prizes.Count is < 1 or > 10) throw new ArgumentException("奖品须为 1 至 10 件。");
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
            var lottery = state.Lottery ?? NewSession(prizes, stopWord);
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
            if (state.Lottery is { Phase: PoolLotteryPhase.Revealing }) throw new InvalidOperationException("请等本次开奖结束。");
            var next = NewSession(prizes, stopWord);
            store.Value.Save(next, endCurrentAt: time.GetUtcNow());
            revealTimer?.Dispose();
            state = state with { Lottery = next };
            Publish();
            logger.LogInformation("奖池抽奖新开一场：直播间 {RoomId}，抽奖 {LotteryId}", state.RoomId, next.Id);
        }
    }

    private PoolLotterySession NewSession(IReadOnlyCollection<PoolPrize> prizes, string stopWord) =>
        new(Guid.NewGuid().ToString("N"), state.RoomId!.Value, stopWord.Trim(), time.GetUtcNow(), null,
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
                store.Value.RememberViewer(lottery.Id, danmaku.Uid, danmaku.Nickname);
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
        // Ten midpoint samples represent the pool's weights without changing the actual draw odds.
        var candidates = ImmutableArray.CreateBuilder<PoolPrize>(10);
        for (var index = 0; index < 10; index++)
        {
            var position = (2L * index + 1) * total / 20;
            foreach (var prize in lottery.Prizes)
            {
                if (position < prize.Weight) { candidates.Add(prize); break; }
                position -= prize.Weight;
            }
        }
        var now = time.GetUtcNow();
        var draw = new PoolDraw(Guid.NewGuid().ToString("N"), winner, lottery.Uid,
            lottery.Uid is { } uid ? lottery.Viewers.GetValueOrDefault(uid) : null, stoppedByHost, now, candidates.MoveToImmutable());
        Save(lottery with { Draws = lottery.Draws.Add(draw), Phase = PoolLotteryPhase.Revealing, RevealUntil = now + RevealDuration }, draw);
        ResumeReveal();
        logger.LogInformation("奖池抽奖开奖：抽奖 {LotteryId}，{Grade} {Prize}，观众 {Uid} {Nickname}，主播点停 {Host}",
            lottery.Id, winner.Grade, winner.Name, draw.Uid, draw.Nickname, stoppedByHost);
    }

    private void ResumeReveal()
    {
        if (state.Lottery is not { Phase: PoolLotteryPhase.Revealing, RevealUntil: { } until } lottery) return;
        var remaining = until - time.GetUtcNow();
        if (remaining <= TimeSpan.Zero) { FinishReveal(lottery.Id, until); return; }
        revealTimer?.Dispose();
        revealTimer = time.CreateTimer(_ =>
        {
            lock (gate)
            {
                try { FinishReveal(lottery.Id, until); }
                catch (Exception error) { logger.LogError(error, "保存开奖结束状态失败"); }
            }
        }, null, remaining, Timeout.InfiniteTimeSpan);
    }

    private void FinishReveal(string id, DateTimeOffset until)
    {
        if (!disposed && state.Lottery is { Phase: PoolLotteryPhase.Revealing } lottery && lottery.Id == id && lottery.RevealUntil == until)
        {
            var finished = lottery with { Phase = PoolLotteryPhase.Idle, RevealUntil = null };
            if (state.CanControl) Save(finished);
            // A different process may own the room now; its next load uses the persisted reveal deadline.
            else { state = state with { Lottery = finished }; Publish(); }
        }
    }

    private void Save(PoolLotterySession lottery, PoolDraw? draw = null)
    {
        store.Value.Save(lottery, draw: draw);
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
            revealTimer?.Dispose();
            foreach (var subscriber in subscribers) subscriber.Writer.TryComplete();
            subscribers.Clear();
        }
    }
}
