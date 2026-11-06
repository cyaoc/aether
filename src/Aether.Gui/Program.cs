using Avalonia;
using Avalonia.Media;

namespace Aether.Gui;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        // ADR 0015: opened while another GUI installs an update here, this one would run a mix of old and new files, so it quits and
        // the other opens the new version. It finds the install still holding its lock, or, reaching here only after the install,
        // its own assembly older than the one on disk.
        AutoUpdater.IsInstalling(AppContext.BaseDirectory) || AutoUpdater.IsOutdated(AppContext.BaseDirectory)
            ? 0 : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

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
