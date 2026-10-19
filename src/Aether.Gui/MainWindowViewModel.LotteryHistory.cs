using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Aether.Gui;

public partial class MainWindowViewModel
{
    private Task lotteryHistoryRead = Task.CompletedTask;
    private int lotteryHistoryVersion;
    private string? lotteryHistorySelection;
    [ObservableProperty] private IReadOnlyList<LotteryHistory> lotteryHistory = [];
    [ObservableProperty] private Lottery? selectedLotteryHistory;
    [ObservableProperty] private string lotteryHistoryError = "";
    [ObservableProperty] private bool lotteryHistoryLoading;

    internal Task RefreshLotteryHistoryAsync(string? selectedId = null)
    {
        if (shuttingDown) return Task.CompletedTask;
        var previous = lotteryHistoryRead;
        var version = ++lotteryHistoryVersion;
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
        return lotteryHistoryRead = ReadAsync();

        async Task ReadAsync()
        {
            try
            {
                await previous;
                if (version != lotteryHistoryVersion || shuttingDown) return;
                var result = await Task.Run(() =>
                {
                    var items = roomId is { } room ? client.Lottery.GetHistory(room) : [];
                    var selected = items.FirstOrDefault(item => item.Id == selectedId) ?? items.FirstOrDefault();
                    var details = selected is null ? null : client.Lottery.GetHistoryDetails(selected.RoomId, selected.Id);
                    return (items, details);
                });
                if (version != lotteryHistoryVersion || shuttingDown) return;
                LotteryHistory = result.items;
                SelectedLotteryHistory = result.details;
                lotteryHistorySelection = result.details?.Id;
            }
            catch (Exception error)
            {
                if (version != lotteryHistoryVersion || shuttingDown) return;
                logger.LogError(error, "读取抽奖历史失败");
                LotteryHistoryError = $"读取抽奖历史失败：{error.Message}";
            }
            finally { if (version == lotteryHistoryVersion) LotteryHistoryLoading = false; }
        }
    }
}
