using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Aether.Gui;
public partial class VideoPageStatusBar : UserControl
{
    public event Action? GeneralSettingsRequested;
    public VideoPageStatusBar() => InitializeComponent();
    private void OpenGeneralSettings(object? sender, RoutedEventArgs e) => GeneralSettingsRequested?.Invoke();
}
