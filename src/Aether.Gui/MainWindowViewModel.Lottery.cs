using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? lotteryStop;
    private Task lotteryUpdates = Task.CompletedTask;
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
            SaveGuiSettings();
        }
        if (previous.RoomId != LotteryState.RoomId || ((previous.Lottery, LotteryState.Lottery) switch
            {
                (PoolLottery before, PoolLottery after) => before.Id != after.Id || before.Draws != after.Draws
                    || before.Prizes != after.Prizes || before.StopWord != after.StopWord,
                _ => previous.Lottery != LotteryState.Lottery,
            }))
            _ = RefreshLotteryHistoryAsync();
    }

    private void ObserveLottery()
    {
        if (shutdown.IsCancellationRequested || lotteryStop is not null) return;
        lotteryStop = new();
        lotteryUpdates = ReadAsync(lotteryStop.Token);

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

    private async Task FinishLotteryChangeAsync()
    {
        // The page reports failures; closing still waits until a database write has finished.
        try { await lotteryChange; }
        catch { }
    }

    private async Task StopLotteryAsync()
    {
        await FinishLotteryChangeAsync();
        lotteryStop?.Cancel();
        await lotteryUpdates;
        lotteryStop?.Dispose();
        lotteryStop = null;
        await lotteryHistoryRead.StopAsync();
    }
}
