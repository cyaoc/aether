using System.Collections.Immutable;
using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

// PROTOTYPE (#180), throwaway: lives only on prototype/lottery-panel-bindings.
// The 开奖记录 aside of 奖池抽奖 as the properties a LotteryViewModel (#177) would expose, bound by PoolDrawsAsidePrototype.axaml.
public sealed partial class PoolDrawsPrototype : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Latest), nameof(ShowLatest), nameof(Rows), nameof(DrawCount), nameof(IsEmpty))]
    private LotteryState state = new(null, false, null);

    private ImmutableArray<PoolDraw> Draws => State.PoolLottery?.Draws ?? [];
    private bool Drawing => State.PoolLottery?.Phase == PoolLotteryPhase.Drawing;

    public PoolDrawRow? Latest => Draws.Length > 0 ? new(Draws[^1], false) : null;
    public bool ShowLatest => Draws.Length > 0 && State.PoolLottery?.Phase != PoolLotteryPhase.Spinning;
    public string DrawCount => Draws.Length > 0 ? $"本场 {Draws.Length} 次" : "";
    public bool IsEmpty => Draws.Length == 0;

    // A state arrives for every danmaku while spinning; keep the list itself so the ItemsControl only rebuilds when draws change.
    private (ImmutableArray<PoolDraw> Draws, bool Drawing) rowsFor;
    private IReadOnlyList<PoolDrawRow> rows = [];
    public IReadOnlyList<PoolDrawRow> Rows
    {
        get
        {
            if (rowsFor.Draws == Draws && rowsFor.Drawing == Drawing && !rowsFor.Draws.IsDefault) return rows;
            rowsFor = (Draws, Drawing);
            // The draw on air stays highlighted until the 抽奖页 finishes it, as on canvas V1.
            return rows = [.. Draws.Reverse().Select((draw, index) => new PoolDrawRow(draw, Drawing && index == 0))];
        }
    }
}

/// <summary>One line per draw as on canvas V1: who, 档次, prize, time.</summary>
public sealed record PoolDrawRow(PoolDraw Draw, bool Fresh)
{
    public string Who => Viewer ?? "主播点停";
    public bool HostBadge => Draw.StoppedByHost && Draw.Uid is not null;
    public Grade Grade => Draw.Prize.Grade;
    public string GradeName => LotteryPanel.GradeNames[(int)Grade];
    public string Prize => Draw.Prize.Name;
    public string Time => $"{Draw.DrawnAt.ToLocalTime():HH:mm}";
    public string Head => (Draw.StoppedByHost ? Viewer is null ? "主播点停" : $"{Viewer} · 主播点停" : $"{Viewer} 喊停") + $" · {Time}";
    private string? Viewer => Draw.Uid is { } uid ? Draw.Nickname ?? $"UID {uid}" : null;
}
