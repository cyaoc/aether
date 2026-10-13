using Avalonia;
using Avalonia.Headless;

namespace Aether.Gui.Tests.Support;

/// <summary>Avalonia.Headless.XUnit targets xUnit v3 3.x, so UI tests dispatch onto the session themselves.</summary>
internal static class HeadlessApp
{
    public static HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));

    /// <summary>Where VisualReviewTests saves screenshots; null skips them.</summary>
    // Computed rather than stored: BuildAvaloniaApp reads it on the session thread while Session's initializer is
    // still running, and touching a stored static field there would wait for that initializer forever.
    public static string? ScreenshotDirectory =>
        Environment.GetEnvironmentVariable("AETHER_SCREENSHOT_DIR") is { Length: > 0 } directory ? directory : null;

    // Only screenshots need Skia; the other tests keep headless drawing so they don't depend on the machine's fonts.
    public static AppBuilder BuildAvaloniaApp() =>
        Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = ScreenshotDirectory is null });
}
