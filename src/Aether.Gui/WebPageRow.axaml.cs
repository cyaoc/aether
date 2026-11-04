using Avalonia.Controls;
using Avalonia.Media;

namespace Aether.Gui;

/// <summary>A web page's row in the general settings' page list: its state, its address and the browser source size to give it. The data
/// context is the page's <see cref="WebPageStatus"/>; the icon, what the page is for and its size never change, so the list sets them.</summary>
public partial class WebPageRow : UserControl
{
    public WebPageRow() => InitializeComponent();

    public Geometry? Icon { get => IconPath.Data; set => IconPath.Data = value; }
    public string? Purpose { get => PurposeText.Text; set => PurposeText.Text = value; }
    public string? Size { get => (string?)PageSize.Content; set => PageSize.Content = value; }
}
