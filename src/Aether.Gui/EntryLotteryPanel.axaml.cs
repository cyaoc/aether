using System.ComponentModel;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class EntryLotteryPanel : UserControl
{
    private MainWindowViewModel? viewModel;
    private string? loadedId;
    private EntryLottery? displayed;
    private bool loading;
    private bool busy;
    private bool confirmingEnd;

    public EntryLotteryPanel()
    {
        InitializeComponent();
        KeywordInput.TextChanged += (_, _) => Refresh();
        AddAwardButton.Click += (_, _) =>
        {
            if (!AddAwardButton.IsEnabled) return;
            var count = AwardRows.Children.Count;
            var row = AddAward(new("", "", 1, count == 0 ? Grade.Grand : count == 1 ? Grade.Second : Grade.Third), true);
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
        // Ending asks once more in the footer, as on canvas O1.
        EndEntryButton.Click += (_, _) => { if (EndEntryButton.IsEnabled) { confirmingEnd = true; Refresh(); } };
        CancelEndButton.Click += (_, _) => { confirmingEnd = false; Refresh(); };
        ConfirmEndButton.Click += async (_, _) =>
        {
            if (!ConfirmEndButton.IsEnabled) return;
            confirmingEnd = false;
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
        KeywordHint.Text = preparing ? "不能靠送礼物报名" : "开始报名后，关键字和奖项不能再改";
        AddAwardBar.IsVisible = preparing;
        AddAwardButton.IsEnabled = preparing && !busy && AwardRows.Children.Count < LotteryController.MaxAwards;
        var rows = Rows.ToArray();
        var invalid = KeywordError.IsVisible;
        for (var index = 0; index < rows.Length; index++) invalid |= rows[index].Refresh(index, rows.Length, lottery, busy, state.CanControl);
        StartEntryButton.IsVisible = preparing;
        StartEntryButton.IsEnabled = preparing && !busy && state.Connected && !invalid;
        CloseEntriesButton.IsVisible = open;
        CloseEntriesButton.IsEnabled = open && !busy && state.CanControl;
        confirmingEnd &= closed;
        EndEntryButton.IsVisible = closed && !confirmingEnd;
        EndEntryButton.IsEnabled = ConfirmEndButton.IsEnabled = closed && !busy && !drawing && state.CanControl;
        ConfirmEndText.IsVisible = CancelEndButton.IsVisible = ConfirmEndButton.IsVisible = confirmingEnd;
        VisibilityButton.IsVisible = ended;
        VisibilityButton.IsEnabled = ended && !busy && state.CanControl;
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
