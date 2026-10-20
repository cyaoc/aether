using System.ComponentModel;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Aether.Gui;

public partial class LotteryPanel : UserControl
{
    private MainWindowViewModel? viewModel;
    private PoolLottery? displayedHistory;

    public LotteryPanel()
    {
        InitializeComponent();
        ViewTabs.SelectionChanged += (_, _) =>
        {
            Refresh();
            if (ViewTabs.SelectedIndex == 1 && viewModel is not null) _ = viewModel.Lottery.RefreshLotteryHistoryAsync();
        };
        HistoryContent.ReturnRequested += () => ViewTabs.SelectedIndex = 0;
        AddButton.Click += (_, _) => { if (AddButton.IsEnabled) viewModel?.Lottery.AddPrize(); };
        StartButton.Click += async (_, _) => { if (StartButton.IsEnabled) await ChangeAsync(lottery => lottery.StartPoolAsync()); };
        NewButton.Click += async (_, _) =>
        {
            if (!NewButton.IsEnabled || TopLevel.GetTopLevel(this) is not Window owner) return;
            var mode = await new NewLotteryWindow().ShowDialog<LotteryMode?>(owner);
            if (mode is { } selected)
            {
                await ChangeAsync(lottery => lottery.NewLotteryAsync(selected));
                ViewTabs.SelectedIndex = 0;
            }
        };
        StopButton.Click += async (_, _) => { if (StopButton.IsEnabled) await ChangeAsync(lottery => lottery.StopPoolAsync()); };
        CancelButton.Click += async (_, _) => { if (CancelButton.IsEnabled) await ChangeAsync(lottery => lottery.CancelPoolAsync()); };
        Refresh();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.Lottery.PropertyChanged -= StateChanged;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.Lottery.PropertyChanged += StateChanged;
        Refresh();
        if (viewModel is not null) _ = viewModel.Lottery.RefreshLotteryHistoryAsync();
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LotteryViewModel.LotteryState) or nameof(LotteryViewModel.LotteryHistory)) Refresh();
    }

    private void Refresh()
    {
        if (PrizeRows is null) return;
        var state = viewModel?.Lottery.LotteryState;
        var entry = state?.Lottery as EntryLottery;
        var history = ViewTabs.SelectedIndex == 1;
        HistoryContent.IsVisible = history;
        EntryContent.IsVisible = !history && entry is not null;
        PoolContent.IsVisible = !history && entry is null;
        ModeChip.IsVisible = PhaseChip.IsVisible = !history;
        HistoryTabText.Text = $"历史 {viewModel?.Lottery.LotteryHistory.Count ?? 0}";
        ModeText.Text = entry is not null ? "报名抽奖" : "奖池抽奖";
        NewHint.Text = entry is { Phase: EntryLotteryPhase.Open or EntryLotteryPhase.Closed } ? "先结束这一场才能新开" : "";
        if (entry is not null)
        {
            PhaseText.Text = LotteryViewModel.EntryPhaseText(entry);
            PhaseChip.Classes.Set("spinning", entry.Phase == EntryLotteryPhase.Open);
            PhaseChip.Classes.Set("drawing", entry.Phase == EntryLotteryPhase.Closed);
            PhaseChip.Classes.Set("ended", entry.Phase == EntryLotteryPhase.Ended);
            return;
        }
        var lottery = state?.Lottery as PoolLottery;
        var phase = lottery?.Phase ?? PoolLotteryPhase.Idle;
        var spinning = phase == PoolLotteryPhase.Spinning;
        var drawing = lottery?.IsDrawing == true;
        PhaseText.Text = viewModel?.Lottery.LotteryPhaseText is { Length: > 0 } text ? text : "待机";
        PhaseChip.Classes.Set("spinning", spinning);
        PhaseChip.Classes.Set("drawing", drawing);
        PhaseChip.Classes.Set("ended", false);

        if (displayedHistory?.Id == lottery?.Id && displayedHistory?.Draws == lottery?.Draws && displayedHistory?.Phase == phase
            && displayedHistory?.IsDrawing == drawing) return;
        displayedHistory = lottery;
        var draws = lottery?.Draws ?? [];
        LatestCard.IsVisible = draws.Length > 0 && !spinning;
        if (draws.Length > 0)
        {
            var last = draws[^1];
            var who = Viewer(last);
            LatestHead.Text = (last.StoppedByHost ? who is null ? "主播点停" : $"{who} · 主播点停" : $"{who} 喊停")
                + $" · {last.DrawnAt.ToLocalTime():HH:mm}";
            LatestGrade.Grade = last.Prize.Grade;
            LatestPrize.Text = last.Prize.Name;
        }
        DrawCount.Text = draws.Length > 0 ? $"本场 {draws.Length} 次" : "";
        EmptyHistory.IsVisible = draws.Length == 0;
        HistoryRows.Children.Clear();
        foreach (var draw in draws.Reverse()) HistoryRows.Children.Add(HistoryRow(draw));
        // The draw on air stays highlighted until the 抽奖页 finishes it, as on canvas V1.
        if (drawing && HistoryRows.Children.FirstOrDefault() is { } fresh) fresh.Classes.Add("fresh");
    }

    private static string? Viewer(PoolDraw draw) => draw.Uid is { } uid ? draw.Nickname ?? $"UID {uid}" : null;

    /// <summary>One line per draw as on canvas V1: who, 档次, prize, time.</summary>
    private static Border HistoryRow(PoolDraw draw)
    {
        var who = new TextBlock { Text = Viewer(draw) ?? "主播点停", FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold, MaxWidth = 84, VerticalAlignment = VerticalAlignment.Center };
        who.Classes.Add("singleLine");
        var grade = new GradeBadge { Grade = draw.Prize.Grade, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var prize = new TextBlock { Text = draw.Prize.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        prize.Classes.Add("singleLine");
        var time = new TextBlock { Text = $"{draw.DrawnAt.ToLocalTime():HH:mm}", VerticalAlignment = VerticalAlignment.Center };
        time.Classes.AddRange(["caption", "muted", "numeric"]);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { who } };
        if (draw.StoppedByHost && draw.Uid is not null)
        {
            var host = new Border { Child = new TextBlock { Text = "点停" }, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            host.Classes.AddRange(["badge", "neutralBadge"]);
            ToolTip.SetTip(host, "主播点停");
            line.Children.Add(host);
        }
        line.Children.Add(grade);
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6, Children = { line, prize, time } };
        Grid.SetColumn(prize, 1);
        Grid.SetColumn(time, 2);
        return new Border { Child = row };
    }

    private async Task ChangeAsync(Func<LotteryViewModel, Task> change)
    {
        if (viewModel is null || viewModel.Lottery.Busy) return;
        viewModel.Lottery.PoolLotteryError = null;
        try { await change(viewModel.Lottery); }
        catch (Exception error)
        {
            if (EntryContent.IsVisible) EntryContent.ShowError(error.Message);
            else viewModel.Lottery.PoolLotteryError = error.Message;
        }
    }
}
