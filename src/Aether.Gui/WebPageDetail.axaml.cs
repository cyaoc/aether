using Avalonia.Controls;

namespace Aether.Gui;

/// <summary>The web page picked in the general settings' page list: its state, its address and the browser source size to give it. The data
/// context is the page's <see cref="WebPageStatus"/>.</summary>
public partial class WebPageDetail : UserControl
{
    public WebPageDetail() => InitializeComponent();
}
