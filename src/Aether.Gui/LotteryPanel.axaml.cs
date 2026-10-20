using System.ComponentModel;
using System.Globalization;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class LotteryPanel : UserControl
{
    private MainWindowViewModel? viewModel;
    private string? loadedId;
    private long? loadedRoom;
    private bool loading;
    private bool busy;

    public LotteryPanel()
    {
        InitializeComponent();
        ViewTabs.SelectionChanged += (_, _) =>
        {
            Refresh();
            if (ViewTabs.SelectedIndex == 1 && viewModel is not null) _ = viewModel.Lottery.RefreshLotteryHistoryAsync();
        };
        HistoryContent.ReturnRequested += () => ViewTabs.SelectedIndex = 0;
        StopWordInput.Text = LotteryController.DefaultStopWord;
        AddPrize(LotteryController.DefaultPrize, isNew: true);
        AddButton.Click += (_, _) =>
        {
            if (!AddButton.IsEnabled) return;
            var row = AddPrize(new("", 1, Grade.Third), isNew: true);
            Refresh();
            Dispatcher.UIThread.Post(() =>
            {
                if (PrizeRows.Children.Contains(row)) row.FocusInput();
            }, DispatcherPriority.Loaded);
        };
        UidInput.TextChanged += (_, _) => Refresh();
        StopWordInput.TextChanged += (_, _) => Refresh();
        StartButton.Click += async (_, _) =>
        {
            if (!StartButton.IsEnabled) return;
            var prizes = Prizes();
            var word = StopWordInput.Text ?? "";
            var uid = ParseUid();
            await ChangeAsync(lottery => lottery.Start(prizes, word, uid));
        };
        NewButton.Click += async (_, _) =>
        {
            if (!NewButton.IsEnabled || TopLevel.GetTopLevel(this) is not Window owner) return;
            var mode = await new NewLotteryWindow().ShowDialog<LotteryMode?>(owner);
            if (mode is { } selected)
            {
                await ChangeAsync(lottery => lottery.New(selected));
                ViewTabs.SelectedIndex = 0;
            }
        };
        StopButton.Click += async (_, _) => { if (StopButton.IsEnabled) await ChangeAsync(lottery => lottery.Stop()); };
        CancelButton.Click += async (_, _) => { if (CancelButton.IsEnabled) await ChangeAsync(lottery => lottery.Cancel()); };
        Refresh();
    }

    private IEnumerable<PoolPrizeRow> Rows => PrizeRows.Children.OfType<PoolPrizeRow>();
    private PoolPrize[] Prizes() => Rows.Select(row => row.Value).ToArray();
    private long? ParseUid() => Core.Viewer.TryParseUid(UidInput.Text, out var uid) ? uid : null;

    private PoolPrizeRow AddPrize(PoolPrize prize, bool isNew = false)
    {
        var row = new PoolPrizeRow(prize, isNew);
        row.Changed += Refresh;
        row.RemoveRequested += () => { if (Rows.Count() > 1 && AddPrizeBar.IsVisible) { PrizeRows.Children.Remove(row); Refresh(); } };
        PrizeRows.Children.Add(row);
        return row;
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
        if (loading || PrizeRows is null) return;
        var state = viewModel?.Lottery.LotteryState;
        var entry = state?.Lottery as EntryLottery;
        var history = ViewTabs.SelectedIndex == 1;
        HistoryContent.IsVisible = history;
        EntryContent.IsVisible = !history && entry is not null;
        PoolContent.IsVisible = !history && entry is null;
        ModeChip.IsVisible = PhaseChip.IsVisible = !history;
        HistoryTabText.Text = $"历史 {viewModel?.Lottery.LotteryHistory.Count ?? 0}";
        if (entry is not null)
        {
            NewButton.IsEnabled = !busy && state!.Connected && entry.Phase is EntryLotteryPhase.Preparing or EntryLotteryPhase.Ended;
            return;
        }
        var lottery = state?.Lottery as PoolLottery;
        if (loadedId != lottery?.Id || loadedRoom is not null && loadedRoom != state?.RoomId)
        {
            loading = true;
            loadedId = lottery?.Id;
            loadedRoom = state?.RoomId;
            PrizeRows.Children.Clear();
            if (lottery is not null)
                foreach (var prize in lottery.Prizes) AddPrize(prize);
            else AddPrize(LotteryController.DefaultPrize, isNew: true);
            UidInput.Text = lottery?.Uid?.ToString(CultureInfo.InvariantCulture) ?? "";
            StopWordInput.Text = lottery?.StopWord ?? LotteryController.DefaultStopWord;
            loading = false;
        }
        loadedRoom = state?.RoomId;
        var phase = lottery?.Phase ?? PoolLotteryPhase.Idle;
        var spinning = phase == PoolLotteryPhase.Spinning;
        var drawing = lottery?.IsDrawing == true;
        var idle = !spinning && !drawing;

        var rows = Rows.ToArray();
        var total = rows.Sum(row => (long)Math.Max(0, row.Value.Weight));
        var invalid = false;
        for (var index = 0; index < rows.Length; index++) invalid |= rows[index].Refresh(index, rows.Length, total, idle && !busy);
        var uid = ParseUid();
        UidError.Text = LotteryController.ValidateUid(UidInput.Text);
        StopWordError.Text = LotteryController.ValidateStopWord(StopWordInput.Text);
        var uidInvalid = UidError.Text is not null;
        var wordInvalid = StopWordError.Text is not null;
        // Single inputs check as you type (#136); only new prize rows wait until they are left.
        UidError.IsVisible = uidInvalid;
        StopWordError.IsVisible = wordInvalid;
        UidInput.IsReadOnly = StopWordInput.IsReadOnly = !idle || busy;
        AddPrizeBar.IsVisible = idle;
        AddButton.IsEnabled = idle && !busy && rows.Length < LotteryController.MaxPrizes;
        var viewer = uid is { } shown ? lottery?.Viewers.GetValueOrDefault(shown) : null;
        ViewerHint.Text = uidInvalid ? "" : uid is null ? "不填就只能主播点“停”"
            : viewer is not null ? $"{viewer} · 这场发过弹幕" : "这场还没发过弹幕，喊停后显示昵称";
        ViewerHint.Classes.Set("success", viewer is not null);
        ViewerHint.Classes.Set("muted", viewer is null);

        var connected = state?.Connected == true;
        var ready = connected && !invalid && !uidInvalid && !wordInvalid;
        StartButton.IsVisible = idle;
        StartButton.IsEnabled = idle && !busy && ready;
        StopButton.IsVisible = CancelButton.IsVisible = spinning;
        StopButton.IsEnabled = CancelButton.IsEnabled = spinning && !busy && state?.CanControl == true;
        DrawingButton.IsVisible = drawing;
        NewButton.IsEnabled = idle && !busy && connected;
        var word = spinning || drawing ? lottery!.StopWord : StopWordInput.Text?.Trim();
        var caller = (spinning || drawing ? lottery!.Uid : uid) is { } callerUid
            ? lottery?.Viewers.GetValueOrDefault(callerUid) ?? $"UID {callerUid}" : null;
        StatusText.Text = phase switch
        {
            PoolLotteryPhase.Spinning => caller is null ? "点“停”开奖" : $"正在等 {caller} 发「{word}」，也可以直接点“停”",
            _ when drawing => "直播画面正在播开奖特效",
            _ when !connected => "先连直播间，才能开始抽奖",
            _ => caller is null ? "没填 UID，开始后要主播点“停”" : $"点“开始”，直播画面上的奖品转起来；只认 {caller} 发的「{word}」",
        };
    }

    private async Task ChangeAsync(Action<LotteryController> change)
    {
        if (busy || viewModel is null) return;
        busy = true;
        ErrorMessage.IsVisible = false;
        Refresh();
        try { await viewModel.Lottery.ChangeLotteryAsync(change); }
        catch (Exception error)
        {
            if (EntryContent.IsVisible) EntryContent.ShowError(error.Message);
            else { ErrorMessage.Text = error.Message; ErrorMessage.IsVisible = true; }
        }
        finally { busy = false; Refresh(); }
    }
}
