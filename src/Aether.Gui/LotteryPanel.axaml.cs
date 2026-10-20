using System.ComponentModel;
using Aether.Core;
using Avalonia.Controls;

namespace Aether.Gui;

public partial class LotteryPanel : UserControl
{
    private MainWindowViewModel? viewModel;

    public LotteryPanel()
    {
        InitializeComponent();
        ViewTabs.SelectionChanged += (_, _) =>
        {
            Refresh();
            if (ViewTabs.SelectedIndex == 1 && viewModel is not null) _ = viewModel.Lottery.RefreshLotteryHistoryAsync();
        };
        HistoryContent.ReturnRequested += () => ViewTabs.SelectedIndex = 0;
        AddButton.Click += (_, _) => { if (AddButton.IsEnabled) viewModel?.Lottery.AddPrize(); };
        StartButton.Click += async (_, _) => { if (StartButton.IsEnabled) await ChangeAsync(lottery => lottery.StartPoolAsync()); };
        NewButton.Click += async (_, _) =>
        {
            if (!NewButton.IsEnabled || TopLevel.GetTopLevel(this) is not Window owner) return;
            var mode = await new NewLotteryWindow().ShowDialog<LotteryMode?>(owner);
            if (mode is { } selected)
            {
                await ChangeAsync(lottery => lottery.NewLotteryAsync(selected));
                ViewTabs.SelectedIndex = 0;
            }
        };
        StopButton.Click += async (_, _) => { if (StopButton.IsEnabled) await ChangeAsync(lottery => lottery.StopPoolAsync()); };
        CancelButton.Click += async (_, _) => { if (CancelButton.IsEnabled) await ChangeAsync(lottery => lottery.CancelPoolAsync()); };
        Refresh();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null) viewModel.Lottery.PropertyChanged -= StateChanged;
        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null) viewModel.Lottery.PropertyChanged += StateChanged;
        Refresh();
        if (viewModel is not null) _ = viewModel.Lottery.RefreshLotteryHistoryAsync();
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LotteryViewModel.LotteryState) or nameof(LotteryViewModel.LotteryHistory)) Refresh();
    }

    private void Refresh()
    {
        if (PrizeRows is null) return;
        var state = viewModel?.Lottery.LotteryState;
        var entry = state?.Lottery as EntryLottery;
        var history = ViewTabs.SelectedIndex == 1;
        HistoryContent.IsVisible = history;
        EntryContent.IsVisible = !history && entry is not null;
        PoolContent.IsVisible = !history && entry is null;
        ModeChip.IsVisible = PhaseChip.IsVisible = !history;
        HistoryTabText.Text = $"历史 {viewModel?.Lottery.LotteryHistory.Count ?? 0}";
    }

    private async Task ChangeAsync(Func<LotteryViewModel, Task> change)
    {
        if (viewModel is null || viewModel.Lottery.Busy) return;
        viewModel.Lottery.PoolLotteryError = null;
        try { await change(viewModel.Lottery); }
        catch (Exception error)
        {
            if (EntryContent.IsVisible) viewModel.Lottery.EntryLotteryError = error.Message;
            else viewModel.Lottery.PoolLotteryError = error.Message;
        }
    }
}
