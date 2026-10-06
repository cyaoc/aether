using Avalonia;
using Avalonia.Headless;

namespace Aether.Gui.Tests.Support;

/// <summary>Avalonia.Headless.XUnit targets xUnit v3 3.x, so UI tests dispatch onto the session themselves.</summary>
internal static class HeadlessApp
{
    public static HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
