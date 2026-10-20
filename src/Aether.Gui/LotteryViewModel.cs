using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Aether.Gui;

public partial class LotteryViewModel(AetherClient client, ILogger logger, GuiSettings settings, Action saveSettings,
    CancellationToken shutdown) : ObservableObject
{
    private readonly LatestRun lotteryUpdates = new();
    private Task lotteryChange = Task.CompletedTask;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LotteryPhaseText))] private LotteryState lotteryState = new(null, false, null);

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
        if (LotteryState.RoomId is { } room && settings.LastLotteryRoomId != room)
        {
            settings.LastLotteryRoomId = room;
            saveSettings();
        }
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
        lotteryUpdates.Start(ReadAsync);

        async Task ReadAsync(CancellationToken token)
        {
            try { await foreach (var update in client.Lottery.GetUpdatesAsync(token)) Apply(update); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    internal Task ChangeLotteryAsync(Action<LotteryController> change)
    {
        if (shutdown.IsCancellationRequested) throw new InvalidOperationException("程序正在关闭。");
        return lotteryChange = Task.Run(() => change(client.Lottery));
    }

    internal void SelectLotteryTheme(LotteryTheme theme)
    {
        client.Lottery.SelectTheme(theme);
        Apply(new LotteryUpdate(client.Lottery.State));
    }

    internal void PlayLotteryExample()
    {
        if (shutdown.IsCancellationRequested) return;
        client.WebService.Lottery.PlayExample();
        Apply(new LotteryUpdate(client.Lottery.State));
    }

    internal async Task StopAsync()
    {
        // The page reports failures; closing still waits until a database write has finished.
        try { await lotteryChange; }
        catch { }
        await lotteryUpdates.StopAsync();
        await lotteryHistoryRead.StopAsync();
    }

    private readonly LatestRun lotteryHistoryRead = new();
    private string? lotteryHistorySelection;
    [ObservableProperty] private IReadOnlyList<LotteryHistory> lotteryHistory = [];
    [ObservableProperty] private Lottery? selectedLotteryHistory;
    [ObservableProperty] private string lotteryHistoryError = "";
    [ObservableProperty] private bool lotteryHistoryLoading;

    internal Task RefreshLotteryHistoryAsync(string? selectedId = null)
    {
        if (shutdown.IsCancellationRequested) return Task.CompletedTask;
        var roomId = LotteryState.RoomId ?? settings.LastLotteryRoomId;
        if (LotteryHistory.FirstOrDefault()?.RoomId != roomId)
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
                var result = await Task.Run(() =>
                {
                    var items = roomId is { } room ? client.Lottery.GetHistory(room) : [];
                    var selected = items.FirstOrDefault(item => item.Id == selectedId) ?? items.FirstOrDefault();
                    var details = selected is null ? null : client.Lottery.GetHistoryDetails(selected.RoomId, selected.Id);
                    return (items, details);
                });
                if (token.IsCancellationRequested || shutdown.IsCancellationRequested) return;
                LotteryHistory = result.items;
                SelectedLotteryHistory = result.details;
                lotteryHistorySelection = result.details?.Id;
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
