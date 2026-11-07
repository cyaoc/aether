using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private readonly LatestRun prizeSetUpdates = new();
    [ObservableProperty] private IReadOnlyList<LotteryPrizeSet> prizeSets = [];
    public PrizeSetPickerViewModel PoolPrizeSets { get; }
    public PrizeSetPickerViewModel EntryPrizeSets { get; }

    internal bool CanEdit(LotteryMode mode) => mode == LotteryMode.Pool ? CanEditPool : CanEditEntry;

    /// <summary>Once a picker shows: Core reports the 奖品方案 now and after each save or delete. A save or delete follows them
    /// again after a read failed.</summary>
    internal void ObservePrizeSets()
    {
        if (shutdown.IsCancellationRequested || !prizeSetUpdates.Current.IsCompleted) return;
        prizeSetUpdates.Follow(client.Lottery.GetPrizeSetUpdatesAsync, sets => PrizeSets = sets,
            error => PoolLotteryError = EntryLotteryError = $"读取奖品方案失败：{error.Message}");
    }

    internal async Task<bool> SavePrizeSetAsync(LotteryMode mode, string name)
    {
        if (!CanEdit(mode)) return false;
        var prizes = Prizes.Select(row => row.Value).ToArray();
        var awards = Awards.Select(row => row.Value).ToArray();
        if (!await TryChangeLotteryAsync(mode, lottery => lottery.SavePrizeSetAsync(mode, name, prizes, awards))) return false;
        PrizeSetPicker(mode).Choose(name.Trim(), saved: true);
        OnPropertyChanged(mode == LotteryMode.Pool ? nameof(PoolStatusText) : nameof(EntryStatusText));
        ObservePrizeSets();
        return true;
    }

    internal async Task DeletePrizeSetAsync(LotteryPrizeSet set)
    {
        if (!CanEdit(set.Mode)) return;
        if (!await TryChangeLotteryAsync(set.Mode, lottery => lottery.DeletePrizeSetAsync(set.Mode, set.Name))) return;
        if (PrizeSetPicker(set.Mode).Chosen == set.Name)
        {
            PrizeSetPicker(set.Mode).Choose(null);
            OnPropertyChanged(set.Mode == LotteryMode.Pool ? nameof(PoolStatusText) : nameof(EntryStatusText));
        }
        ObservePrizeSets();
    }

    internal void LoadPrizeSet(LotteryPrizeSet set)
    {
        if (!CanEdit(set.Mode)) return;
        PrizeSetPicker(set.Mode).Choose(set.Name);
        if (set.Mode == LotteryMode.Pool)
        {
            ReplacePoolPrizes(set.Prizes);
            RefreshPoolInputs();
        }
        else
        {
            ReplaceEntryAwards(set.Awards, fromPrizeSet: true);
            RefreshEntryInputs();
        }
    }

    private PrizeSetPickerViewModel PrizeSetPicker(LotteryMode mode) => mode == LotteryMode.Pool ? PoolPrizeSets : EntryPrizeSets;
}
