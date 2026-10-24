using Aether.Core;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private const string HostStoppedText = "主播点停";
    private PoolLottery? poolDrawsSource;
    private bool poolDrawsCanControl;
    private DateTime poolDrawsDate;
    private IReadOnlyList<PoolDrawRow> poolDraws = [];

    public IReadOnlyList<PoolDrawRow> PoolDraws
    {
        get
        {
            var lottery = LotteryState.Lottery as PoolLottery;
            if (poolDrawsSource?.Id == lottery?.Id && poolDrawsSource?.Draws == lottery?.Draws
                && poolDrawsSource?.Phase == lottery?.Phase && poolDrawsSource?.IsDrawing == lottery?.IsDrawing
                && poolDrawsCanControl == CanControlOperation && poolDrawsDate == clock.GetLocalNow().Date)
                return poolDraws;
            poolDrawsSource = lottery;
            poolDrawsCanControl = CanControlOperation;
            poolDrawsDate = clock.GetLocalNow().Date;
            return poolDraws = lottery is null ? [] : lottery.Draws.Reverse().Select((draw, index) =>
                new PoolDrawRow(DrawViewer(draw) ?? HostStoppedText, draw.StoppedByHost && draw.Uid is not null,
                    draw.Prize.Grade, draw.Prize.Name, DrawTime(draw.DrawnAt) + (draw.HiddenFromStream ? " · 不挂出" : ""),
                    lottery.IsDrawing && index == 0, draw.HiddenFromStream, CanControlOperation,
                    new AsyncRelayCommand(() => ToggleDrawVisibilityAsync(draw)))).ToArray();
        }
    }

    private PoolDraw? LatestPoolDraw => LotteryState.Lottery is PoolLottery { Draws.Length: > 0 } lottery ? lottery.Draws[^1] : null;
    public bool ShowLatestPoolDraw => LotteryState.Lottery is PoolLottery { Phase: not PoolLotteryPhase.Spinning, Draws.Length: > 0 };
    public string LatestPoolDrawHead
    {
        get
        {
            if (LatestPoolDraw is not { } draw) return "";
            var who = DrawViewer(draw);
            return (draw.StoppedByHost ? who is null ? HostStoppedText : $"{who} · {HostStoppedText}" : $"{who} 喊停")
                + $" · {DrawTime(draw.DrawnAt)}";
        }
    }
    public Grade LatestPoolDrawGrade => LatestPoolDraw?.Prize.Grade ?? Grade.Grand;
    public string LatestPoolDrawPrize => LatestPoolDraw?.Prize.Name ?? "";
    public string PoolDrawCountText => PoolDraws.Count > 0 ? $"本场 {PoolDraws.Count} 次"
        + (PoolDraws.Count(row => row.HiddenFromStream) is > 0 and var hidden ? $" · {hidden} 条不挂出" : "") : "";
    public bool IsPoolDrawsEmpty => PoolDraws.Count == 0;
    public string LotteryModeText => ModeText(CurrentMode);
    public string LotteryPanelPhaseText => LotteryState.Lottery is EntryLottery entry
        ? EntryPhaseText(entry) : LotteryPhaseText.Length > 0 ? LotteryPhaseText : "待机";
    public bool IsLotterySpinning => LotteryState.Lottery is EntryLottery { Phase: EntryLotteryPhase.Open }
        or PoolLottery { Phase: PoolLotteryPhase.Spinning };
    public bool IsLotteryDrawing => LotteryState.Lottery is EntryLottery { Phase: EntryLotteryPhase.Closed }
        or PoolLottery { IsDrawing: true };
    public bool IsLotteryEnded => LotteryState.Lottery is EntryLottery { Phase: EntryLotteryPhase.Ended };
    public string NewLotteryHint => LotteryState.Lottery is EntryLottery { Phase: EntryLotteryPhase.Open or EntryLotteryPhase.Closed }
        ? "先结束这一场才能新开" : "";
    public string LotteryExampleText => LotteryState.IsExamplePlaying ? "示例播放中…" : "试一下";

    private static string ModeText(LotteryMode mode) => mode == LotteryMode.Entry ? "报名抽奖" : "奖池抽奖";

    private static string? DrawViewer(PoolDraw draw) => !string.IsNullOrEmpty(draw.Nickname) ? draw.Nickname
        : draw.Uid is { } uid ? $"UID {uid}" : null;
}
