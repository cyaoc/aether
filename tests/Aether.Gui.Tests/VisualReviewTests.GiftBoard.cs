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
    // Canvas GS1's viewers on the board from 4 November 20:03, and the viewers of the 期 before it (3 November 20:01).
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
        var look = new Look(width, height, dark);
        return HeadlessApp.Session.Dispatch(async () =>
        {
            await ShootConnectedBoardAsync(look, [], ShootConfirmationsAsync);
            await ShootConnectedBoardAsync(look, [("gift_board.threshold_by", "2")], Capturing("gift-board-total"));
            await ShootOfflineBoardAsync(look, [], Capturing("gift-board-offline"));
            // The page list of the general settings, with the 礼物榜页 connected and not.
            await ShootConnectedBoardAsync(look, [], ShootPageListAsync);
            return true;
        }, TestContext.Current.CancellationToken);
    }

    private static async Task ShootConfirmationsAsync(BoardShot shoot)
    {
        shoot.Capture("gift-board-connected");
        shoot.ViewModel.WebService.Apply(new WebServiceStatus(true, 17890, 2, 1, GiftBoardConnections: 0));
        shoot.Capture("gift-board-page-waiting");
        shoot.ViewModel.WebService.Apply(new WebServiceStatus(true, 17890, 2, 1, GiftBoardConnections: 1));
        GuiHarness.Click(shoot.Page, "AskClearButton");
        shoot.Capture("gift-board-clear-confirm");
        GuiHarness.Click(shoot.Page, "CancelConfirmButton");
        GuiHarness.Click(shoot.Page, "AskRestoreButton");
        shoot.Capture("gift-board-restore-confirm");
        GuiHarness.Click(shoot.Page, "CancelConfirmButton");
        GuiHarness.Click(shoot.Page, "AskRestoreButton");
        GuiHarness.Click(shoot.Page, "ConfirmRestoreButton");
        await GuiHarness.WaitForAsync(() => shoot.ViewModel.GiftBoard.Rows.Count == 9
            && GuiHarness.FindVisual<TextBlock>(shoot.Page, "Message").Text == "已恢复上一期");
        shoot.Capture("gift-board-restored");
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

    private sealed record BoardShot(MainWindow Window, MainWindowViewModel ViewModel, GiftBoardSettingsPage Page, Look Look)
    {
        public void Capture(string scene) => Save(Window, $"{scene}-{Look.Suffix}");
    }

    /// <summary>What a board scene is set up with: the harness and its clock, the room request the window holds, and the window's view model.</summary>
    private sealed record BoardStage(GuiHarness Harness, FakeTimeProvider Clock, Task RoomStarted, MainWindowViewModel ViewModel);

    private static Task ShootConnectedBoardAsync(Look look, (string Name, string Value)[] settings, Func<BoardShot, Task> shoot) =>
        ShootBoardAsync(look, settings, ConnectAsync, shoot);

    private static Task ShootOfflineBoardAsync(Look look, (string Name, string Value)[] settings, Func<BoardShot, Task> shoot) =>
        ShootBoardAsync(look, settings, _ => Task.CompletedTask, shoot);

    /// <summary>The earlier 期 first, then 42 minutes of connection before the board of the day (canvas GS1).</summary>
    private static async Task ConnectAsync(BoardStage stage)
    {
        var (harness, clock, roomStarted, viewModel) = stage;
        var board = harness.Client.GiftBoard;
        board.Connect(RoomId, liveTime: null);
        board.Receive(RoomId, BoardBefore);
        clock.SetUtcNow(new DateTimeOffset(new DateTime(2026, 11, 4, 19, 21, 0, DateTimeKind.Local)));
        viewModel.RoomNumber = RoomId.ToString();
        viewModel.ConnectCommand.Execute(null);
        await roomStarted.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Apply(new Connected(RoomId, 24680));
        clock.Advance(TimeSpan.FromMinutes(42));
        board.Clear();
        board.Receive(RoomId, BoardToday);
        await GuiHarness.WaitForAsync(() => viewModel.GiftBoard.Period is { Viewers: 9 });
    }

    /// <summary>The 礼物榜 section of canvas GS1 in a main window, after <paramref name="prepare"/> has put the board and the room in the state to shoot.</summary>
    private static async Task ShootBoardAsync(Look look, (string Name, string Value)[] settings, Func<BoardStage, Task> prepare, Func<BoardShot, Task> shoot)
    {
        var clock = new FakeTimeProvider(Yesterday);
        using var harness = new GuiHarness(clock);
        await harness.SaveCredentialAsync();
        harness.SaveSettings([("gift_board.enabled", "true"), ("gift_board.threshold", "100"), .. settings]);
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
            await viewModel.ReadAccountCommand.ExecutionTask!;
            harness.Client.GiftBoard.Apply(Settings.Load(harness.DataDirectory));
            await prepare(new(harness, clock, roomStarted, viewModel));
            viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 2, 1, GiftBoardConnections: 1));
            window.Navigation.SelectedIndex = 4;
            GuiHarness.Flush();
            await shoot(new(window, viewModel, window.GetVisualDescendants().OfType<GiftBoardSettingsPage>().Single(), look));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
    }
}
