using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Styling;

namespace Aether.Gui;

/// <summary>Nickname, current theme and account marker determine a local, request-free avatar.</summary>
public sealed class AvatarBrushConverter : IMultiValueConverter
{
    public static AvatarBrushConverter Instance { get; } = new();
    public static FuncValueConverter<string?, string> Initial { get; } =
        new(name => string.IsNullOrEmpty(name) ? "" : StringInfo.GetNextTextElement(name));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count != 3 || values[0] is not string name || values[1] is not ThemeVariant theme)
            return AvaloniaProperty.UnsetValue;

        // String.GetHashCode is randomized per process; keep a nickname's color stable across launches.
        uint hash = 0;
        foreach (var character in name) hash = unchecked(hash * 31 + character);
        var key = values[2] is true
            ? parameter is "Foreground" ? "Accent.OnSolid" : "Accent"
            : $"Avatar.{hash % 5}.{parameter}";
        return Application.Current!.TryGetResource(key, theme, out var brush) ? brush : AvaloniaProperty.UnsetValue;
    }
}
