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
    public string LotteryPhaseText => LotteryState.EntryLottery is { Phase: EntryLotteryPhase.Open or EntryLotteryPhase.Closed } entry
        ? EntryPhaseText(entry) : LotteryState.Lottery?.Phase switch
    {
        PoolLotteryPhase.Spinning => "转动中",
        PoolLotteryPhase.Drawing => "开奖中",
        _ => "",
    };

    internal static string EntryPhaseText(EntryLottery lottery) => lottery.Phase switch
    {
        EntryLotteryPhase.Preparing => "准备",
        EntryLotteryPhase.Open => "报名中",
        EntryLotteryPhase.Closed => lottery.DrawUntil is not null ? "已截止 · 开奖中" : "已截止",
        _ => "已结束",
    };

    internal void Apply(LotteryUpdate update) => LotteryState = update.State;

    private void ObserveLottery()
    {
        if (lotteryStop is not null) return;
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
        if (shuttingDown) throw new InvalidOperationException("程序正在关闭。");
        return lotteryChange = Task.Run(() => change(client.Lottery));
    }

    private async Task FinishLotteryChangeAsync()
    {
        // The page reports failures; closing still waits until a database write has finished.
        try { await lotteryChange; }
        catch { }
    }

    private async Task StopLotteryAsync()
    {
        lotteryStop?.Cancel();
        await lotteryUpdates;
        lotteryStop?.Dispose();
        lotteryStop = null;
    }
}
