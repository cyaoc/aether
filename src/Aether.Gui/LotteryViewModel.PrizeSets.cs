using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class LotteryViewModel
{
    private readonly LatestRun prizeSetsRead = new();
    private long? prizeSetsRoom;
    [ObservableProperty] private IReadOnlyList<LotteryPrizeSet> prizeSets = [];
    internal long? PrizeSetRoomId => LotteryState.RoomId ?? settings.LastLotteryRoomId;

    internal Task RefreshPrizeSetsAsync()
    {
        if (shutdown.IsCancellationRequested) return Task.CompletedTask;
        var roomId = PrizeSetRoomId;
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

    internal async Task SavePrizeSetAsync(LotteryMode mode, string name)
    {
        if (mode == LotteryMode.Pool ? !CanEditPool : !CanEditEntry) return;
        var room = PrizeSetRoomId ?? throw new InvalidOperationException("请先连接直播间。");
        var prizes = Prizes.Select(row => row.Value).ToArray();
        var awards = Awards.Select(row => row.Value).ToArray();
        await ChangeLotteryAsync(lottery => lottery.SavePrizeSet(room, mode, name, prizes, awards));
        await RefreshPrizeSetsAsync();
    }

    internal async Task DeletePrizeSetAsync(LotteryPrizeSet set)
    {
        if (set.Mode == LotteryMode.Pool ? !CanEditPool : !CanEditEntry) return;
        if (PrizeSetRoomId is not { } room) return;
        await ChangeLotteryAsync(lottery => lottery.DeletePrizeSet(room, set.Mode, set.Name));
        await RefreshPrizeSetsAsync();
    }

    internal void LoadPrizeSet(LotteryPrizeSet set)
    {
        if (set.Mode != LotteryMode.Pool || !CanEditPool) return;
        ReplacePoolPrizes(set.Prizes);
        poolPrizeSetStatus = $"已载入「{set.Name}」，改了不会动到方案，要更新就再存一次";
        RefreshPoolInputs();
    }
}
