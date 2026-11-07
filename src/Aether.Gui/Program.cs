using Aether.Core;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace Aether.Gui;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // ADR 0018: before anything writes data/. Main stays synchronous for [STAThread]; nothing here has a SynchronizationContext yet.
        using var dataLock = DataDirectory.LockAsync(DataDirectory.Locate(), TimeProvider.System).GetAwaiter().GetResult();
        if (dataLock is null) return ShowAlreadyRunning();
        // ADR 0015: one that waited above while the GUI before it installed an update here loaded old files, so it quits and the
        // restarted new version opens. An installer from before ADR 0018 holds no data/ lock, and is found by its install lock.
        return AutoUpdater.IsInstalling(AppContext.BaseDirectory) || AutoUpdater.IsOutdated(AppContext.BaseDirectory)
            ? 0 : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new FontManagerOptions
        {
            FontFallbacks =
            [
                new FontFallback { FontFamily = new FontFamily("PingFang SC") },
                new FontFallback { FontFamily = new FontFamily("Microsoft YaHei UI") }
            ]
        }).LogToTrace();

    /// <summary>Without the desktop lifetime App starts nothing, neither the log nor the settings; only the prompt runs, until it closes.</summary>
    private static int ShowAlreadyRunning()
    {
        BuildAvaloniaApp().SetupWithoutStarting();
        if (OperatingSystem.IsMacOS()) MacOSDockIcon.Apply();
        using var closed = new CancellationTokenSource();
        var prompt = PromptWindow.AlreadyRunning();
        prompt.Closed += (_, _) => closed.Cancel();
        prompt.Show();
        Dispatcher.UIThread.MainLoop(closed.Token);
        return 1;
    }
}
