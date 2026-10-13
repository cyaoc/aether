using Avalonia;
using Avalonia.Media;

namespace Aether.Gui;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new FontManagerOptions
        {
            FontFallbacks =
            [
                new FontFallback { FontFamily = new FontFamily("PingFang SC") },
                new FontFallback { FontFamily = new FontFamily("Microsoft YaHei UI") }
            ]
        }).LogToTrace();
}
