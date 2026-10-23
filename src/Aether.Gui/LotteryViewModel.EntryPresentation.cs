using System.Collections.ObjectModel;
using Aether.Core;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private EntryLottery? entryRowsSource;
    private EntryLottery? entryWinnersSource;
    private IReadOnlyList<WinnerGroupRow> entryWinnerGroups = [];
    public ObservableCollection<LotteryEntryRow> LotteryEntries { get; } = [];

    private void UpdateEntryRows(LotteryState state)
    {
        var previous = entryRowsSource;
        entryRowsSource = state.Lottery as EntryLottery;
        if (previous == entryRowsSource) return;
        if (entryRowsSource is not { } lottery) { LotteryEntries.Clear(); return; }
        if (previous?.Id != lottery.Id || previous.Draws != lottery.Draws || previous.DrawUntil != lottery.DrawUntil
            || LotteryEntries.Count > lottery.Entries.Length) LotteryEntries.Clear();
        var fresh = lottery.IsDrawing ? lottery.Draws[^1] : null;
        var won = lottery.Draws.ToDictionary(draw => draw.Winner.Uid);
        for (var index = LotteryEntries.Count; index < lottery.Entries.Length; index++)
        {
            var entry = lottery.Entries[index];
            var draw = won.GetValueOrDefault(entry.Uid);
            LotteryEntries.Insert(0, new(entry.Nickname, draw is null ? null : $"中 {draw.Award.Name}",
                $"{entry.EnteredAt.ToLocalTime():HH:mm:ss}", draw is not null && draw == fresh));
        }
    }

    public IReadOnlyList<WinnerGroupRow> EntryWinnerGroups
    {
        get
        {
            var lottery = LotteryState.Lottery as EntryLottery;
            if (ReferenceEquals(entryWinnersSource, lottery)) return entryWinnerGroups;
            entryWinnersSource = lottery;
            if (lottery is null) return entryWinnerGroups = [];
            var fresh = lottery.IsDrawing ? lottery.Draws[^1] : null;
            return entryWinnerGroups = lottery.Awards.Select((award, index) =>
            {
                var winners = lottery.Draws.Where(draw => draw.AwardIndex == index).ToArray();
                return new WinnerGroupRow(award.Name, award.Prize, $"{winners.Length} / {award.Capacity}",
                    winners.Select(draw => new WinnerRow(draw.Winner.Nickname, Fresh: draw == fresh)).ToArray());
            }).Where(group => !group.IsEmpty).ToArray();
        }
    }

    public string EntryWinnerCountText => LotteryState.Lottery is EntryLottery { Draws.Length: > 0 } lottery ? $"{lottery.Draws.Length} 人" : "";
    public string LotteryEntryCountText => LotteryState.Lottery is EntryLottery { Entries.Length: > 0 } lottery ? $"{lottery.Entries.Length} 人" : "";
    public bool IsEntryWinnersEmpty => LotteryState.Lottery is not EntryLottery { Draws.Length: > 0 };
    public bool IsLotteryEntriesEmpty => LotteryState.Lottery is not EntryLottery { Entries.Length: > 0 };
    public string EntryVisibilityText => LotteryState.Lottery is EntryLottery { Hidden: true } ? "重新显示" : "收起画面";
    public string EntryStatusText => LotteryState.Lottery is not EntryLottery lottery ? "" : lottery.Phase switch
    {
        EntryLotteryPhase.Preparing => entryPrizeSetStatus ?? (LotteryState.Connected ? "填好关键字和奖项，点“开始报名”" : "先连直播间，才能开始报名"),
        EntryLotteryPhase.Open => $"已有 {lottery.Entries.Length} 人报名，直播画面上显示报名方式和名单",
        EntryLotteryPhase.Closed when lottery.IsDrawing => "直播画面正在播开奖特效",
        EntryLotteryPhase.Closed when lottery.Entries.Length == 0 => "没人报名，没人可抽",
        EntryLotteryPhase.Closed when lottery.Remaining <= 0 => "名额都抽完了，可以结束这场抽奖",
        EntryLotteryPhase.Closed when !lottery.HasEligibleEntries => "没人可抽了：报名的人都中过奖",
        EntryLotteryPhase.Closed => $"{lottery.Entries.Length} 人报名，已抽 {lottery.Draws.Length} / {lottery.Remaining + lottery.Draws.Length}。点奖项右边的“抽一个”",
        _ => lottery.Hidden ? "画面已收起，可以重新显示完整中奖名单" : "抽奖页正在显示完整中奖名单，点“收起画面”让它消失",
    };
}
