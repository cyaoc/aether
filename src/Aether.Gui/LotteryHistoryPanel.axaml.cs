using System.ComponentModel;
using Avalonia.Controls;

namespace Aether.Gui;

public partial class LotteryHistoryPanel : UserControl
{
    private MainWindowViewModel? viewModel;
    private bool loading;
    internal event Action? ReturnRequested;

    public LotteryHistoryPanel()
    {
        InitializeComponent();
        LotteryList.SelectionChanged += async (_, _) =>
        {
            if (!loading && viewModel is not null && LotteryList.SelectedItem is LotteryHistoryRow item)
                await viewModel.Lottery.RefreshLotteryHistoryAsync(item.Id);
        };
        ReturnButton.Click += (_, _) => { if (ReturnButton.IsEnabled) ReturnRequested?.Invoke(); };
        RetryButton.Click += async (_, _) => { if (viewModel is not null) await viewModel.Lottery.RefreshLotteryHistoryAsync(); };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.Lottery.PropertyChanged -= Changed;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.Lottery.PropertyChanged += Changed;
        Refresh();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LotteryViewModel.SelectedLotteryHistory) or nameof(LotteryViewModel.LotteryHistoryLoading)) Refresh();
    }

    private void Refresh()
    {
        if (viewModel is null || LotteryList is null || viewModel.Lottery.LotteryHistoryLoading) return;
        loading = true;
        LotteryList.SelectedItem = viewModel.Lottery.LotteryHistoryRows.FirstOrDefault(row => row.Id == viewModel.Lottery.SelectedLotteryHistory?.Id);
        loading = false;
    }
}
