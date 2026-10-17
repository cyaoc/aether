using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? lotteryStop;
    private Task lotteryUpdates = Task.CompletedTask;
    private Task lotteryChange = Task.CompletedTask;
    [ObservableProperty] private PoolLotteryState lotteryState = new(null, false, null);

    internal void Apply(PoolLotteryUpdate update) => LotteryState = update.State;

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

    internal Task ChangeLotteryAsync(Action<PoolLottery> change)
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
