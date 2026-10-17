using System.ComponentModel;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class EntryLotteryPanel : UserControl
{
    private MainWindowViewModel? viewModel;
    private string? loadedId;
    private EntryLottery? displayed;
    private bool loading;
    private bool busy;

    public EntryLotteryPanel()
    {
        InitializeComponent();
        KeywordInput.TextChanged += (_, _) => Refresh();
        AddAwardButton.Click += (_, _) =>
        {
            if (!AddAwardButton.IsEnabled) return;
            var count = AwardRows.Children.Count;
            var row = AddAward(new("", "", 1, count == 0 ? PrizeGrade.Grand : count == 1 ? PrizeGrade.Second : PrizeGrade.Third), true);
            Refresh();
            Dispatcher.UIThread.Post(() => { if (AwardRows.Children.Contains(row)) row.FocusInput(); }, DispatcherPriority.Loaded);
        };
        StartEntryButton.Click += async (_, _) =>
        {
            if (!StartEntryButton.IsEnabled) return;
            var awards = Rows.Select(row => row.Value).ToArray();
            var keyword = KeywordInput.Text ?? "";
            await ChangeAsync(lottery => lottery.StartEntry(awards, keyword));
        };
        CloseEntriesButton.Click += async (_, _) => { if (CloseEntriesButton.IsEnabled) await ChangeAsync(lottery => lottery.CloseEntries()); };
        EndEntryButton.Click += async (_, _) =>
        {
            if (!EndEntryButton.IsEnabled || TopLevel.GetTopLevel(this) is not Window owner) return;
            var id = viewModel?.LotteryState.EntryLottery?.Id;
            if (await new ConfirmEndLotteryWindow().ShowDialog<bool>(owner) && viewModel?.LotteryState.EntryLottery?.Id == id)
                await ChangeAsync(lottery => lottery.EndEntry());
        };
        VisibilityButton.Click += async (_, _) =>
        {
            if (!VisibilityButton.IsEnabled || viewModel?.LotteryState.EntryLottery is not { } entry) return;
            await ChangeAsync(lottery => lottery.SetEntryHidden(!entry.Hidden));
        };
    }

    private IEnumerable<EntryAwardRow> Rows => AwardRows.Children.OfType<EntryAwardRow>();

    private EntryAwardRow AddAward(LotteryAward award, bool isNew = false)
    {
        var row = new EntryAwardRow(award, isNew);
        row.Changed += Refresh;
        row.RemoveRequested += () =>
        {
            if (!busy && AddAwardBar.IsVisible && AwardRows.Children.Count > 1) { AwardRows.Children.Remove(row); Refresh(); }
        };
        row.DrawRequested += async () =>
        {
            var index = AwardRows.Children.IndexOf(row);
            if (index >= 0) await ChangeAsync(lottery => lottery.DrawEntry(index));
        };
        AwardRows.Children.Add(row);
        return row;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.PropertyChanged -= StateChanged;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.PropertyChanged += StateChanged;
        Refresh();
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.LotteryState)) Refresh();
    }

    private void Refresh()
    {
        if (loading || AwardRows is null) return;
        var state = viewModel?.LotteryState;
        if (state?.EntryLottery is not { } lottery) { loadedId = null; displayed = null; return; }
        if (loadedId != lottery.Id)
        {
            loading = true;
            loadedId = lottery.Id;
            KeywordInput.Text = lottery.Keyword;
            AwardRows.Children.Clear();
            foreach (var award in lottery.Awards) AddAward(award, string.IsNullOrWhiteSpace(award.Prize));
            ErrorMessage.IsVisible = false;
            loading = false;
        }
        var preparing = lottery.Phase == EntryLotteryPhase.Preparing;
        var open = lottery.Phase == EntryLotteryPhase.Open;
        var closed = lottery.Phase == EntryLotteryPhase.Closed;
        var ended = lottery.Phase == EntryLotteryPhase.Ended;
        var drawing = lottery.DrawUntil is not null;
        KeywordInput.IsReadOnly = !preparing || busy;
        KeywordError.IsVisible = preparing && string.IsNullOrWhiteSpace(KeywordInput.Text);
        LockedHint.IsVisible = !preparing;
        AddAwardBar.IsVisible = preparing;
        AddAwardButton.IsEnabled = preparing && !busy && AwardRows.Children.Count < LotteryController.MaxAwards;
        var rows = Rows.ToArray();
        var invalid = KeywordError.IsVisible;
        for (var index = 0; index < rows.Length; index++) invalid |= rows[index].Refresh(index, rows.Length, lottery, busy, state.CanControl);
        StartEntryButton.IsVisible = preparing;
        StartEntryButton.IsEnabled = preparing && !busy && state.Connected && !invalid;
        CloseEntriesButton.IsVisible = open;
        CloseEntriesButton.IsEnabled = open && !busy && state.CanControl;
        EndEntryButton.IsVisible = closed;
        EndEntryButton.IsEnabled = closed && !busy && !drawing && state.CanControl;
        VisibilityButton.IsVisible = ended;
        VisibilityButton.IsEnabled = ended && !busy && state.CanControl;
        VisibilityButton.Content = lottery.Hidden ? "重新显示" : "收起画面";
        StatusText.Text = lottery.Phase switch
        {
            EntryLotteryPhase.Preparing => state.Connected ? "填好关键字和奖项，点“开始报名”" : "先连直播间，才能开始报名",
            EntryLotteryPhase.Open => $"已有 {lottery.Entries.Length} 人报名，直播画面正显示报名方式和名单",
            EntryLotteryPhase.Closed when drawing => "直播画面正在播开奖特效",
            EntryLotteryPhase.Closed when lottery.Entries.Length <= lottery.Draws.Length => "没有还没中奖的报名观众可抽，可以结束这场抽奖",
            EntryLotteryPhase.Closed => $"{lottery.Entries.Length} 人报名，已抽 {lottery.Draws.Length} / {lottery.Awards.Sum(a => (long)a.Capacity)}。点奖项右边的“抽一个”",
            _ => lottery.Hidden ? "画面已收起，可以重新显示完整中奖名单" : "抽奖页正显示完整中奖名单，点“收起画面”让它消失",
        };
        if (displayed == lottery) return;
        displayed = lottery;
        WinnerCount.Text = $"{lottery.Draws.Length} 人";
        EntryCount.Text = $"{lottery.Entries.Length} 人";
        EmptyWinners.IsVisible = lottery.Draws.Length == 0;
        EmptyEntries.IsVisible = lottery.Entries.Length == 0;
        WinnerGroups.Children.Clear();
        for (var index = 0; index < lottery.Awards.Length; index++)
        {
            var winners = lottery.Draws.Where(draw => draw.AwardIndex == index).ToArray();
            if (winners.Length == 0) continue;
            var award = lottery.Awards[index];
            var title = new TextBlock { Text = $"{award.Name} · {award.Prize}   {winners.Length} / {award.Capacity}" };
            title.Classes.AddRange(["caption", "muted", "singleLine"]);
            var names = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var winner in winners)
            {
                var name = new TextBlock { Text = winner.Winner.Nickname, MaxWidth = 210 };
                name.Classes.Add("singleLine");
                var chip = new Border { Child = name, Margin = new(0, 4, 6, 0), Padding = new(6, 2) };
                chip.Classes.AddRange(["badge", "neutralBadge", "winner"]);
                chip.Classes.Set("fresh", drawing && winner.Id == lottery.Draws[^1].Id);
                names.Children.Add(chip);
            }
            var group = new Border { Child = new StackPanel { Children = { title, names } } };
            WinnerGroups.Children.Add(group);
        }
        EntryRows.Children.Clear();
        var won = lottery.Draws.ToDictionary(draw => draw.Winner.Uid);
        foreach (var entry in lottery.Entries.Reverse())
        {
            var name = new TextBlock { Text = entry.Nickname, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            name.Classes.Add("singleLine");
            var at = new TextBlock { Text = $"{entry.EnteredAt.ToLocalTime():HH:mm:ss}", VerticalAlignment = VerticalAlignment.Center };
            at.Classes.AddRange(["caption", "muted", "numeric"]);
            var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 6, Children = { name, at } };
            Grid.SetColumn(at, 2);
            if (won.TryGetValue(entry.Uid, out var draw))
            {
                var text = new TextBlock { Text = $"中 {draw.Award.Name}", MaxWidth = 90 };
                text.Classes.Add("singleLine");
                var badge = new Border { Child = text, Height = 20, Padding = new(5, 0), VerticalAlignment = VerticalAlignment.Center };
                badge.Classes.AddRange(["badge", "neutralBadge"]);
                Grid.SetColumn(badge, 1);
                row.Children.Add(badge);
            }
            EntryRows.Children.Add(row);
        }
    }

    internal void ShowError(string message) { ErrorMessage.Text = message; ErrorMessage.IsVisible = true; }

    private async Task ChangeAsync(Action<LotteryController> change)
    {
        if (busy || viewModel is null) return;
        busy = true;
        ErrorMessage.IsVisible = false;
        Refresh();
        try { await viewModel.ChangeLotteryAsync(change); }
        catch (Exception error) { ShowError(error.Message); }
        finally { busy = false; Refresh(); }
    }
}
