using Aether.Core;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Gui.Tests.Support;

/// <summary>The 礼物榜 section at the minimum window size, over a data directory of its own, with the 礼物榜 of the harness's
/// client behind it. Connecting and giving go straight to Core, the way a room connection would.</summary>
internal sealed class GiftBoardScene : IAsyncDisposable
{
    public const long RoomId = 1768506153;

    /// <summary>Settings that turn the board on at a threshold of 1 电池.</summary>
    public static readonly (string Name, string Value)[] On = [("gift_board.enabled", "true"), ("gift_board.threshold", "1")];

    /// <summary>When the scene's clock starts (3 November 2026, local time), and when <see cref="GoToToday"/> sets it.</summary>
    public static readonly DateTimeOffset Yesterday = new(new DateTime(2026, 11, 3, 20, 1, 0, DateTimeKind.Local));
    public static readonly DateTimeOffset Today = new(new DateTime(2026, 11, 4, 20, 3, 0, DateTimeKind.Local));

    private readonly GuiHarness harness;
    private readonly Window window;

    private GiftBoardScene(GuiHarness harness, FakeTimeProvider clock, MainWindowViewModel viewModel, GiftBoardSettingsPage page, Window window)
    {
        this.harness = harness;
        Clock = clock;
        ViewModel = viewModel;
        Page = page;
        this.window = window;
    }

    public FakeTimeProvider Clock { get; }
    public MainWindowViewModel ViewModel { get; }
    public GiftBoardSettingsPage Page { get; }
    public GiftBoardController Board => harness.Client.GiftBoard;
    public string DataDirectory => harness.DataDirectory;

    /// <summary>Runs <paramref name="check"/> on the UI thread over a scene whose saved settings are the defaults, but for <paramref name="settings"/>.</summary>
    public static Task Run(Func<GiftBoardScene, Task> check, params (string Name, string Value)[] settings) => HeadlessApp.Session.Dispatch(async () =>
    {
        await using var scene = Open(settings);
        await check(scene);
        return true;
    }, TestContext.Current.CancellationToken);

    private static GiftBoardScene Open((string Name, string Value)[] settings)
    {
        var clock = new FakeTimeProvider(Yesterday);
        var harness = new GuiHarness(clock);
        harness.SaveSettings(settings);
        harness.Client.GiftBoard.Apply(Settings.Load(harness.DataDirectory));
        var viewModel = harness.CreateViewModel(clock);
        var page = new GiftBoardSettingsPage(harness.DataDirectory) { DataContext = viewModel };
        var window = new Window { Content = page, Width = 1040, Height = 600 };
        window.Show();
        viewModel.GiftBoard.Observe();
        return new(harness, clock, viewModel, page, window);
    }

    /// <summary>A paid gift: <paramref name="batteries"/> is the price of one, and a giftId stays the same for the same name.</summary>
    public static Gift PaidGift(long uid, string nickname, string giftName, long batteries, long num = 1) =>
        new(uid, nickname, batteries * 100 * num, null) { GiftId = giftName.GetHashCode(), GiftName = giftName, Price = batteries * 100, Num = num };

    public void GoToToday() => Clock.SetUtcNow(Today);

    /// <summary>The GUI and the board both connect to <see cref="RoomId"/>.</summary>
    public void Connect()
    {
        ViewModel.RoomNumber = RoomId.ToString();
        ViewModel.ConnectionState = RoomConnectionState.Connected;
        Board.Connect(RoomId, liveTime: null);
    }

    /// <summary>The streamer clicked 断开.</summary>
    public void Disconnect()
    {
        ViewModel.ConnectionState = RoomConnectionState.Disconnected;
        Board.EndRoom(RoomId);
    }

    public void Give(params Gift[] gifts) => Board.Receive(RoomId, gifts);

    /// <summary>What each line of the 当前榜单 says: its place, nickname, gift and value, as the eye reads them.</summary>
    public string[] Listed() => Page.FindControl<ListBox>("BoardList")!.GetVisualDescendants().OfType<ListBoxItem>()
        .Select(item => string.Join(" ", item.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text))).ToArray();

    public async ValueTask DisposeAsync()
    {
        await ViewModel.ShutdownAsync();
        window.Close();
        GuiHarness.Flush();
        harness.Dispose();
    }
}
