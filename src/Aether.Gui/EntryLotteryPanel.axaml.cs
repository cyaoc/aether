using Avalonia.Controls;

namespace Aether.Gui;

public partial class EntryLotteryPanel : UserControl
{
    private MainWindowViewModel? viewModel;

    public EntryLotteryPanel()
    {
        InitializeComponent();
        AddAwardButton.Click += (_, _) => { if (AddAwardButton.IsEnabled) viewModel?.Lottery.AddAward(); };
        StartEntryButton.Click += async (_, _) => { if (StartEntryButton.IsEnabled && viewModel is not null) await viewModel.Lottery.StartEntryAsync(); };
        CloseEntriesButton.Click += async (_, _) => { if (CloseEntriesButton.IsEnabled && viewModel is not null) await viewModel.Lottery.CloseEntriesAsync(); };
        EndEntryButton.Click += (_, _) => { if (EndEntryButton.IsEnabled) viewModel?.Lottery.RequestEndEntry(); };
        CancelEndButton.Click += (_, _) => { if (CancelEndButton.IsEnabled) viewModel?.Lottery.CancelEndEntry(); };
        ConfirmEndButton.Click += async (_, _) => { if (ConfirmEndButton.IsEnabled && viewModel is not null) await viewModel.Lottery.ConfirmEndEntryAsync(); };
        VisibilityButton.Click += async (_, _) => { if (VisibilityButton.IsEnabled && viewModel is not null) await viewModel.Lottery.ToggleEntryHiddenAsync(); };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        viewModel = DataContext as MainWindowViewModel;
    }
}
