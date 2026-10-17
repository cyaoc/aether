using System.ComponentModel;
using System.Globalization;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class LotteryPage : UserControl
{
    private MainWindowViewModel? viewModel;
    private string? loadedId;
    private long? loadedRoom;
    private bool loading;
    private bool busy;
    private bool uidRevealed;
    private bool wordRevealed;
    private PoolLotterySession? displayedHistory;

    public LotteryPage()
    {
        InitializeComponent();
        AddPrize(new("", 1, PrizeGrade.Grand), isNew: true);
        AddButton.Click += (_, _) =>
        {
            if (!AddButton.IsEnabled) return;
            var row = AddPrize(new("", 1, PrizeGrade.Third), isNew: true);
            Refresh();
            Dispatcher.UIThread.Post(() =>
            {
                if (PrizeRows.Children.Contains(row)) row.FocusInput();
            }, DispatcherPriority.Loaded);
        };
        UidInput.TextChanged += (_, _) => Refresh();
        UidInput.LostFocus += (_, _) => { uidRevealed = true; Refresh(); };
        StopWordInput.TextChanged += (_, _) => Refresh();
        StopWordInput.LostFocus += (_, _) => { wordRevealed = true; Refresh(); };
        StartButton.Click += async (_, _) =>
        {
            if (!StartButton.IsEnabled) return;
            var prizes = Rows.Select(row => row.Value).ToArray();
            var word = StopWordInput.Text ?? "";
            var uid = ParseUid();
            await ChangeAsync(lottery => lottery.Start(prizes, word, uid));
        };
        NewButton.Click += async (_, _) =>
        {
            if (!NewButton.IsEnabled) return;
            var prizes = Rows.Select(row => row.Value).ToArray();
            var word = StopWordInput.Text ?? "";
            await ChangeAsync(lottery => lottery.New(prizes, word));
        };
        StopButton.Click += async (_, _) => { if (StopButton.IsEnabled) await ChangeAsync(lottery => lottery.Stop()); };
        CancelButton.Click += async (_, _) => { if (CancelButton.IsEnabled) await ChangeAsync(lottery => lottery.Cancel()); };
        Refresh();
    }

    private IEnumerable<PoolPrizeRow> Rows => PrizeRows.Children.OfType<PoolPrizeRow>();
    private long? ParseUid() => long.TryParse(UidInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid) ? uid : null;

    private PoolPrizeRow AddPrize(PoolPrize prize, bool isNew = false)
    {
        var row = new PoolPrizeRow(prize, isNew);
        row.Changed += Refresh;
        row.RemoveRequested += () => { if (Rows.Count() > 1 && PrizeRows.IsEnabled) { PrizeRows.Children.Remove(row); Refresh(); } };
        PrizeRows.Children.Add(row);
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
        if (loading || PrizeRows is null) return;
        var state = viewModel?.LotteryState;
        var lottery = state?.Lottery;
        if (loadedId != lottery?.Id || loadedRoom is not null && loadedRoom != state?.RoomId)
        {
            loading = true;
            loadedId = lottery?.Id;
            loadedRoom = state?.RoomId;
            PrizeRows.Children.Clear();
            if (lottery is not null)
                foreach (var prize in lottery.Prizes) AddPrize(prize);
            else AddPrize(new("", 1, PrizeGrade.Grand), isNew: true);
            UidInput.Text = lottery?.Uid?.ToString(CultureInfo.InvariantCulture) ?? "";
            StopWordInput.Text = lottery?.StopWord ?? "停";
            uidRevealed = wordRevealed = false;
            loading = false;
        }
        loadedRoom = state?.RoomId;
        var phase = lottery?.Phase ?? PoolLotteryPhase.Idle;
        PhaseText.Text = phase switch { PoolLotteryPhase.Spinning => "转动中", PoolLotteryPhase.Revealing => "开奖中", _ => "待机" };
        var rows = Rows.ToArray();
        var total = rows.Sum(row => (long)Math.Max(0, row.Value.Weight));
        var invalid = rows.Length == 0;
        for (var index = 0; index < rows.Length; index++) invalid |= rows[index].Refresh(index, rows.Length, total);
        var uid = ParseUid();
        var uidInvalid = !string.IsNullOrWhiteSpace(UidInput.Text) && (uid is null or <= 0);
        var wordInvalid = string.IsNullOrWhiteSpace(StopWordInput.Text);
        UidError.Text = "UID 必须是正整数或留空。";
        UidError.IsVisible = uidInvalid && uidRevealed;
        StopWordError.Text = "口令不能为空。";
        StopWordError.IsVisible = wordInvalid && wordRevealed;
        ViewerHint.Text = uid is > 0 ? lottery?.Viewers.GetValueOrDefault(uid.Value) is { } name
            ? $"{name} · 这场发过弹幕" : "这位观众这场还没发过弹幕"
            : "不填 UID 时，只能由主播点停。";
        var idle = phase == PoolLotteryPhase.Idle;
        var spinning = phase == PoolLotteryPhase.Spinning;
        PrizeRows.IsEnabled = StopFields.IsEnabled = idle && !busy;
        AddButton.IsEnabled = idle && !busy && rows.Length < 10;
        StartButton.IsVisible = idle;
        StartButton.IsEnabled = idle && !busy && state?.Connected == true && !invalid && !uidInvalid && !wordInvalid;
        StopButton.IsVisible = CancelButton.IsVisible = spinning;
        StopButton.IsEnabled = CancelButton.IsEnabled = spinning && !busy && state?.CanControl == true;
        RevealingButton.IsVisible = phase == PoolLotteryPhase.Revealing;
        NewButton.IsEnabled = phase != PoolLotteryPhase.Revealing && !busy && state?.Connected == true && !invalid && !wordInvalid;
        ConnectionHint.IsVisible = state?.Connected != true;
        if (displayedHistory?.Id != lottery?.Id || displayedHistory?.Draws != lottery?.Draws)
        {
            displayedHistory = lottery;
            HistoryRows.Children.Clear();
            LatestResult.Text = lottery is { Draws.Length: > 0 } ? ResultText(lottery.Draws[^1]) : "还没有开奖";
            if (lottery is not null)
                foreach (var draw in lottery.Draws.Reverse())
                {
                    var text = new TextBlock { Text = $"{ResultText(draw)}\n{draw.DrawnAt.ToLocalTime():HH:mm:ss}", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                    text.Classes.Add("secondary");
                    var border = new Border { Child = text };
                    border.Classes.Add("activityRow");
                    HistoryRows.Children.Add(border);
                }
        }
    }

    private static string ResultText(PoolDraw draw)
    {
        var grade = draw.Prize.Grade switch { PrizeGrade.Grand => "大奖", PrizeGrade.Second => "二奖", _ => "三奖" };
        var viewer = draw.Uid is { } uid ? $"\n{draw.Nickname ?? $"UID {uid}"}" : "";
        return $"{grade} · {draw.Prize.Name}{viewer}{(draw.StoppedByHost ? "\n主播点停" : "")}";
    }

    private async Task ChangeAsync(Action<PoolLottery> change)
    {
        if (busy || viewModel is null) return;
        busy = true;
        ErrorMessage.IsVisible = false;
        Refresh();
        try { await viewModel.ChangeLotteryAsync(change); }
        catch (Exception error) { ErrorMessage.Text = error.Message; ErrorMessage.IsVisible = true; }
        finally { busy = false; Refresh(); }
    }
}
