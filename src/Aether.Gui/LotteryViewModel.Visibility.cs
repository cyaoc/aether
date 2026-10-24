using Aether.Core;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    public bool PinnedResults
    {
        get => LotteryState.PinnedResults;
        set
        {
            if (value == PinnedResults) return;
            client.Lottery.SetPinnedResults(value);
            Apply(new LotteryUpdate(client.Lottery.State));
        }
    }

    public string PinnedResultsHint => PinnedResults ? "下面没变灰的，就是直播画面上挂着的" : "默认关；打开前可以先点‘全部不挂出’";

    [RelayCommand]
    private Task HideAllDrawsAsync() => CanHideAllDraws
        ? TryChangeLotteryAsync(CurrentMode, lottery => lottery.HideAllDrawsFromStream()) : Task.CompletedTask;

    public bool CanHideAllDraws => CanControlOperation && (LotteryState.Lottery switch
    {
        PoolLottery pool => pool.Draws.Any(draw => !draw.HiddenFromStream),
        EntryLottery entry => entry.Draws.Any(draw => !draw.HiddenFromStream),
        _ => false,
    });

    internal Task ToggleDrawVisibilityAsync(LotteryDraw draw) => CanControlOperation
        ? TryChangeLotteryAsync(CurrentMode, lottery => lottery.SetDrawHiddenFromStream(draw.Id, !draw.HiddenFromStream)) : Task.CompletedTask;
}
