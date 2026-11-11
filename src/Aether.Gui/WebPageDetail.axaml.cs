using Avalonia.Controls;
using Avalonia.Media;

namespace Aether.Gui;

/// <summary>The web page picked in the general settings' page list: its state, its address and the browser source size to give it. The data
/// context is the page's <see cref="WebPageStatus"/>; the icon, what the page is for and its size never change, so the page sets them.</summary>
public partial class WebPageDetail : UserControl
{
    public WebPageDetail() => InitializeComponent();

    public Geometry? Icon { get => Summary.Icon; set => Summary.Icon = value; }
    public string? Purpose { get => PurposeText.Text; set => PurposeText.Text = value; }
    public string? Size { get => PageSize.Text; set => PageSize.Text = value; }
}
