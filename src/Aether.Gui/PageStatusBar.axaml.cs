using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Aether.Gui;
public partial class PageStatusBar : UserControl
{
    /// <summary>Bubbles up from the bar's link to the main window, which opens the general settings with <see cref="Page"/> picked.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> GeneralSettingsRequestedEvent =
        RoutedEvent.Register<PageStatusBar, RoutedEventArgs>("GeneralSettingsRequested", RoutingStrategies.Bubble);
    public PageStatusBar() => InitializeComponent();
    public WebPageTexts Page => ((WebPageStatus)DataContext!).Texts;
    private void OpenGeneralSettings(object? sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(GeneralSettingsRequestedEvent));
}
