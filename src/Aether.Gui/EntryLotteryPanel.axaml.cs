using System.ComponentModel;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Aether.Gui;

public partial class EntryLotteryPanel : UserControl
{
    private MainWindowViewModel? viewModel;
    private EntryLottery? displayed;

    public EntryLotteryPanel()
    {
        InitializeComponent();
        AddAwardButton.Click += (_, _) => { if (AddAwardButton.IsEnabled) viewModel?.Lottery.AddAward(); };
        StartEntryButton.Click += async (_, _) => { if (StartEntryButton.IsEnabled && viewModel is not null) await viewModel.Lottery.StartEntryAsync(); };
        CloseEntriesButton.Click += async (_, _) => { if (CloseEntriesButton.IsEnabled && viewModel is not null) await viewModel.Lottery.CloseEntriesAsync(); };
        EndEntryButton.Click += (_, _) => { if (EndEntryButton.IsEnabled) viewModel?.Lottery.RequestEndEntry(); };
        CancelEndButton.Click += (_, _) => { if (CancelEndButton.IsEnabled) viewModel?.Lottery.CancelEndEntry(); };
        ConfirmEndButton.Click += async (_, _) => { if (ConfirmEndButton.IsEnabled && viewModel is not null) await viewModel.Lottery.ConfirmEndEntryAsync(); };
        VisibilityButton.Click += async (_, _) => { if (VisibilityButton.IsEnabled && viewModel is not null) await viewModel.Lottery.ToggleEntryHiddenAsync(); };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.Lottery.PropertyChanged -= StateChanged;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.Lottery.PropertyChanged += StateChanged;
        Refresh();
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LotteryViewModel.LotteryState)) Refresh();
    }

    private void Refresh()
    {
        var state = viewModel?.Lottery.LotteryState;
        if (state?.Lottery is not EntryLottery lottery) { displayed = null; return; }
        var drawing = lottery.IsDrawing;
        VisibilityButton.Content = lottery.Hidden ? "重新显示" : "收起画面";
        StatusText.Text = lottery.Phase switch
        {
            EntryLotteryPhase.Preparing => state.Connected ? "填好关键字和奖项，点“开始报名”" : "先连直播间，才能开始报名",
            EntryLotteryPhase.Open => $"已有 {lottery.Entries.Length} 人报名，直播画面上显示报名方式和名单",
            EntryLotteryPhase.Closed when drawing => "直播画面正在播开奖特效",
            EntryLotteryPhase.Closed when lottery.Entries.Length == 0 => "没人报名，没人可抽",
            EntryLotteryPhase.Closed when lottery.Remaining <= 0 => "名额都抽完了，可以结束这场抽奖",
            EntryLotteryPhase.Closed when lottery.Entries.Length <= lottery.Draws.Length => "没人可抽了：报名的人都中过奖",
            EntryLotteryPhase.Closed => $"{lottery.Entries.Length} 人报名，已抽 {lottery.Draws.Length} / {lottery.Remaining + lottery.Draws.Length}。点奖项右边的“抽一个”",
            _ => lottery.Hidden ? "画面已收起，可以重新显示完整中奖名单" : "抽奖页正在显示完整中奖名单，点“收起画面”让它消失",
        };
        if (displayed == lottery) return;
        var previous = displayed;
        displayed = lottery;
        WinnerCount.Text = lottery.Draws.Length > 0 ? $"{lottery.Draws.Length} 人" : "";
        EntryCount.Text = lottery.Entries.Length > 0 ? $"{lottery.Entries.Length} 人" : "";
        EmptyWinners.IsVisible = lottery.Draws.Length == 0;
        EmptyEntries.IsVisible = lottery.Entries.Length == 0;
        var fresh = drawing ? lottery.Draws[^1] : null;
        WinnerGroups.Children.Clear();
        for (var index = 0; index < lottery.Awards.Length; index++)
        {
            var winners = lottery.Draws.Where(draw => draw.AwardIndex == index).ToArray();
            if (winners.Length == 0) continue;
            var award = lottery.Awards[index];
            WinnerGroups.Children.Add(WinnerGroup(award.Name, award.Prize, $"{winners.Length} / {award.Capacity}", winners.Select(winner =>
            {
                var chip = WinnerChip(winner.Winner.Nickname);
                chip.Classes.Set("fresh", winner == fresh);
                return (Control)chip;
            })));
        }
        // Entries only grow while 报名中, so a busy room adds rows instead of redrawing the list; a draw changes the badges and rebuilds it.
        if (previous?.Id != lottery.Id || previous.Draws != lottery.Draws || previous.DrawUntil != lottery.DrawUntil
            || EntryRows.Children.Count > lottery.Entries.Length) EntryRows.Children.Clear();
        var won = lottery.Draws.ToDictionary(draw => draw.Winner.Uid);
        for (var index = EntryRows.Children.Count; index < lottery.Entries.Length; index++)
            EntryRows.Children.Insert(0, EntryRow(lottery.Entries[index], won.GetValueOrDefault(lottery.Entries[index].Uid), fresh));
    }

    /// <summary>One 中奖名单 group as on canvases O1 and O4: award, prize and progress over the people.</summary>
    internal static StackPanel WinnerGroup(string award, string prize, string progress, IEnumerable<Control> people)
    {
        // Centred, as the 报名 rows are: CJK and digit fonts have different line boxes.
        var name = new TextBlock { Text = award, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var detail = new TextBlock { Text = prize, VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock { Text = progress, VerticalAlignment = VerticalAlignment.Center };
        name.Classes.AddRange(["caption", "awardName", "singleLine"]);
        detail.Classes.AddRange(["caption", "muted", "singleLine"]);
        count.Classes.AddRange(["caption", "muted", "numeric"]);
        Grid.SetColumn(detail, 1);
        Grid.SetColumn(count, 2);
        var chips = new WrapPanel();
        chips.Children.AddRange(people);
        return new StackPanel
        {
            Spacing = 4,
            Children = { new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6, Children = { name, detail, count } }, chips },
        };
    }

    internal static Border WinnerChip(string nickname)
    {
        var text = new TextBlock { Text = nickname, MaxWidth = 210, VerticalAlignment = VerticalAlignment.Center };
        text.Classes.Add("singleLine");
        var chip = new Border { Child = text };
        chip.Classes.Add("winner");
        return chip;
    }

    /// <summary>One 报名 row as on canvas O1: name, the award it won, time.</summary>
    private static Border EntryRow(LotteryEntry entry, EntryDraw? draw, EntryDraw? fresh)
    {
        var name = new TextBlock { Text = entry.Nickname, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.Classes.Add("singleLine");
        var at = new TextBlock { Text = $"{entry.EnteredAt.ToLocalTime():HH:mm:ss}", VerticalAlignment = VerticalAlignment.Center };
        at.Classes.AddRange(["caption", "muted", "numeric"]);
        Grid.SetColumn(at, 2);
        var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 6, Children = { name, at } };
        if (draw is not null)
        {
            var text = new TextBlock { Text = $"中 {draw.Award.Name}", MaxWidth = 90 };
            text.Classes.Add("singleLine");
            var badge = new Border { Child = text, Height = 20, Padding = new(6, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.Classes.AddRange(["badge", "won"]);
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
        }
        var border = new Border { Child = row };
        border.Classes.Set("fresh", draw is not null && draw == fresh);
        return border;
    }

}
