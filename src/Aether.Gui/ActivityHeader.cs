using Avalonia;
using Avalonia.Controls;

namespace Aether.Gui;

/// <summary>An activity record's first row: the wrapping header text, then the time on the right, on the header's first baseline.</summary>
// A Grid can only top- or center-align the time; with fallback fonts (Helvetica digits, PingFang Chinese) neither lands on the baseline.
public sealed class ActivityHeader : Panel
{
    private const double Spacing = 8;

    private TextBlock Header => (TextBlock)Children[0];
    private TextBlock Time => (TextBlock)Children[1];

    protected override Size MeasureOverride(Size availableSize)
    {
        Time.Measure(Size.Infinity);
        Header.Measure(availableSize.WithWidth(Math.Max(0, availableSize.Width - Time.DesiredSize.Width - Spacing)));
        return new Size(Header.DesiredSize.Width + Spacing + Time.DesiredSize.Width,
            Math.Max(Header.DesiredSize.Height, TimeTop() + Time.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var timeWidth = Time.DesiredSize.Width;
        Header.Arrange(new Rect(0, 0, Math.Max(0, finalSize.Width - timeWidth - Spacing), Header.DesiredSize.Height));
        Time.Arrange(new Rect(finalSize.Width - timeWidth, TimeTop(), timeWidth, Time.DesiredSize.Height));
        return finalSize;
    }

    private double TimeTop() => Math.Max(0, Header.TextLayout.Baseline - Time.TextLayout.Baseline);
}
