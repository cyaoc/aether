using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class FollowLatestTests : IDisposable
{
    private readonly GuiHarness h = new();

    private static void Flush()
    {
        // The window defers scrolling through nested Loaded-priority posts, so settle a few rounds.
        for (var i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private (MainWindow Window, MainWindowViewModel ViewModel) ShowWindow()
    {
        var viewModel = h.CreateViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Flush();
        return (window, viewModel);
    }

    private static ScrollViewer Viewer(MainWindow window) =>
        window.DanmakuList.GetVisualDescendants().OfType<ScrollViewer>().First();

    private static bool AtBottom(ScrollViewer viewer) =>
        viewer.Offset.Y >= viewer.Extent.Height - viewer.Viewport.Height - 1;

    private static void Wheel(MainWindow window, double deltaY)
    {
        var list = window.DanmakuList;
        var center = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), window)!.Value;
        window.MouseWheel(center, new Vector(0, deltaY));
        Flush();
    }

    private void Receive(MainWindowViewModel viewModel, int count)
    {
        h.Receive(viewModel, count);
        Flush();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(1100)]
    public Task New_danmaku_scroll_to_the_latest_while_at_the_bottom(int count) => HeadlessApp.Session.Dispatch(() =>
    {
        var (window, viewModel) = ShowWindow();
        Receive(viewModel, count);

        var viewer = Viewer(window);
        Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
        Assert.True(AtBottom(viewer));
        Assert.NotNull(window.DanmakuList.ContainerFromIndex(viewModel.Danmaku.Count - 1));
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(200)]
    [InlineData(1000)]
    public Task Scrolling_up_pauses_following_until_the_user_returns_to_the_bottom(int count) => HeadlessApp.Session.Dispatch(() =>
    {
        var (window, viewModel) = ShowWindow();
        Receive(viewModel, count);
        var viewer = Viewer(window);

        Wheel(window, 5);
        Assert.False(AtBottom(viewer));
        Receive(viewModel, 20);
        Assert.False(AtBottom(viewer));

        Wheel(window, -10_000);
        Assert.True(AtBottom(viewer));
        Receive(viewModel, 20);
        Assert.True(AtBottom(viewer));
        Assert.NotNull(window.DanmakuList.ContainerFromIndex(viewModel.Danmaku.Count - 1));
    }, TestContext.Current.CancellationToken);

    public void Dispose() => h.Dispose();
}
