using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aether.Gui;

/// <summary>The line icons in Theme.axaml, for code that picks one by its key.</summary>
internal static class Icons
{
    public static Geometry Find(string key) => (Geometry)Application.Current!.FindResource(key)!;
}
