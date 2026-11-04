using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using static Aether.Gui.Tests.Support.GiftBoardScene;

namespace Aether.Gui.Tests;

// The 礼物榜 section and the page list of the general settings, as canvases GS1-GS10 draw them.
public sealed partial class VisualReviewTests
{
    // Canvas GS1's viewers, and the 期 before it (4 November 20:03 and 3 November 20:01).
    private static readonly Gift[] BoardToday =
    [
        PaidGift(1, "月下Lily", "次元之城", 12450), PaidGift(2, "龍騰雲海", "告白气球", 2000, 5), PaidGift(3, "さくら月影", "舰长", 1980),
        PaidGift(4, "柚子不酸", "天空之翼", 1000), PaidGift(5, "鬼滅の刃推し", "告白花束", 220, 4), PaidGift(6, "Yuzu_Sour", "花式夸夸", 330, 2),
        PaidGift(7, "夜雨听风", "星河入梦", 199, 3), PaidGift(8, "星屑Koi", "干杯", 66, 8), PaidGift(9, "白鹭", "情书", 52, 5)
    ];

    private static readonly Gift[] BoardBefore =
    [
        PaidGift(2, "龍騰雲海", "告白气球", 2000, 3), PaidGift(10, "Aki秋", "为你加冕", 4500), PaidGift(11, "南風知我意", "提督", 19980),
        PaidGift(7, "夜雨听风", "星河入梦", 199, 2)
    ];

    [Theory]
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    [InlineData(1040, 600, false)]
    [InlineData(1040, 600, true)]
    public Task Screenshots_gift_board(int width, int height, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            var before = new DateTimeOffset(new DateTime(2026, 11, 3, 20, 1, 0, DateTimeKind.Local));
            var clock = new FakeTimeProvider(before);
            using var harness = new GuiHarness(clock);
            await harness.SaveCredentialAsync();
            Settings.Save(harness.DataDirectory, new Dictionary<string, string>
            {
                ["gift_board.enabled"] = "true", ["gift_board.threshold"] = "100", ["web_service.port"] = GuiHarness.FreePort().ToString()
            });
            var viewModel = harness.CreateViewModel(clock);
            var window = new MainWindow(harness.DataDirectory)
            {
                DataContext = viewModel, Width = width, Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await viewModel.ReadAccountCommand.ExecutionTask!;
                var board = harness.Client.GiftBoard;
                board.Apply(Settings.Load(harness.DataDirectory));
                // The 3 November period first, then the 42 minutes before the new one opens.
                board.Connect(GiftBoardScene.Room, liveTime: null);
                board.Receive(GiftBoardScene.Room, BoardBefore);
                clock.SetUtcNow(new DateTimeOffset(new DateTime(2026, 11, 4, 19, 21, 0, DateTimeKind.Local)));
                viewModel.RoomNumber = GiftBoardScene.Room.ToString();
                viewModel.Apply(new Connected(GiftBoardScene.Room, 24680));
                clock.Advance(TimeSpan.FromMinutes(42));
                board.Clear();
                board.Receive(GiftBoardScene.Room, BoardToday);
                viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 2, 1, GiftBoardConnections: 1));
                window.Navigation.SelectedIndex = 4;
                await GuiHarness.WaitForAsync(() => viewModel.GiftBoard.Rows.Count == 7);
                Capture("");
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;

            void Capture(string scene) => Save(window, $"gift-board{scene}-{width}x{height}-{(dark ? "dark" : "light")}");
        }, TestContext.Current.CancellationToken);
    }
}
