using Avalonia.Controls;
using Avalonia.Media;

namespace Aether.Gui;

/// <summary>A web page's icon, its name and whether streaming software is connected: an item of the general settings' page list,
/// and the heading of the page's detail. The data context is the page's <see cref="WebPageStatus"/>.</summary>
public partial class WebPageSummary : UserControl
{
    public WebPageSummary() => InitializeComponent();

    public Geometry? Icon { get => IconPath.Data; set => IconPath.Data = value; }
}
