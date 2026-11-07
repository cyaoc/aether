using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Aether.Gui;

public partial class LotteryViewModel : ObservableObject
{
    private readonly AetherClient client;
    private readonly ILogger logger;
    private readonly CancellationToken shutdown;
    private readonly TimeProvider clock;

    public LotteryViewModel(AetherClient client, ILogger logger, TimeProvider clock, CancellationToken shutdown)
    {
        this.client = client;
        this.logger = logger;
        this.shutdown = shutdown;
        this.clock = clock;
        PoolPrizeSets = new(this, LotteryMode.Pool);
        EntryPrizeSets = new(this, LotteryMode.Entry);
        Prizes.RowChanged += RefreshPoolInputs;
        Awards.RowChanged += RefreshEntryInputs;
        RefreshPoolInputs();
        RefreshEntryInputs();
    }

    private readonly LatestRun lotteryUpdates = new();
    [ObservableProperty] private bool busy;
    private bool CanBeginOperation => !Busy && LotteryState.Connected;
    private bool CanControlOperation => !Busy && LotteryState.CanControl;
    private LotteryMode CurrentMode => LotteryState.Lottery is EntryLottery ? LotteryMode.Entry : LotteryMode.Pool;
    [ObservableProperty]
    private LotteryState lotteryState = new(null, false, null);

    partial void OnLotteryStateChanged(LotteryState value)
    {
        RefreshPoolForm();
        RefreshEntryForm();
        UpdateEntryRows(value);
        OnPropertyChanged("");
    }

    partial void OnBusyChanged(bool value)
    {
        RefreshPoolInputs();
        RefreshEntryInputs();
    }

    /// <summary>The sidebar shows the phase only while a draw is under way.</summary>
    public string LotteryPhaseText => LotteryState.Lottery switch
    {
        EntryLottery { Phase: EntryLotteryPhase.Open } => "报名中",
        EntryLottery { Phase: EntryLotteryPhase.Closed } => "开奖中",
        PoolLottery { Phase: PoolLotteryPhase.Spinning } => "转动中",
        PoolLottery { IsDrawing: true } => "开奖中",
        _ => "",
    };

    /// <summary>The 抽奖 page's phase chip for 报名抽奖, as on canvas O1.</summary>
    internal static string EntryPhaseText(EntryLottery lottery) => lottery.Phase switch
    {
        EntryLotteryPhase.Preparing => "准备中",
        EntryLotteryPhase.Open => "报名中",
        EntryLotteryPhase.Closed => "已截止 · 开奖中",
        _ => "已结束",
    };

    internal void Apply(LotteryUpdate update)
    {
        var previous = LotteryState;
        LotteryState = update.State;
        if (previous.RoomId != LotteryState.RoomId || ((previous.Lottery, LotteryState.Lottery) switch
            {
                (PoolLottery before, PoolLottery after) => before.Id != after.Id || before.Draws != after.Draws
                    || before.Prizes != after.Prizes || before.StopWord != after.StopWord,
                _ => previous.Lottery != LotteryState.Lottery,
            }))
            _ = RefreshLotteryHistoryAsync();
    }

    internal void ObserveLottery()
    {
        if (shutdown.IsCancellationRequested || !lotteryUpdates.Current.IsCompleted) return;
        lotteryUpdates.Follow(client.Lottery.GetUpdatesAsync, Apply, logger, "抽奖状态订阅中断");
    }

    internal async Task ChangeLotteryAsync(Func<LotteryFacade, Task> change)
    {
        if (Busy) return;
        if (shutdown.IsCancellationRequested) throw new InvalidOperationException("程序正在关闭。");
        Busy = true;
        try { await change(client.Lottery); }
        finally { Busy = false; }
    }

    /// <summary>Like <see cref="ChangeLotteryAsync"/>, but a failure shows under the given mode's panel.</summary>
    internal async Task<bool> TryChangeLotteryAsync(LotteryMode panel, Func<LotteryFacade, Task> change)
    {
        if (Busy) return false;
        SetLotteryError(panel, null);
        try { await ChangeLotteryAsync(change); return true; }
        catch (Exception error) { SetLotteryError(panel, error.Message); return false; }
    }

    private void SetLotteryError(LotteryMode panel, string? message)
    {
        if (panel == LotteryMode.Pool) PoolLotteryError = message;
        else EntryLotteryError = message;
    }

    internal void SelectLotteryTheme(LotteryTheme theme) => ShowAfterAsync(client.Lottery.SelectThemeAsync(theme)).RaiseFailure();

    internal Task PlayLotteryExampleAsync() => shutdown.IsCancellationRequested ? Task.CompletedTask
        : ShowAfterAsync(client.WebService.Lottery.PlayExampleAsync());

    // Shown as soon as Core has made the change, without waiting for its report.
    private async Task ShowAfterAsync(Task change)
    {
        await change;
        Apply(new LotteryUpdate(client.Lottery.State));
    }

    private readonly LatestRun lotteryHistoryRead = new();
    private string? lotteryHistorySelection;
    [ObservableProperty]
    private IReadOnlyList<LotteryHistory> lotteryHistory = [];
    [ObservableProperty]
    private Lottery? selectedLotteryHistory;
    [ObservableProperty]
    private string lotteryHistoryError = "";
    [ObservableProperty] private bool lotteryHistoryLoading;

    partial void OnLotteryHistoryChanged(IReadOnlyList<LotteryHistory> value) => OnPropertyChanged("");
    partial void OnSelectedLotteryHistoryChanged(Lottery? value) => OnPropertyChanged("");
    partial void OnLotteryHistoryErrorChanged(string value) => OnPropertyChanged("");
    partial void OnLotteryHistoryLoadingChanged(bool value) => OnPropertyChanged("");

    internal Task RefreshLotteryHistoryAsync(string? selectedId = null)
    {
        if (shutdown.IsCancellationRequested) return Task.CompletedTask;
        // Without a room connection, the room is read with the history: the one where the last lottery was held.
        var roomId = LotteryState.RoomId;
        if (roomId is not null && LotteryHistory.FirstOrDefault()?.RoomId != roomId)
        {
            lotteryHistorySelection = null;
            LotteryHistory = [];
            SelectedLotteryHistory = null;
        }
        if (selectedId is not null) lotteryHistorySelection = selectedId;
        selectedId = lotteryHistorySelection;
        LotteryHistoryLoading = true;
        LotteryHistoryError = "";
        return lotteryHistoryRead.Start(ReadAsync);

        async Task ReadAsync(CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested || shutdown.IsCancellationRequested) return;
                var items = (roomId ?? await client.Lottery.GetLastLotteryRoomAsync()) is { } room
                    ? await client.Lottery.GetHistoryAsync(room) : [];
                var selected = items.FirstOrDefault(item => item.Id == selectedId) ?? items.FirstOrDefault();
                var details = selected is null ? null : await client.Lottery.GetHistoryDetailsAsync(selected.RoomId, selected.Id);
                if (token.IsCancellationRequested || shutdown.IsCancellationRequested) return;
                LotteryHistory = items;
                SelectedLotteryHistory = details;
                lotteryHistorySelection = details?.Id;
            }
            catch (Exception error)
            {
                if (token.IsCancellationRequested || shutdown.IsCancellationRequested) return;
                logger.LogError(error, "读取抽奖历史失败");
                LotteryHistoryError = $"读取抽奖历史失败：{error.Message}";
            }
            finally { if (!token.IsCancellationRequested) LotteryHistoryLoading = false; }
        }
    }
}
