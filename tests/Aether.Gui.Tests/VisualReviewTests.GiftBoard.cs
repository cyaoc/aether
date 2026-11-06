using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using static Aether.Gui.Tests.Support.GiftBoardScene;

namespace Aether.Gui.Tests;

// The 礼物榜 section and the page list of the general settings, as canvases GS1–GS13 draw them.
public sealed partial class VisualReviewTests
{
    // Canvas GS1's viewers on the board from 4 November 20:03; 星屑Koi and 白鹭 stay under its threshold of 100 电池.
    private static readonly Gift[] BoardToday =
    [
        PaidGift(1, "月下Lily", "次元之城", 12450), PaidGift(2, "龍騰雲海", "告白气球", 2000, 5), PaidGift(3, "さくら月影", "舰长", 1980),
        PaidGift(4, "柚子不酸", "天空之翼", 1000), PaidGift(5, "鬼滅の刃推し", "告白花束", 220, 4), PaidGift(6, "Yuzu_Sour", "花式夸夸", 330, 2),
        PaidGift(7, "夜雨听风", "星河入梦", 199, 3), PaidGift(8, "星屑Koi", "干杯", 66, 8), PaidGift(9, "白鹭", "情书", 52, 5)
    ];

    [Theory]
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    [InlineData(1040, 600, false)]
    [InlineData(1040, 600, true)]
    public Task Screenshots_gift_board(int width, int height, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        var look = new Look(width, height, dark);
        return HeadlessApp.Session.Dispatch(async () =>
        {
            await ShootBoardAsync(look, StartPeriodAsync(options => options), ShootPeriodAsync);
            await ShootBoardAsync(look, StartPeriodAsync(options => options with { ThresholdBy = GiftBoardOptions.ByTotalPrice }), Capturing("gift-board-total"));
            await ShootBoardAsync(look, ConnectAsync, Capturing("gift-board-not-started"));
            await ShootBoardAsync(look, _ => Task.CompletedTask, Capturing("gift-board-offline"));
            // The page list of the general settings, with the 礼物榜页 connected and not.
            await ShootBoardAsync(look, StartPeriodAsync(options => options), ShootPageListAsync);
            return true;
        }, TestContext.Current.CancellationToken);
    }

    // Recording, then the question 结束这一期 asks, then the 期 ended at 22:41 and the question a new 期 asks after it.
    private static async Task ShootPeriodAsync(BoardShot shoot)
    {
        shoot.Capture("gift-board-connected");
        shoot.ViewModel.WebService.Apply(new WebServiceStatus(true, 17890, 2, 1, GiftBoardConnections: 0));
        shoot.Capture("gift-board-page-waiting");
        shoot.ViewModel.WebService.Apply(new WebServiceStatus(true, 17890, 2, 1, GiftBoardConnections: 1));
        GuiHarness.Click(shoot.Page, "EndButton");
        shoot.Capture("gift-board-end-confirm");
        GuiHarness.Click(shoot.Page, "CancelConfirmButton");
        shoot.Stage.Clock.SetUtcNow(new DateTimeOffset(new DateTime(2026, 11, 4, 22, 41, 0, DateTimeKind.Local)));
        shoot.Stage.Harness.Client.GiftBoard.EndPeriod(RoomId);
        await GuiHarness.WaitForAsync(() => shoot.ViewModel.GiftBoard.Ended);
        shoot.Capture("gift-board-ended");
        GuiHarness.Click(shoot.Page, "NewPeriodButton");
        shoot.Capture("gift-board-new-period-confirm");
    }

    private static Task ShootPageListAsync(BoardShot shoot)
    {
        shoot.Window.Navigation.SelectedIndex = 7;
        shoot.Capture("general-gift-board-pages");
        shoot.ViewModel.WebService.Apply(new WebServiceStatus(true, 17890, 2, 1, GiftBoardConnections: 0));
        shoot.Capture("general-gift-board-waiting");
        return Task.CompletedTask;
    }

    private static Func<BoardShot, Task> Capturing(string scene) => shoot =>
    {
        shoot.Capture(scene);
        return Task.CompletedTask;
    };

    private readonly record struct Look(int Width, int Height, bool Dark)
    {
        public string Suffix => $"{Width}x{Height}-{(Dark ? "dark" : "light")}";
    }

    private sealed record BoardShot(MainWindow Window, MainWindowViewModel ViewModel, GiftBoardPanel Page, BoardStage Stage, Look Look)
    {
        public void Capture(string scene) => Save(Window, $"{scene}-{Look.Suffix}");
    }

    /// <summary>What a board scene is set up with: the harness and its clock, the room request the window holds, and the window's view model.</summary>
    private sealed record BoardStage(GuiHarness Harness, FakeTimeProvider Clock, Task RoomStarted, MainWindowViewModel ViewModel);

    /// <summary>42 minutes of connection to the room up to 4 November 20:03, before any 期 (canvas GS13).</summary>
    private static async Task ConnectAsync(BoardStage stage)
    {
        var (harness, clock, roomStarted, viewModel) = stage;
        ConnectBoard(harness, RoomId);
        clock.SetUtcNow(new DateTimeOffset(new DateTime(2026, 11, 4, 19, 21, 0, DateTimeKind.Local)));
        viewModel.RoomNumber = RoomId.ToString();
        viewModel.ConnectCommand.Execute(null);
        await roomStarted.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Apply(new Connected(RoomId, 24680));
        clock.Advance(TimeSpan.FromMinutes(42));
        await GuiHarness.WaitForAsync(() => viewModel.GiftBoard.NotStarted);
    }

    /// <summary>The 期 of canvas GS1, started at 20:03 at a threshold of 100 电池 with <paramref name="options"/> changed, and its viewers.</summary>
    private static Func<BoardStage, Task> StartPeriodAsync(Func<GiftBoardOptions, GiftBoardOptions> options) => async stage =>
    {
        await ConnectAsync(stage);
        var board = stage.Harness.Client.GiftBoard;
        board.SetOptions(RoomId, options(GiftBoardOptions.Default));
        board.StartNewPeriod(RoomId);
        Give(stage.Harness, RoomId, BoardToday);
        var rows = (await board.CurrentAsync()).Rows.Length;
        await GuiHarness.WaitForAsync(() => stage.ViewModel.GiftBoard.Rows.Count == rows);
    };

    /// <summary>The 礼物榜 section in a main window, after <paramref name="prepare"/> has put the board and the room in the state to shoot.</summary>
    private static async Task ShootBoardAsync(Look look, Func<BoardStage, Task> prepare, Func<BoardShot, Task> shoot)
    {
        var clock = new FakeTimeProvider(Yesterday);
        using var harness = new GuiHarness(clock);
        await harness.SaveCredentialAsync();
        harness.SaveSettings();
        var roomStarted = harness.HoldRoomRequest();
        var viewModel = harness.CreateViewModel(clock);
        var window = new MainWindow(harness.DataDirectory)
        {
            DataContext = viewModel, Width = look.Width, Height = look.Height,
            RequestedThemeVariant = look.Dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            await GuiHarness.AccountReadAsync(viewModel);
            var stage = new BoardStage(harness, clock, roomStarted, viewModel);
            await prepare(stage);
            viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 2, 1, GiftBoardConnections: 1));
            window.Navigation.SelectedIndex = 4;
            GuiHarness.Flush();
            await shoot(new(window, viewModel, window.GetVisualDescendants().OfType<GiftBoardPanel>().Single(), stage, look));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
    }
}
