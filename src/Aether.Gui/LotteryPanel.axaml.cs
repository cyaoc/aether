using System.ComponentModel;
using System.Globalization;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class LotteryPanel : UserControl
{
    internal static readonly string[] GradeNames = ["大奖", "二奖", "三奖"];
    private static readonly string[] GradeClasses = ["grand", "second", "third"];
    private const string DefaultStopWord = "停";
    private MainWindowViewModel? viewModel;
    private string? loadedId;
    private long? loadedRoom;
    private bool loading;
    private bool busy;
    private PoolLottery? displayedHistory;

    public LotteryPanel()
    {
        InitializeComponent();
        ViewTabs.SelectionChanged += (_, _) =>
        {
            Refresh();
            if (ViewTabs.SelectedIndex == 1 && viewModel is not null) _ = viewModel.RefreshLotteryHistoryAsync();
        };
        HistoryContent.ReturnRequested += () => ViewTabs.SelectedIndex = 0;
        StopWordInput.Text = DefaultStopWord;
        AddPrize(new("", 1, Grade.Grand), isNew: true);
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
    private long? ParseUid() => long.TryParse(UidInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) && uid > 0 ? uid : null;

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
        if (viewModel is not null) viewModel.PropertyChanged -= StateChanged;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.PropertyChanged += StateChanged;
        Refresh();
        if (viewModel is not null) _ = viewModel.RefreshLotteryHistoryAsync();
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.LotteryState) or nameof(MainWindowViewModel.LotteryHistory)) Refresh();
    }

    private void Refresh()
    {
        if (loading || PrizeRows is null) return;
        var state = viewModel?.LotteryState;
        var entry = state?.EntryLottery;
        var history = ViewTabs.SelectedIndex == 1;
        HistoryContent.IsVisible = history;
        EntryContent.IsVisible = !history && entry is not null;
        PoolContent.IsVisible = !history && entry is null;
        ModeChip.IsVisible = PhaseChip.IsVisible = !history;
        HistoryTabText.Text = $"历史 {viewModel?.LotteryHistory.Count ?? 0}";
        ModeText.Text = entry is not null ? "报名抽奖" : "奖池抽奖";
        NewHint.Text = entry is { Phase: EntryLotteryPhase.Open or EntryLotteryPhase.Closed } ? "先结束这一场才能新开" : "";
        if (entry is not null)
        {
            PhaseText.Text = MainWindowViewModel.EntryPhaseText(entry);
            PhaseChip.Classes.Set("spinning", entry.Phase == EntryLotteryPhase.Open);
            PhaseChip.Classes.Set("drawing", entry.Phase == EntryLotteryPhase.Closed);
            PhaseChip.Classes.Set("ended", entry.Phase == EntryLotteryPhase.Ended);
            NewButton.IsEnabled = !busy && state!.Connected && entry.Phase is EntryLotteryPhase.Preparing or EntryLotteryPhase.Ended;
            return;
        }
        var lottery = state?.PoolLottery;
        if (loadedId != lottery?.Id || loadedRoom is not null && loadedRoom != state?.RoomId)
        {
            loading = true;
            loadedId = lottery?.Id;
            loadedRoom = state?.RoomId;
            PrizeRows.Children.Clear();
            if (lottery is not null)
                foreach (var prize in lottery.Prizes) AddPrize(prize);
            else AddPrize(new("", 1, Grade.Grand), isNew: true);
            UidInput.Text = lottery?.Uid?.ToString(CultureInfo.InvariantCulture) ?? "";
            StopWordInput.Text = lottery?.StopWord ?? DefaultStopWord;
            loading = false;
        }
        loadedRoom = state?.RoomId;
        var phase = lottery?.Phase ?? PoolLotteryPhase.Idle;
        var spinning = phase == PoolLotteryPhase.Spinning;
        var drawing = lottery?.IsDrawing == true;
        var idle = !spinning && !drawing;
        PhaseText.Text = viewModel?.LotteryPhaseText is { Length: > 0 } text ? text : "待机";
        PhaseChip.Classes.Set("spinning", spinning);
        PhaseChip.Classes.Set("drawing", drawing);
        PhaseChip.Classes.Set("ended", false);

        var rows = Rows.ToArray();
        var total = rows.Sum(row => (long)Math.Max(0, row.Value.Weight));
        var invalid = false;
        for (var index = 0; index < rows.Length; index++) invalid |= rows[index].Refresh(index, rows.Length, total, idle && !busy);
        var uid = ParseUid();
        var uidInvalid = !string.IsNullOrWhiteSpace(UidInput.Text) && uid is null;
        var wordInvalid = string.IsNullOrWhiteSpace(StopWordInput.Text);
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
            SetGrade(LatestGrade, LatestGradeText, last.Prize.Grade);
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

    internal static void SetGrade(Border badge, TextBlock text, Grade grade)
    {
        text.Text = GradeNames[(int)grade];
        for (var index = 0; index < GradeClasses.Length; index++) badge.Classes.Set(GradeClasses[index], index == (int)grade);
    }

    /// <summary>One line per draw as on canvas V1: who, 档次, prize, time.</summary>
    private static Border HistoryRow(PoolDraw draw)
    {
        var who = new TextBlock { Text = Viewer(draw) ?? "主播点停", FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold, MaxWidth = 84, VerticalAlignment = VerticalAlignment.Center };
        who.Classes.Add("singleLine");
        var gradeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var grade = new Border { Child = gradeText, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        grade.Classes.Add("badge");
        SetGrade(grade, gradeText, draw.Prize.Grade);
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

    private async Task ChangeAsync(Action<LotteryController> change)
    {
        if (busy || viewModel is null) return;
        busy = true;
        ErrorMessage.IsVisible = false;
        Refresh();
        try { await viewModel.ChangeLotteryAsync(change); }
        catch (Exception error)
        {
            if (EntryContent.IsVisible) EntryContent.ShowError(error.Message);
            else { ErrorMessage.Text = error.Message; ErrorMessage.IsVisible = true; }
        }
        finally { busy = false; Refresh(); }
    }
}
