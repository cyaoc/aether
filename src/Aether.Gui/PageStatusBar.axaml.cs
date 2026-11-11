using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Aether.Gui;
public partial class PageStatusBar : UserControl
{
    public event Action<WebPageTexts>? GeneralSettingsRequested;
    public PageStatusBar() => InitializeComponent();
    private void OpenGeneralSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WebPageStatus page) GeneralSettingsRequested?.Invoke(page.Texts);
    }
}
