using Avalonia.Controls;

namespace Aether.Gui;

public partial class GiftBoardPanel : UserControl
{
    public event Action? GeneralSettingsRequested;

    public GiftBoardPanel()
    {
        InitializeComponent();
        PageStatus.GeneralSettingsRequested += () => GeneralSettingsRequested?.Invoke();
        EndButton.Click += (_, _) => Board?.AskEnd();
        ContinueButton.Click += async (_, _) => { if (Board is { } board) await board.ContinueAsync(); };
        NewPeriodButton.Click += async (_, _) => { if (DataContext is MainWindowViewModel view) await view.GiftBoard.AskNewPeriodAsync(view.RoomNumber); };
        CancelConfirmButton.Click += (_, _) => Board?.CancelConfirmation();
        ConfirmEndButton.Click += async (_, _) => await ConfirmAsync();
        ConfirmNewPeriodButton.Click += async (_, _) => await ConfirmAsync();
    }

    private async Task ConfirmAsync()
    {
        if (DataContext is MainWindowViewModel view) await view.GiftBoard.ConfirmAsync(view.RoomNumber);
    }

    private GiftBoardViewModel? Board => (DataContext as MainWindowViewModel)?.GiftBoard;
}
