using System.Runtime.InteropServices;
using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

// PROTOTYPE (#180), throwaway: the 开奖记录 aside drawn by LotteryPanel's C# against PoolDrawsAsidePrototype's template and bindings.
public sealed class PoolDrawsPrototypeTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 17, 20, 30, 0, TimeSpan.FromHours(8));
    private static readonly PoolPrize[] Prizes = [new("机械键盘", 1, Grade.Grand), new("周边抱枕", 3, Grade.Second),
        new("签名照", 4, Grade.Third), new("超长名字的限定版亚克力立牌套装", 2, Grade.Third)];

    private static PoolLottery Lottery(PoolLotteryPhase phase, bool draws) => LotteryPanelTests.TestLottery() with
    {
        Prizes = [.. Prizes],
        Phase = phase,
        Draws = draws
            ? [
                new("d1", Prizes[3], 42, "小明", false, At, []),
                new("d2", Prizes[1], null, null, true, At.AddMinutes(2), []),
                new("d3", Prizes[2], 42, "小明", true, At.AddMinutes(6), []),
                new("d4", Prizes[0], 7, "一个名字特别特别长的观众", false, At.AddMinutes(9), []),
                new("d5", Prizes[3], 8, null, false, At.AddMinutes(11), []),
            ]
            : [],
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Template_draws_the_same_pixels_as_the_csharp_rows(bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var h = new GuiHarness();
            var vm = h.CreateViewModel();
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var old = new Window { Content = new LotteryPanel { DataContext = vm }, Width = 976, Height = 720, RequestedThemeVariant = theme };
            var proto = new PoolDrawsPrototype();
            var next = new Window { Content = new PoolDrawsAsidePrototype { DataContext = proto }, Width = 280, Height = 660, RequestedThemeVariant = theme };
            old.Show();
            next.Show();
            var report = new List<string>();
            try
            {
                foreach (var (name, phase, draws) in new[]
                {
                    ("empty", PoolLotteryPhase.Idle, false), ("idle", PoolLotteryPhase.Idle, true),
                    ("spinning", PoolLotteryPhase.Spinning, true), ("drawing", PoolLotteryPhase.Drawing, true),
                })
                {
                    vm.Apply(new LotteryUpdate(new(7734200, true, Lottery(phase, draws))));
                    proto.State = vm.LotteryState;
                    GuiHarness.Flush();
                    using var a = old.CaptureRenderedFrame()!;
                    using var b = next.CaptureRenderedFrame()!;
                    var diff = Compare(a, new PixelPoint(696, 60), b, $"{name}-{(dark ? "dark" : "light")}");
                    report.Add($"{name}: {diff} px differ");
                }
            }
            finally { old.Close(); next.Close(); await vm.ShutdownAsync(); GuiHarness.Flush(); }
            File.WriteAllLines(Path.Combine(HeadlessApp.ScreenshotDirectory!, $"prototype-180-{(dark ? "dark" : "light")}.txt"), report);
            Assert.All(report, line => Assert.EndsWith(": 0 px differ", line));
            return true;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Saves old | new | diff side by side and returns how many pixels differ.</summary>
    private static int Compare(WriteableBitmap oldFrame, PixelPoint origin, WriteableBitmap newFrame, string name)
    {
        var size = newFrame.PixelSize;
        int[] a = new int[size.Width * size.Height], b = new int[size.Width * size.Height];
        using (var locked = oldFrame.Lock())
            for (var y = 0; y < size.Height; y++)
                Marshal.Copy(locked.Address + (origin.Y + y) * locked.RowBytes + origin.X * 4, a, y * size.Width, size.Width);
        using (var locked = newFrame.Lock())
            for (var y = 0; y < size.Height; y++)
                Marshal.Copy(locked.Address + y * locked.RowBytes, b, y * size.Width, size.Width);
        var differ = 0;
        using var sheet = new WriteableBitmap(new PixelSize(size.Width * 3, size.Height), newFrame.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var locked = sheet.Lock())
        {
            var row = new int[size.Width * 3];
            for (var y = 0; y < size.Height; y++)
            {
                for (var x = 0; x < size.Width; x++)
                {
                    var i = y * size.Width + x;
                    row[x] = a[i];
                    row[size.Width + x] = b[i];
                    var same = a[i] == b[i];
                    if (!same) differ++;
                    row[size.Width * 2 + x] = same ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFFFF0000);
                }
                Marshal.Copy(row, 0, locked.Address + y * locked.RowBytes, row.Length);
            }
        }
        sheet.Save(Path.Combine(HeadlessApp.ScreenshotDirectory!, $"prototype-180-{name}.png"), PngBitmapEncoderOptions.Default);
        return differ;
    }

    // How the existing assertions in LotteryPanelTests.Core_state_controls_buttons_and_viewer_hint read once the aside is bound.
    [Fact]
    public Task Assertions_move_from_named_controls_to_the_view_model() => HeadlessApp.Session.Dispatch(() =>
    {
        var vm = new PoolDrawsPrototype();
        var aside = new PoolDrawsAsidePrototype { DataContext = vm };
        var window = new Window { Content = aside, Width = 280, Height = 660 };
        window.Show();
        try
        {
            var draw = new PoolDraw("draw", new("限定抱枕", 1, Grade.Grand), 42, "小明", true, DateTimeOffset.UnixEpoch, []);
            vm.State = new(7734200, true, LotteryPanelTests.TestLottery(PoolLotteryPhase.Drawing) with { Draws = [draw] });
            GuiHarness.Flush();
            // Before: page.FindControl<Border>("LatestCard")!.IsVisible, FindControl<TextBlock>("LatestHead")!.Text, ...
            Assert.True(vm.ShowLatest);
            Assert.StartsWith("小明 · 主播点停 · ", vm.Latest!.Head);
            Assert.Equal("限定抱枕", vm.Latest.Prize);
            Assert.Equal("本场 1 次", vm.DrawCount);
            Assert.False(vm.IsEmpty);
            Assert.True(Assert.Single(vm.Rows).Fresh);
            // Names inside a DataTemplate live in each row's own scope, so the page can't FindControl them;
            // a test that needs the drawn class walks the realized containers instead.
            var rows = aside.FindControl<ItemsControl>("HistoryRows")!;
            var row = Assert.Single(rows.GetRealizedContainers()).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("drawRow"));
            Assert.Contains("fresh", row.Classes);
            var rowsBefore = vm.Rows;
            // A danmaku while drawing changes Viewers only: the row list stays the same object, so nothing is rebuilt.
            vm.State = vm.State with { PoolLottery = vm.State.PoolLottery! with { Viewers = vm.State.PoolLottery.Viewers.Add(9, "路人") } };
            Assert.Same(rowsBefore, vm.Rows);
            vm.State = vm.State with { PoolLottery = vm.State.PoolLottery! with { Phase = PoolLotteryPhase.Idle } };
            GuiHarness.Flush();
            Assert.False(Assert.Single(vm.Rows).Fresh);
            row = Assert.Single(rows.GetRealizedContainers()).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("drawRow"));
            Assert.DoesNotContain("fresh", row.Classes);
        }
        finally { window.Close(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
