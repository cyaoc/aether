using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class FollowLatestTests : IDisposable
{
    private readonly GuiHarness harness = new();

    private async Task<(MainWindow Window, MainWindowViewModel ViewModel)> ShowWindowAsync()
    {
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        GuiHarness.Flush();
        return (window, viewModel);
    }

    private static ScrollViewer Viewer(MainWindow window) =>
        window.DanmakuList.GetVisualDescendants().OfType<ScrollViewer>().First();

    private static bool LatestDanmakuIsVisible(MainWindow window)
    {
        var list = window.DanmakuList;
        var latest = list.ContainerFromIndex(list.ItemCount - 1);
        if (latest is null) return false;
        var viewport = Viewer(window).GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        var topLeft = latest.TranslatePoint(default, viewport)!.Value;
        return new Rect(viewport.Bounds.Size).Contains(new Rect(topLeft, latest.Bounds.Size));
    }

    private static void Wheel(MainWindow window, double deltaY)
    {
        var list = window.DanmakuList;
        var center = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), window)!.Value;
        window.MouseWheel(center, new Vector(0, deltaY));
        GuiHarness.Flush();
    }

    private void Receive(MainWindowViewModel viewModel, int count)
    {
        harness.Receive(viewModel, count);
        GuiHarness.Flush();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(1100)]
    public Task New_danmaku_scroll_to_the_latest_while_at_the_bottom(int count) => HeadlessApp.Session.Dispatch(async () =>
    {
        var (window, viewModel) = await ShowWindowAsync();
        try
        {
            Receive(viewModel, count);
            var viewer = Viewer(window);
            Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
            Assert.True(LatestDanmakuIsVisible(window));
            Assert.Equal(viewer.Extent.Height - viewer.Viewport.Height, viewer.Offset.Y, precision: 1);
            var latest = window.DanmakuList.ContainerFromIndex(window.DanmakuList.ItemCount - 1)!;
            var viewport = viewer.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
            var bottom = latest.TranslatePoint(new Point(0, latest.Bounds.Height), viewport)!.Value.Y;
            Assert.True(viewport.Bounds.Height - bottom >= 12, $"Bottom space: {viewport.Bounds.Height - bottom}");
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(200)]
    [InlineData(1000)]
    public Task Scrolling_up_pauses_following_until_the_user_returns_to_the_bottom(int count) => HeadlessApp.Session.Dispatch(async () =>
    {
        var (window, viewModel) = await ShowWindowAsync();
        try
        {
            Receive(viewModel, count);
            Wheel(window, 5);
            Assert.False(LatestDanmakuIsVisible(window));
            Receive(viewModel, 20);
            Assert.False(LatestDanmakuIsVisible(window));

            Wheel(window, -10_000);
            Assert.True(LatestDanmakuIsVisible(window));
            Receive(viewModel, 20);
            Assert.True(LatestDanmakuIsVisible(window));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(200)]
    [InlineData(1100)]
    public Task Following_reaches_the_padded_bottom_with_variable_height_rows_and_resizing(int count) => HeadlessApp.Session.Dispatch(async () =>
    {
        var (window, viewModel) = await ShowWindowAsync();
        try
        {
            window.Width = 760;
            window.Height = 520;
            for (var i = 0; i < count; i++)
                viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "观众", new string('长', 1 + i % 7 * 25)));
            AssertBottom();
            viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "观众", "最新\n另一行"));
            AssertBottom();
            window.Width = 960;
            window.Height = 640;
            AssertBottom();
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;

        void AssertBottom()
        {
            GuiHarness.Flush();
            var viewer = Viewer(window);
            Assert.Equal(viewer.Extent.Height - viewer.Viewport.Height, viewer.Offset.Y, precision: 1);
            Assert.True(LatestDanmakuIsVisible(window));
            var latest = window.DanmakuList.ContainerFromIndex(window.DanmakuList.ItemCount - 1)!;
            var viewport = viewer.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
            Assert.True(viewport.Bounds.Height - latest.TranslatePoint(new Point(0, latest.Bounds.Height), viewport)!.Value.Y >= 12);
        }
    }, TestContext.Current.CancellationToken);

    public void Dispose() => harness.Dispose();
}
