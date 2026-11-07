using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private readonly LatestRun prizeSetsRead = new();
    [ObservableProperty] private IReadOnlyList<LotteryPrizeSet> prizeSets = [];
    public PrizeSetPickerViewModel PoolPrizeSets { get; }
    public PrizeSetPickerViewModel EntryPrizeSets { get; }

    internal bool CanEdit(LotteryMode mode) => mode == LotteryMode.Pool ? CanEditPool : CanEditEntry;

    internal Task RefreshPrizeSetsAsync()
    {
        if (shutdown.IsCancellationRequested) return Task.CompletedTask;
        return prizeSetsRead.Start(async token =>
        {
            try
            {
                if (token.IsCancellationRequested || shutdown.IsCancellationRequested) return;
                var sets = await Task.Run<IReadOnlyList<LotteryPrizeSet>>(() =>
                    [.. client.Lottery.GetPrizeSets(LotteryMode.Pool), .. client.Lottery.GetPrizeSets(LotteryMode.Entry)]);
                if (!token.IsCancellationRequested && !shutdown.IsCancellationRequested) PrizeSets = sets;
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested && !shutdown.IsCancellationRequested)
                {
                    PoolLotteryError = EntryLotteryError = $"读取奖品方案失败：{error.Message}";
                }
            }
        });
    }

    internal async Task<bool> SavePrizeSetAsync(LotteryMode mode, string name)
    {
        if (!CanEdit(mode)) return false;
        var prizes = Prizes.Select(row => row.Value).ToArray();
        var awards = Awards.Select(row => row.Value).ToArray();
        if (!await TryChangeLotteryAsync(mode, lottery => lottery.SavePrizeSet(mode, name, prizes, awards))) return false;
        PrizeSetPicker(mode).Choose(name.Trim(), saved: true);
        OnPropertyChanged(mode == LotteryMode.Pool ? nameof(PoolStatusText) : nameof(EntryStatusText));
        await RefreshPrizeSetsAsync();
        return true;
    }

    internal async Task DeletePrizeSetAsync(LotteryPrizeSet set)
    {
        if (!CanEdit(set.Mode)) return;
        if (!await TryChangeLotteryAsync(set.Mode, lottery => lottery.DeletePrizeSet(set.Mode, set.Name))) return;
        if (PrizeSetPicker(set.Mode).Chosen == set.Name)
        {
            PrizeSetPicker(set.Mode).Choose(null);
            OnPropertyChanged(set.Mode == LotteryMode.Pool ? nameof(PoolStatusText) : nameof(EntryStatusText));
        }
        await RefreshPrizeSetsAsync();
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
