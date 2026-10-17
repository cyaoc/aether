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
        var selected = viewModel.SelectedLotteryHistory;
        if (!viewModel.LotteryHistoryLoading)
            LotteryList.SelectedItem = LotteryList.Items.OfType<ListBoxItem>().FirstOrDefault(row => row.Tag is LotteryHistory item && item.Id == selected?.Lottery.Id);
        loading = false;
        Details.IsVisible = selected is not null;
        ReturnButton.IsVisible = selected is { Lottery.EndedAt: null };
        ReturnButton.IsEnabled = selected is not null
            && selected.Lottery.Id == (viewModel.LotteryState.EntryLottery?.Id ?? viewModel.LotteryState.Lottery?.Id);
        ReturnHint.IsVisible = ReturnButton.IsVisible && !ReturnButton.IsEnabled;
        if (ReferenceEquals(details, selected)) return;
        details = selected;
        WinnerGroups.Children.Clear();
        if (selected is null) return;
        var lottery = selected.Lottery;
        DetailDate.Text = Date(lottery);
        DetailSummary.Text = $"{Mode(lottery)} · {Count(lottery)}";
        ConditionTitle.Text = lottery.Mode == LotteryMode.Entry ? "报名条件" : "谁来喊停";
        ConditionText.Text = lottery.Mode == LotteryMode.Entry ? $"发弹幕「{lottery.Keyword}」" : $"口令「{lottery.StopWord}」";
        if (lottery.Mode == LotteryMode.Entry)
        {
            for (var index = 0; index < lottery.Awards.Length; index++)
            {
                var award = lottery.Awards[index];
                var winners = selected.EntryDraws.Where(draw => draw.AwardIndex == index).ToArray();
                AddWinners($"{award.Name}  {award.Prize}", $"已抽 {winners.Length} / {award.Capacity}",
                    winners.Select(draw => (draw.Winner.Nickname, $"UID {draw.Winner.Uid}")), "还没抽");
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
                AddWinners($"{LotteryPanel.GradeNames[(int)grade]}  {name}", $"{draws.Length} 次", winners, "还没抽到");
            }
        }
    }

    private static string Date(LotteryHistory item) => $"{item.StartedAt.ToLocalTime():yyyy年M月d日 HH:mm}";
    private static string Mode(LotteryHistory item) => item.Mode == LotteryMode.Entry ? "报名抽奖" : "奖池抽奖";
    private static string Count(LotteryHistory item) => item.Mode == LotteryMode.Entry
        ? $"报名 {item.EntryCount} · 中奖 {item.DrawCount}" : $"抽了 {item.DrawCount} 次";

    private static ListBoxItem Row(LotteryHistory item)
    {
        var date = Text(Date(item), "singleLine");
        date.FontWeight = FontWeight.SemiBold;
        var status = new Border { Child = Text(item.EndedAt is null ? "进行中" : "已结束", "caption"), Padding = new(5, 2), CornerRadius = new(4) };
        status.Classes.Add("historyStatus");
        status.Classes.Set("active", item.EndedAt is null);
        var mode = new Border { Child = Text(Mode(item), "caption"), Padding = new(5, 2) };
        mode.Classes.Add("historyMode");
        var count = Text(Count(item), "caption", "muted");
        Grid.SetColumn(status, 1);
        Grid.SetColumn(mode, 2);
        Grid.SetColumn(count, 3);
        var top = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), ColumnSpacing = 8, Children = { date, status, mode, count } };
        var summary = item.Mode == LotteryMode.Entry
            ? string.Join(" · ", item.Awards.Select(award => $"{award.Name} {award.Prize} ×{award.Capacity}"))
            : string.Join(" · ", item.Prizes.Select(prize => $"{LotteryPanel.GradeNames[(int)prize.Grade]} {prize.Name}"));
        var description = Text(summary, "caption", "singleLine");
        var row = new ListBoxItem { Tag = item, Content = new StackPanel { Spacing = 4, Children = { top, description } } };
        AutomationProperties.SetName(row, $"{Date(item)} · {(item.EndedAt is null ? "进行中" : "已结束")} · {Mode(item)} · {Count(item)} · {summary}");
        return row;
    }

    private void AddWinners(string title, string count, IEnumerable<(string Name, string Hint)> winners, string empty)
    {
        var name = Text(title, "caption", "singleLine");
        var progress = Text(count, "caption", "muted", "numeric");
        Grid.SetColumn(progress, 1);
        var people = new WrapPanel();
        foreach (var (nickname, hint) in winners)
        {
            var text = Text(nickname, "singleLine");
            text.MaxWidth = 230;
            var chip = new Border { Child = text };
            chip.Classes.Add("winner");
            ToolTip.SetTip(chip, $"{nickname} · {hint}");
            people.Children.Add(chip);
        }
        if (people.Children.Count == 0) people.Children.Add(Text(empty, "caption", "muted"));
        WinnerGroups.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children = { new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6, Children = { name, progress } }, people },
        });
    }

    private static TextBlock Text(string value, params string[] classes)
    {
        var text = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center };
        text.Classes.AddRange(classes);
        return text;
    }
}
