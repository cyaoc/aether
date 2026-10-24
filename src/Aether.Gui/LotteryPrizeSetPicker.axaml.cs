using Avalonia.Controls;

namespace Aether.Gui;

public partial class LotteryPrizeSetPicker : UserControl
{
    private PrizeSetPickerViewModel? ViewModel => DataContext as PrizeSetPickerViewModel;
    private MenuFlyout LoadFlyout => (MenuFlyout)LoadButton.Flyout!;
    private Flyout SaveFlyout => (Flyout)SaveButton.Flyout!;

    public LotteryPrizeSetPicker()
    {
        InitializeComponent();
        LoadFlyout.Opening += (_, _) => LoadFlyout.HorizontalOffset = SaveButton.Bounds.Width + 6;
        LoadFlyout.Opened += (_, _) => LoadButton.Classes.Add("open");
        LoadFlyout.Closed += (_, _) => LoadButton.Classes.Remove("open");
        SaveFlyout.Opened += (_, _) => { SaveButton.Classes.Add("open"); ViewModel?.StartDraft(); };
        SaveFlyout.Closed += (_, _) => SaveButton.Classes.Remove("open");
        CancelButton.Click += (_, _) => SaveFlyout.Hide();
        ConfirmButton.Click += async (_, _) =>
        {
            if (ViewModel is { } viewModel && await viewModel.SaveAsync()) SaveFlyout.Hide();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _ = ViewModel?.RefreshAsync();
    }
}
