using Aether.Core;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Gui.Tests.Support;

/// <summary>The 礼物榜 section at the minimum window size, over a data directory of its own, with the 礼物榜 of the harness's
/// client behind it. Connecting and giving go straight to Core, the way a room connection would.</summary>
internal sealed class GiftBoardScene : IAsyncDisposable
{
    public const long RoomId = 1768506153;

    /// <summary>When the scene's clock starts (3 November 2026, local time), and when <see cref="GoToToday"/> sets it.</summary>
    public static readonly DateTimeOffset Yesterday = new(new DateTime(2026, 11, 3, 20, 1, 0, DateTimeKind.Local));
    public static readonly DateTimeOffset Today = new(new DateTime(2026, 11, 4, 20, 3, 0, DateTimeKind.Local));

    private readonly GuiHarness harness;
    private readonly Window window;

    private GiftBoardScene(GuiHarness harness, FakeTimeProvider clock, MainWindowViewModel viewModel, GiftBoardPanel page, Window window)
    {
        this.harness = harness;
        Clock = clock;
        ViewModel = viewModel;
        Page = page;
        this.window = window;
    }

    public FakeTimeProvider Clock { get; }
    public MainWindowViewModel ViewModel { get; }
    public GiftBoardPanel Page { get; }
    public GiftBoardFacade Board => harness.Client.GiftBoard;
    public string DataDirectory => harness.DataDirectory;

    /// <summary>Runs <paramref name="check"/> on the UI thread over a scene with no room connected.</summary>
    public static Task Run(Func<GiftBoardScene, Task> check) => HeadlessApp.Session.Dispatch(async () =>
    {
        await using var scene = Open();
        await check(scene);
        return true;
    }, TestContext.Current.CancellationToken);

    private static GiftBoardScene Open()
    {
        var clock = new FakeTimeProvider(Yesterday);
        var harness = new GuiHarness(clock);
        var viewModel = harness.CreateViewModel(clock);
        var page = new GiftBoardPanel { DataContext = viewModel };
        var window = new Window { Content = page, Width = 1040, Height = 600 };
        window.Show();
        viewModel.GiftBoard.Observe();
        return new(harness, clock, viewModel, page, window);
    }

    /// <summary>A paid gift: <paramref name="batteries"/> is the price of one, and a giftId stays the same for the same name. It
    /// brings an avatar, so the board does not ask B站 for one.</summary>
    public static Gift PaidGift(long uid, string nickname, string giftName, long batteries, long num = 1) =>
        new(uid, nickname, Battery.ToCoins(batteries) * num)
        {
            GiftId = giftName.GetHashCode(), GiftName = giftName, Price = Battery.ToCoins(batteries), Num = num, Value = Battery.ToCoins(batteries) * num,
            FaceUrl = $"https://i0.hdslb.com/bfs/face/test-{uid}.jpg"
        };

    /// <summary>Connects the board of <paramref name="harness"/>'s client to <paramref name="room"/>, the way a room connection would.</summary>
    public static void ConnectBoard(GuiHarness harness, long room) => harness.Client.GiftBoard.Connect(room);

    /// <summary>Puts gifts sent in <paramref name="room"/> on the board and records them through the 收礼记录, the way a room
    /// connection with the 盲盒统计 off would.</summary>
    public static async Task GiveAsync(GuiHarness harness, long room, IReadOnlyList<Gift> gifts)
    {
        var periodOf = await harness.Client.GiftBoard.TakeAsync(room, gifts);
        new GiftRecording(new GiftStore(new Database(harness.DataDirectory), NullLogger.Instance), NullLogger.Instance, room, blindBoxes: false)
            .Record(periodOf, write => { foreach (var gift in gifts) write(gift); });
    }

    public void GoToToday() => Clock.SetUtcNow(Today);

    /// <summary>The GUI and the board both connect to <see cref="RoomId"/>.</summary>
    public void Connect()
    {
        ViewModel.RoomNumber = RoomId.ToString();
        ViewModel.ConnectionState = RoomConnectionState.Connected;
        ConnectBoard(harness, RoomId);
    }

    /// <summary>Starts a 期 in the connected room at <paramref name="threshold"/> 电池, as the streamer would in the panel.</summary>
    public async Task StartAsync(int threshold = 1)
    {
        await Board.SetOptionsAsync(RoomId, GiftBoardOptions.Default with { Threshold = threshold });
        await Board.StartNewPeriodAsync(RoomId);
    }

    /// <summary>The streamer clicked 断开.</summary>
    public void Disconnect()
    {
        ViewModel.ConnectionState = RoomConnectionState.Disconnected;
        Board.EndRoom(RoomId);
    }

    public Task GiveAsync(params Gift[] gifts) => GiveAsync(harness, RoomId, gifts);

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
