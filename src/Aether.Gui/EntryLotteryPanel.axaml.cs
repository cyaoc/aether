using System.ComponentModel;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class EntryLotteryPanel : UserControl
{
    private MainWindowViewModel? viewModel;
    private string? loadedId;
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
            if (!VisibilityButton.IsEnabled || viewModel?.Lottery.LotteryState.Lottery is not EntryLottery entry) return;
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
        if (loading || AwardRows is null) return;
        var state = viewModel?.Lottery.LotteryState;
        if (state?.Lottery is not EntryLottery lottery) { loadedId = null; return; }
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
        var drawing = lottery.IsDrawing;
        KeywordInput.IsReadOnly = !preparing || busy;
        KeywordError.Text = LotteryController.ValidateKeyword(KeywordInput.Text);
        KeywordError.IsVisible = preparing && KeywordError.Text is not null;
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
    }

    internal void ShowError(string message) { ErrorMessage.Text = message; ErrorMessage.IsVisible = true; }

    private async Task ChangeAsync(Action<LotteryController> change)
    {
        if (busy || viewModel is null) return;
        busy = true;
        ErrorMessage.IsVisible = false;
        Refresh();
        try { await viewModel.Lottery.ChangeLotteryAsync(change); }
        catch (Exception error) { ShowError(error.Message); }
        finally { busy = false; Refresh(); }
    }
}
