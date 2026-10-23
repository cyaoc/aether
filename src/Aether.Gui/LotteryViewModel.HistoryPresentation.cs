using Aether.Core;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private IReadOnlyList<LotteryHistory>? historyRowsSource;
    private IReadOnlyList<LotteryHistoryRow> historyRows = [];
    private Lottery? historyWinnersSource;
    private IReadOnlyList<WinnerGroupRow> historyWinnerGroups = [];

    public IReadOnlyList<LotteryHistoryRow> LotteryHistoryRows
    {
        get
        {
            if (ReferenceEquals(historyRowsSource, LotteryHistory)) return historyRows;
            historyRowsSource = LotteryHistory;
            return historyRows = LotteryHistory.Select(item =>
            {
                var date = FormatLotteryDate(item.StartedAt, includeTodayDate: true);
                var status = item.EndedAt is null ? "进行中" : "已结束";
                var mode = ModeText(item.Mode);
                var count = item.Mode == LotteryMode.Entry ? $"报名 {item.EntryCount} · 中奖 {item.DrawCount}" : $"抽了 {item.DrawCount} 次";
                var summary = item.Mode == LotteryMode.Entry
                    ? string.Join(" · ", item.Awards.Select(award => $"{award.Name} {award.Prize} ×{award.Capacity}"))
                    : string.Join(" · ", item.Prizes.Select(prize => $"{LotteryController.GradeNames[(int)prize.Grade]} {prize.Name}"));
                return new LotteryHistoryRow(item.Id, date, item.EndedAt is null, status, mode, count, summary,
                    $"{date} · {status} · {mode} · {count} · {summary}");
            }).ToArray();
        }
    }

    public IReadOnlyList<WinnerGroupRow> HistoryWinnerGroups
    {
        get
        {
            if (ReferenceEquals(historyWinnersSource, SelectedLotteryHistory)) return historyWinnerGroups;
            historyWinnersSource = SelectedLotteryHistory;
            if (SelectedLotteryHistory is EntryLottery entry)
                return historyWinnerGroups = entry.Awards.Select((award, index) =>
                {
                    var winners = entry.Draws.Where(draw => draw.AwardIndex == index).ToArray();
                    return new WinnerGroupRow(award.Name, award.Prize, $"已抽 {winners.Length} / {award.Capacity}",
                        winners.Select(draw => new WinnerRow(draw.Winner.Nickname, $"UID {draw.Winner.Uid}")).ToArray());
                }).ToArray();
            if (SelectedLotteryHistory is not PoolLottery pool) return historyWinnerGroups = [];
            // A pool can change between draws; retain prizes already won even if later removed.
            var prizes = pool.Prizes.Select(prize => (prize.Name, prize.Grade))
                .Concat(pool.Draws.Select(draw => (draw.Prize.Name, draw.Prize.Grade))).Distinct();
            return historyWinnerGroups = prizes.Select(prize =>
            {
                var draws = pool.Draws.Where(draw => draw.Prize.Name == prize.Name && draw.Prize.Grade == prize.Grade).ToArray();
                var winners = draws.GroupBy(draw => draw.Uid).Select(group =>
                {
                    var who = DrawViewer(group.LastOrDefault(draw => !string.IsNullOrEmpty(draw.Nickname)) ?? group.Last())
                        ?? HostStoppedText;
                    return new WinnerRow(group.Count() > 1 ? $"{who} ×{group.Count()}" : who,
                        group.Key is { } viewer ? $"UID {viewer}" : "未指定观众");
                }).ToArray();
                return new WinnerGroupRow(LotteryController.GradeNames[(int)prize.Grade], prize.Name, $"{draws.Length} 次", winners);
            }).ToArray();
        }
    }

    public string LotteryHistoryTabText => $"历史 {LotteryHistory.Count}";
    public string HistoryRoomText => LotteryHistory.FirstOrDefault() is { } first ? $"直播间 {first.RoomId}" : "";
    public bool ShowHistoryRoom => LotteryState.RoomId is null && LotteryHistory.Count > 0;
    public bool HasLotteryHistory => LotteryHistory.Count > 0;
    public bool HasLotteryHistoryError => !string.IsNullOrEmpty(LotteryHistoryError);
    public bool IsLotteryHistoryEmpty => !HasLotteryHistory && !LotteryHistoryLoading && !HasLotteryHistoryError;
    public bool HasLotteryHistoryDetails => SelectedLotteryHistory is not null;
    public bool ShowReturnToCurrent => SelectedLotteryHistory is { EndedAt: null };
    public bool CanReturnToCurrent => SelectedLotteryHistory is not null && SelectedLotteryHistory.Id == LotteryState.Lottery?.Id;
    public bool ShowReturnToCurrentHint => ShowReturnToCurrent && !CanReturnToCurrent;
    public string HistoryDetailDate => SelectedLotteryHistory is { } lottery ? FormatLotteryDate(lottery.StartedAt, includeTodayDate: true) : "";
    public string HistoryDetailSummary => SelectedLotteryHistory switch
    {
        EntryLottery entry => $"{ModeText(LotteryMode.Entry)} · 报名 {entry.EntryCount} 人",
        PoolLottery pool => $"{ModeText(LotteryMode.Pool)} · 抽了 {pool.Draws.Length} 次",
        _ => "",
    };
    public string HistoryConditionTitle => SelectedLotteryHistory is EntryLottery ? "报名条件" : "谁来喊停";
    public string HistoryConditionText => SelectedLotteryHistory switch
    {
        EntryLottery entry => $"发弹幕「{entry.Keyword}」",
        PoolLottery pool => $"口令「{pool.StopWord}」，每次点名一位观众喊停，或主播点停",
        _ => "",
    };

    private string FormatLotteryDate(DateTimeOffset timestamp, bool includeTodayDate = false)
    {
        var at = timestamp.ToLocalTime();
        var today = clock.GetLocalNow();
        return !includeTodayDate && at.Date == today.Date ? $"{at:HH:mm}"
            : at.Year == today.Year ? $"{at:M月d日 HH:mm}" : $"{at:yyyy年M月d日 HH:mm}";
    }
}
