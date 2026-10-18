using System.Collections.Immutable;

namespace Aether.Core;

public sealed partial class LotteryController
{
    private LotteryState? example;
    private ITimer? exampleTimer;
    private object? exampleRun;

    public void SelectTheme(LotteryTheme theme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentException("请选择直播画面主题。");
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            state = state with { Theme = theme };
            Broadcast();
        }
    }

    internal void PlayExample()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (example is not null) throw new InvalidOperationException("示例播放中，请稍后再试。");
            var now = time.GetUtcNow();
            var id = "example-" + Guid.NewGuid().ToString("N");
            ImmutableArray<LotteryAward> awards = [new("一等奖", "机械键盘", 1, Grade.Grand),
                new("二等奖", "周边抱枕", 2, Grade.Second), new("三等奖", "签名照", 3, Grade.Third)];
            ImmutableArray<LotteryEntry> entries = [new(1, "柚子不酸", now), new(2, "星河入梦", now), new(3, "一只小鹿", now)];
            var entry = new EntryLottery(id, 0, "抽奖", now, awards, entries, [], EntryLotteryPhase.Open);
            var entryDraw = new EntryDraw(id + "-entry", 0, awards[0], entries[0], now.AddSeconds(4),
                ["柚子不酸", "星河入梦", "一只小鹿"]);
            ImmutableArray<PoolPrize> prizes = [new("机械键盘", 1, Grade.Grand), new("周边抱枕", 3, Grade.Second), new("签名照", 6, Grade.Third)];
            var pool = new PoolLottery(id + "-pool", 0, "停", now, prizes, [], ImmutableDictionary<long, string>.Empty, PoolLotteryPhase.Spinning);
            var poolDraw = new PoolDraw(id + "-draw", prizes[1], 2, "星河入梦", false, now.AddSeconds(14), prizes);
            (int At, LotteryState? State)[] frames =
            [
                (0, new(null, false, null, entry)),
                (3, new(null, false, null, entry with { Phase = EntryLotteryPhase.Closed })),
                (4, new(null, false, null, entry with { Phase = EntryLotteryPhase.Closed, Draws = [entryDraw], DrawUntil = now.AddSeconds(11) })),
                (11, new(null, false, null, entry with { Phase = EntryLotteryPhase.Ended, Draws = [entryDraw], EndedAt = now.AddSeconds(11) })),
                (13, new(null, false, pool)),
                (14, new(null, false, pool with { Phase = PoolLotteryPhase.Drawing, Draws = [poolDraw], DrawUntil = now.AddSeconds(21) })),
                (21, null)
            ];
            var run = exampleRun = new object();
            var index = 0;
            void Advance()
            {
                lock (gate)
                {
                    if (disposed || exampleRun != run) return;
                    example = frames[index].State;
                    state = state with { IsExamplePlaying = example is not null };
                    Broadcast();
                    exampleTimer?.Dispose();
                    if (++index < frames.Length)
                        exampleTimer = time.CreateTimer(_ => Advance(), null,
                            TimeSpan.FromTicks(Math.Max(0, (now.AddSeconds(frames[index].At) - time.GetUtcNow()).Ticks)), Timeout.InfiniteTimeSpan);
                    else exampleRun = null;
                }
            }
            Advance();
        }
    }

    internal void StopExample()
    {
        lock (gate)
        {
            if (example is null) return;
            ClearExample();
            Broadcast();
        }
    }

    private void ClearExample()
    {
        exampleTimer?.Dispose();
        exampleTimer = null;
        exampleRun = null;
        example = null;
        state = state with { IsExamplePlaying = false };
    }
}
