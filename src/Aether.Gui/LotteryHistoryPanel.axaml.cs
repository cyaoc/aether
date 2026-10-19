using System.ComponentModel;
using Aether.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Aether.Gui;

public partial class LotteryHistoryPanel : UserControl
{
    private MainWindowViewModel? viewModel;
    private IReadOnlyList<LotteryHistory>? displayed;
    private LotteryHistoryDetails? details;
    private bool loading;
    internal event Action? ReturnRequested;

    public LotteryHistoryPanel()
    {
        InitializeComponent();
        LotteryList.SelectionChanged += async (_, _) =>
        {
            if (!loading && LotteryList.SelectedItem is ListBoxItem { Tag: LotteryHistory item } && viewModel is not null)
                await viewModel.RefreshLotteryHistoryAsync(item.Id);
        };
        ReturnButton.Click += (_, _) => { if (ReturnButton.IsEnabled) ReturnRequested?.Invoke(); };
        RetryButton.Click += async (_, _) => { if (viewModel is not null) await viewModel.RefreshLotteryHistoryAsync(); };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.PropertyChanged -= Changed;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.PropertyChanged += Changed;
        Refresh();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.LotteryHistory) or nameof(MainWindowViewModel.SelectedLotteryHistory)
            or nameof(MainWindowViewModel.LotteryHistoryLoading) or nameof(MainWindowViewModel.LotteryHistoryError)
            or nameof(MainWindowViewModel.LotteryState)) Refresh();
    }

    private void Refresh()
    {
        if (viewModel is null || LotteryList is null) return;
        LoadingText.IsVisible = viewModel.LotteryHistoryLoading;
        ErrorText.Text = viewModel.LotteryHistoryError;
        ErrorText.IsVisible = RetryButton.IsVisible = !string.IsNullOrEmpty(ErrorText.Text);
        EmptyText.IsVisible = viewModel.LotteryHistory.Count == 0 && !LoadingText.IsVisible && !ErrorText.IsVisible;
        LotteryList.IsVisible = viewModel.LotteryHistory.Count > 0;
        loading = true;
        if (!ReferenceEquals(displayed, viewModel.LotteryHistory))
        {
            displayed = viewModel.LotteryHistory;
            LotteryList.Items.Clear();
            foreach (var item in displayed) LotteryList.Items.Add(Row(item));
            RoomText.Text = displayed.FirstOrDefault() is { } first ? $"直播间 {first.RoomId}" : "";
        }
        // Connected, the sidebar already names the room; offline, this says whose history is shown.
        RoomText.IsVisible = viewModel.LotteryState.RoomId is null && RoomText.Text != "";
        var selected = viewModel.SelectedLotteryHistory;
        if (!viewModel.LotteryHistoryLoading)
            LotteryList.SelectedItem = LotteryList.Items.OfType<ListBoxItem>().FirstOrDefault(row => row.Tag is LotteryHistory item && item.Id == selected?.Lottery.Id);
        loading = false;
        Details.IsVisible = selected is not null;
        ReturnButton.IsVisible = selected is { Lottery.EndedAt: null };
        ReturnButton.IsEnabled = selected is not null
            && selected.Lottery.Id == viewModel.LotteryState.Lottery?.Id;
        ReturnHint.IsVisible = ReturnButton.IsVisible && !ReturnButton.IsEnabled;
        if (ReferenceEquals(details, selected)) return;
        details = selected;
        WinnerGroups.Children.Clear();
        if (selected is null) return;
        var lottery = selected.Lottery;
        DetailDate.Text = Date(lottery);
        DetailSummary.Text = $"{Mode(lottery)} · {(lottery.Mode == LotteryMode.Entry ? $"报名 {lottery.EntryCount} 人" : $"抽了 {lottery.DrawCount} 次")}";
        ConditionTitle.Text = lottery.Mode == LotteryMode.Entry ? "报名条件" : "谁来喊停";
        ConditionText.Text = lottery.Mode == LotteryMode.Entry
            ? $"发弹幕「{lottery.Keyword}」" : $"口令「{lottery.StopWord}」，每次点名一位观众喊停，或主播点停";
        if (lottery.Mode == LotteryMode.Entry)
        {
            for (var index = 0; index < lottery.Awards.Length; index++)
            {
                var award = lottery.Awards[index];
                var winners = selected.EntryDraws.Where(draw => draw.AwardIndex == index).ToArray();
                AddWinners(award.Name, award.Prize, $"已抽 {winners.Length} / {award.Capacity}",
                    winners.Select(draw => (draw.Winner.Nickname, $"UID {draw.Winner.Uid}")));
            }
        }
        else
        {
            // A pool can change between draws; retain prizes already won even if later removed.
            var prizes = lottery.Prizes.Select(prize => (prize.Name, prize.Grade))
                .Concat(selected.PoolDraws.Select(draw => (draw.Prize.Name, draw.Prize.Grade))).Distinct();
            foreach (var (name, grade) in prizes)
            {
                var draws = selected.PoolDraws.Where(draw => draw.Prize.Name == name && draw.Prize.Grade == grade).ToArray();
                var winners = draws.GroupBy(draw => draw.Uid).Select(group =>
                {
                    var who = group.LastOrDefault(draw => !string.IsNullOrEmpty(draw.Nickname))?.Nickname
                        ?? (group.Key is { } uid ? $"UID {uid}" : "主播点停");
                    return (group.Count() > 1 ? $"{who} ×{group.Count()}" : who,
                        group.Key is { } viewer ? $"UID {viewer}" : "未指定观众");
                });
                AddWinners(LotteryPanel.GradeNames[(int)grade], name, $"{draws.Length} 次", winners);
            }
        }
    }

    // O4 drops the year; an older year keeps it so rows stay unambiguous.
    private static string Date(LotteryHistory item) => item.StartedAt.ToLocalTime() is var at && at.Year == DateTimeOffset.Now.Year
        ? $"{at:M月d日 HH:mm}" : $"{at:yyyy年M月d日 HH:mm}";
    private static string Mode(LotteryHistory item) => item.Mode == LotteryMode.Entry ? "报名抽奖" : "奖池抽奖";
    private static string Count(LotteryHistory item) => item.Mode == LotteryMode.Entry
        ? $"报名 {item.EntryCount} · 中奖 {item.DrawCount}" : $"抽了 {item.DrawCount} 次";

    private static ListBoxItem Row(LotteryHistory item)
    {
        var date = Text(Date(item), "numeric");
        date.FontWeight = FontWeight.SemiBold;
        var status = new Border { Child = Text(item.EndedAt is null ? "进行中" : "已结束") };
        status.Classes.AddRange(["badge", "historyTag"]);
        status.Classes.Set("active", item.EndedAt is null);
        var mode = new Border { Child = Text(Mode(item)) };
        mode.Classes.AddRange(["badge", "historyTag", "mode"]);
        var count = Text(Count(item), "caption", "muted", "numeric");
        Grid.SetColumn(status, 1);
        Grid.SetColumn(mode, 2);
        Grid.SetColumn(count, 4);
        var top = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,*,Auto"), ColumnSpacing = 8, Children = { date, status, mode, count } };
        var summary = item.Mode == LotteryMode.Entry
            ? string.Join(" · ", item.Awards.Select(award => $"{award.Name} {award.Prize} ×{award.Capacity}"))
            : string.Join(" · ", item.Prizes.Select(prize => $"{LotteryPanel.GradeNames[(int)prize.Grade]} {prize.Name}"));
        var description = Text(summary, "label", "singleLine");
        var content = new Border { Child = new StackPanel { Spacing = 6, Children = { top, description } } };
        content.Classes.Add("historyRow");
        var row = new ListBoxItem { Tag = item, Content = content };
        AutomationProperties.SetName(row, $"{Date(item)} · {(item.EndedAt is null ? "进行中" : "已结束")} · {Mode(item)} · {Count(item)} · {summary}");
        return row;
    }

    private void AddWinners(string award, string prize, string progress, IEnumerable<(string Name, string Hint)> winners)
    {
        var people = winners.Select(winner =>
        {
            var chip = EntryLotteryPanel.WinnerChip(winner.Name);
            ToolTip.SetTip(chip.Child!, $"{winner.Name} · {winner.Hint}");
            return (Control)chip;
        }).DefaultIfEmpty(Text("还没抽", "caption", "muted"));
        WinnerGroups.Children.Add(EntryLotteryPanel.WinnerGroup(award, prize, progress, people));
    }

    private static TextBlock Text(string value, params string[] classes)
    {
        var text = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center };
        text.Classes.AddRange(classes);
        return text;
    }
}
