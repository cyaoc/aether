using Aether.Core;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private IAsyncRelayCommand? hideAllDrawsCommand;
    public IAsyncRelayCommand HideAllDrawsCommand => hideAllDrawsCommand ??= new AsyncRelayCommand(
        () => CanHideAllDraws ? ChangeDrawVisibilityAsync(lottery => lottery.HideAllDrawsFromStream()) : Task.CompletedTask);

    public bool CanHideAllDraws => CanControlOperation && (LotteryState.Lottery switch
    {
        PoolLottery pool => pool.Draws.Any(draw => !draw.HiddenFromStream),
        EntryLottery entry => entry.Draws.Any(draw => !draw.HiddenFromStream),
        _ => false,
    });

    internal Task ToggleDrawVisibilityAsync(LotteryDraw draw) => CanControlOperation
        ? ChangeDrawVisibilityAsync(lottery => lottery.SetDrawHiddenFromStream(draw.Id, !draw.HiddenFromStream)) : Task.CompletedTask;

    private async Task ChangeDrawVisibilityAsync(Action<LotteryController> change)
    {
        var entry = LotteryState.Lottery is EntryLottery;
        if (entry) EntryLotteryError = null;
        else PoolLotteryError = null;
        try { await ChangeLotteryAsync(change); }
        catch (Exception error)
        {
            if (entry) EntryLotteryError = error.Message;
            else PoolLotteryError = error.Message;
        }
    }
}
