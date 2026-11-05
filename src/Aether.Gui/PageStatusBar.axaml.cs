using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Aether.Gui;
public partial class PageStatusBar : UserControl
{
    public event Action? GeneralSettingsRequested;
    public PageStatusBar() => InitializeComponent();
    private void OpenGeneralSettings(object? sender, RoutedEventArgs e) => GeneralSettingsRequested?.Invoke();
}
