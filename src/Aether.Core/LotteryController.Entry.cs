using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

public sealed partial class LotteryController
{
    public const int MaxAwards = 5;

    public static string? ValidateAward(LotteryAward award) => string.IsNullOrWhiteSpace(award.Name) ? "请填写奖项名称。"
        : string.IsNullOrWhiteSpace(award.Prize) ? "请填写奖品。" : award.Capacity <= 0 ? "名额必须是正整数。"
        : !Enum.IsDefined(award.Grade) ? "请选择档次。" : null;

    public static string? ValidateKeyword(string? text) => string.IsNullOrWhiteSpace(text) ? "报名关键字不能为空" : null;

    private void RequireNew()
    {
        if (state.Lottery is EntryLottery { Phase: EntryLotteryPhase.Open or EntryLotteryPhase.Closed })
            throw new InvalidOperationException("先结束这一场才能新开。");
        if (state.Lottery is PoolLottery { IsDrawing: true })
            throw new InvalidOperationException("请等本次开奖结束。");
    }

    public void New(LotteryMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("请选择抽奖方式。");
        lock (gate)
        {
            RequireConnection();
            RequireNew();
            var now = time.GetUtcNow();
            Lottery next;
            if (mode == LotteryMode.Entry)
            {
                var previous = store().Load(state.RoomId!.Value, LotteryMode.Entry) as EntryLottery;
                next = new EntryLottery(Guid.NewGuid().ToString("N"), state.RoomId.Value,
                    previous?.Keyword ?? "抽奖", now, previous?.Awards ?? [new("一等奖", "", 1, Grade.Grand)], [], []);
            }
            else
            {
                var previous = store().Load(state.RoomId!.Value, LotteryMode.Pool) as PoolLottery;
                next = NewPoolLottery(previous?.Prizes ?? [new("", 1, Grade.Grand)], previous?.StopWord ?? "停")
                    with { Uid = previous?.Uid };
            }
            store().Save(next, endCurrentAt: now);
            state = state with { Lottery = next };
            drawTimer?.Dispose();
            Publish();
            logger.LogInformation("抽奖新开一场：直播间 {RoomId}，方式 {Mode}", state.RoomId, mode);
        }
    }

    public void StartEntry(IReadOnlyCollection<LotteryAward> awards, string keyword)
    {
        if (ValidateKeyword(keyword) is { } keywordError) throw new ArgumentException(keywordError);
        if (awards.Count is < 1 or > MaxAwards) throw new ArgumentException($"奖项须为 1 至 {MaxAwards} 个。");
        foreach (var award in awards)
            if (ValidateAward(award) is { } error) throw new ArgumentException(error);
        lock (gate)
        {
            RequireConnection();
            var lottery = RequireEntry(EntryLotteryPhase.Preparing);
            Save(lottery with { Keyword = keyword.Trim(), Awards = awards.ToImmutableArray(), Phase = EntryLotteryPhase.Open });
            logger.LogInformation("报名抽奖开始报名：直播间 {RoomId}，抽奖 {LotteryId}，关键字 {Keyword}", state.RoomId, lottery.Id, keyword.Trim());
        }
    }

    public void CloseEntries()
    {
        lock (gate)
        {
            var lottery = RequireEntry(EntryLotteryPhase.Open);
            Save(lottery with { Phase = EntryLotteryPhase.Closed });
            logger.LogInformation("报名抽奖截止：抽奖 {LotteryId}，共 {Count} 人", lottery.Id, lottery.Entries.Length);
        }
    }

    public void DrawEntry(int awardIndex)
    {
        lock (gate)
        {
            var lottery = RequireEntry(EntryLotteryPhase.Closed);
            if (lottery.IsDrawing) throw new InvalidOperationException("请等本次开奖结束。");
            if (awardIndex < 0 || awardIndex >= lottery.Awards.Length) throw new ArgumentOutOfRangeException(nameof(awardIndex));
            var award = lottery.Awards[awardIndex];
            if (lottery.Draws.Count(draw => draw.AwardIndex == awardIndex) >= award.Capacity)
                throw new InvalidOperationException("这个奖项的名额已满。");
            var winners = lottery.Draws.Select(draw => draw.Winner.Uid).ToHashSet();
            var available = lottery.Entries.Where(entry => !winners.Contains(entry.Uid)).ToArray();
            if (available.Length == 0) throw new InvalidOperationException("没有还没中奖的报名观众可抽。");
            var pick = (int)random.NextInt64(available.Length);
            // Display cells repeat when fewer than ten viewers remain; they do not affect the uniform draw.
            var candidates = Enumerable.Range(0, 10).Select(index => available[(pick + index) % available.Length].Nickname).ToImmutableArray();
            var now = time.GetUtcNow();
            var draw = new EntryDraw(Guid.NewGuid().ToString("N"), awardIndex, award, available[pick], now, candidates);
            Save(lottery with { Draws = lottery.Draws.Add(draw), DrawUntil = now + DrawDuration }, draw);
            ResumeDraw();
            logger.LogInformation("报名抽奖开奖：抽奖 {LotteryId}，奖项 {Award}，奖品 {Prize}，观众 {Uid} {Nickname}",
                lottery.Id, award.Name, award.Prize, draw.Winner.Uid, draw.Winner.Nickname);
        }
    }

    public void EndEntry()
    {
        lock (gate)
        {
            var lottery = RequireEntry(EntryLotteryPhase.Closed);
            if (lottery.IsDrawing) throw new InvalidOperationException("请等本次开奖结束。");
            Save(lottery with { Phase = EntryLotteryPhase.Ended, EndedAt = time.GetUtcNow() });
            logger.LogInformation("报名抽奖结束：抽奖 {LotteryId}，共 {Count} 人中奖", lottery.Id, lottery.Draws.Length);
        }
    }

    public void SetEntryHidden(bool hidden)
    {
        lock (gate) Save(RequireEntry(EntryLotteryPhase.Ended) with { Hidden = hidden });
    }

    private EntryLottery RequireEntry(EntryLotteryPhase phase)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!state.CanControl) throw new InvalidOperationException("请先连接直播间。");
        if (state.Lottery is not EntryLottery lottery || lottery.Phase != phase) throw new InvalidOperationException("当前阶段不能执行此操作。");
        return lottery;
    }

    private void ReceiveEntry(Danmaku danmaku)
    {
        if (state.Lottery is not EntryLottery { Phase: EntryLotteryPhase.Open } lottery || danmaku.Uid <= 0
            || danmaku.Content.Trim() != lottery.Keyword || lottery.Entries.Any(entry => entry.Uid == danmaku.Uid)) return;
        var entry = new LotteryEntry(danmaku.Uid, danmaku.Nickname, time.GetUtcNow());
        store().AddEntry(lottery.Id, entry);
        state = state with { Lottery = lottery with { Entries = lottery.Entries.Add(entry) } };
        Publish();
        logger.LogInformation("报名抽奖报名：抽奖 {LotteryId}，观众 {Uid} {Nickname}", lottery.Id, entry.Uid, entry.Nickname);
    }
}
