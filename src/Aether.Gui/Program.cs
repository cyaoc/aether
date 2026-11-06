using Avalonia;
using Avalonia.Media;

namespace Aether.Gui;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        // ADR 0015: opened while another GUI installs an update here, it would run a mix of old and new files.
        AutoUpdater.IsInstalling(AppContext.BaseDirectory) ? 0 : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

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
