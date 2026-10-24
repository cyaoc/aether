using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private readonly LatestRun prizeSetsRead = new();
    private long? prizeSetsRoom;
    [ObservableProperty] private IReadOnlyList<LotteryPrizeSet> prizeSets = [];
    public PrizeSetPickerViewModel PoolPrizeSets { get; }
    public PrizeSetPickerViewModel EntryPrizeSets { get; }

    internal bool CanEdit(LotteryMode mode) => mode == LotteryMode.Pool ? CanEditPool : CanEditEntry;

    internal Task RefreshPrizeSetsAsync()
    {
        if (shutdown.IsCancellationRequested) return Task.CompletedTask;
        var roomId = CurrentOrLastRoomId;
        if (prizeSetsRoom != roomId) { prizeSetsRoom = roomId; PrizeSets = []; }
        return prizeSetsRead.Start(async token =>
        {
            try
            {
                if (token.IsCancellationRequested || shutdown.IsCancellationRequested) return;
                var sets = await Task.Run<IReadOnlyList<LotteryPrizeSet>>(() => roomId is { } room
                    ? [.. client.Lottery.GetPrizeSets(room, LotteryMode.Pool), .. client.Lottery.GetPrizeSets(room, LotteryMode.Entry)] : []);
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
        var room = CurrentOrLastRoomId;
        var prizes = Prizes.Select(row => row.Value).ToArray();
        var awards = Awards.Select(row => row.Value).ToArray();
        if (!await TryChangeLotteryAsync(mode, lottery => lottery.SavePrizeSet(
            room ?? throw new InvalidOperationException("请先连接直播间。"), mode, name, prizes, awards))) return false;
        PrizeSetPicker(mode).Choose(name.Trim(), saved: true);
        OnPropertyChanged(mode == LotteryMode.Pool ? nameof(PoolStatusText) : nameof(EntryStatusText));
        await RefreshPrizeSetsAsync();
        return true;
    }

    internal async Task DeletePrizeSetAsync(LotteryPrizeSet set)
    {
        if (!CanEdit(set.Mode) || CurrentOrLastRoomId is not { } room) return;
        if (await TryChangeLotteryAsync(set.Mode, lottery => lottery.DeletePrizeSet(room, set.Mode, set.Name)))
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
