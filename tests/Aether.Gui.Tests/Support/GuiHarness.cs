using Aether.Core;
using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Gui.Tests.Support;

internal sealed class GuiHarness : IDisposable
{
    /// <summary>A viewer nickname with no break opportunity, wider than any activity list.</summary>
    public const string LongEnglishNickname = "VeryLongEnglishViewerNameWithoutAnySpaces0123456789VeryLongEnglishViewerNameWithoutAnySpaces0123456789";

    private readonly AetherClient client;
    private readonly GuiHttp http = new();
    private int received;

    public GuiHarness(TimeProvider? timeProvider = null) => client = new AetherClient(http,
        (_, _) => throw new InvalidOperationException("GUI tests must not connect a WebSocket."),
        timeProvider ?? TimeProvider.System, NullLogger<AetherClient>.Instance, DataDirectory);

    public AetherClient Client => client;

    /// <summary>The navigation items that are settings pages: all but the danmaku, the 礼物榜 and the lottery.</summary>
    public static readonly int[] SettingsPages = [1, 2, 3, 5, 7, 8];
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "aether-gui-tests-" + Guid.NewGuid());
    /// <summary>Where releases before ADR 0016 kept the GUI's own settings; only the upgrade reads it.</summary>
    public string LegacyGuiFile => Path.Combine(DataDirectory, "gui.yml");

    /// <summary>What the database holds, read beside the client's own region.</summary>
    public GuiState LoadGuiState() => GuiStates.Load();

    /// <summary>Saves what an earlier run left, before a window follows the GUI state.</summary>
    public void SaveGuiState(GuiState state) => GuiStates.Save(state);

    private GuiStateStore GuiStates => new(new Database(DataDirectory));

    /// <summary>GitHub for the auto-update check, which only a release build makes; see <see cref="ReleaseVersion"/>.</summary>
    public FakeGitHub GitHub { get; } = new();
    public string ProgramDirectory => DataDirectory + "-program";
    /// <summary>The release version the window's program reports; null keeps the dev build, which never checks for updates.</summary>
    public string? ReleaseVersion { get; set; }

    public MainWindowViewModel CreateViewModel(TimeProvider? timeProvider = null, bool keepSplashTiming = false)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var updater = new AutoUpdater(new HttpClient(GitHub), new Uri(FakeGitHub.LatestRelease), ProgramDirectory, ReleaseVersion ?? "0.0.0-dev",
            "osx-arm64", true, clock, NullLogger.Instance);
        var viewModel = new MainWindowViewModel(client, NullLogger<MainWindowViewModel>.Instance, clock, updater);
        if (!keepSplashTiming)
        {
            // What the start follows behind the splash, followed at once: these tests skip the splash, and many open no window.
            viewModel.ObserveGuiState();
            viewModel.SplashMinimumDuration = TimeSpan.Zero;
            viewModel.SplashFadeDuration = TimeSpan.Zero;
            viewModel.SplashOpacity = 0;
            viewModel.IsSplashVisible = false;
        }
        return viewModel;
    }

    public Func<CancellationToken, Task<HttpResponseMessage>> NavResponse
    {
        set => http.NavResponse = value;
    }
    public int NavRequests => http.NavRequests;
    public Func<Uri, CancellationToken, Task<HttpResponseMessage>> LoginResponse
    {
        set => http.LoginResponse = value;
    }
    public Func<Uri, CancellationToken, Task<HttpResponseMessage>> RoomResponse
    {
        set => http.RoomResponse = value;
    }
    public Func<Uri, CancellationToken, Task<HttpResponseMessage>> OnlineRankResponse
    {
        set => http.OnlineRankResponse = value;
    }
    public List<Uri> RejectedRequests => http.RejectedRequests;

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>A nav response for the account that <see cref="SaveCredentialAsync"/> saves (UID 123).</summary>
    public static HttpResponseMessage AccountResponse(string nickname = "测试账号") =>
        Json($$$"""{"code":0,"data":{"isLogin":true,"mid":123,"uname":"{{{nickname}}}"}}""");

    /// <summary>The data directory's aether.db, opened beside the client's own connections.</summary>
    public SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection($"Data Source={Path.Combine(DataDirectory, "aether.db")};Pooling=False");
        connection.Open();
        return connection;
    }

    public async Task SaveCredentialAsync()
    {
        // Seed the real store so tests unrelated to login can enter the main window.
        await client.GetAccountAsync();
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO credential (id, cookies, refresh_token, saved_at, checked_at)
            VALUES (1, '{"SESSDATA":"test-session","bili_jct":"test-csrf","DedeUserID":"123"}', 'test-token', $savedAt, $savedAt)
            """;
        command.Parameters.AddWithValue("$savedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    /// <summary>A response that never arrives, so the request stays pending until cancelled.</summary>
    public static Task<HttpResponseMessage> NoResponse(CancellationToken token) =>
        new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);

    public void HoldLoginRequests() => LoginResponse = (_, token) => NoResponse(token);

    /// <summary>Holds room_init until cancelled, so a connection stays at "connecting"; completes with the request.</summary>
    public Task<Uri> HoldRoomRequest()
    {
        var requested = new TaskCompletionSource<Uri>();
        RoomResponse = (uri, token) =>
        {
            requested.TrySetResult(uri);
            return NoResponse(token);
        };
        return requested.Task;
    }

    /// <summary>Saves settings over a web service port of its own, so that saving a page does not start the service on the default one.</summary>
    public void SaveSettings(params (string Name, string Value)[] settings)
    {
        var saved = new Dictionary<string, string> { ["web_service.port"] = FreePort().ToString() };
        foreach (var (name, value) in settings) saved[name] = value;
        Settings.Save(DataDirectory, saved);
    }

    public void SaveRecentRooms(params string[] rooms) => SaveGuiState(LoadGuiState() with { RecentRooms = [.. rooms] });

    /// <summary>Makes every later write of the GUI state fail, as a damaged database would, once Core has read what it needs.</summary>
    public async Task BreakGuiStateAsync()
    {
        await client.IdleAsync();
        LoadGuiState();
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE gui_state";
        command.ExecuteNonQuery();
    }

    /// <summary>Delivers numbered danmaku the way a watch stream would; Content is the running number.</summary>
    public void Receive(MainWindowViewModel viewModel, int count)
    {
        for (var i = 0; i < count; i++)
            viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "viewer", (received++).ToString()));
    }

    public static void Flush()
    {
        // The window defers scrolling through nested Loaded-priority posts, so settle a few rounds.
        for (var i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Switches a settings page with tabs, the 桌宠 page or the general settings, to the tab at <paramref name="index"/>.</summary>
    public static void Tab(SettingsPage page, int index)
    {
        page.FindControl<ListBox>("Tabs")!.SelectedIndex = index;
        Flush();
    }

    public static T FindVisual<T>(Visual parent, string name) where T : Control =>
        parent.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    /// <summary>A control in one page's detail beside the general settings' page list; <paramref name="pageName"/> is Video, Lottery, GiftBoard or DesktopPet.
    /// Found by name, not in the visual tree: a detail not picked was never shown, so its content is not there yet.</summary>
    public static T FindPageDetailControl<T>(Visual page, string pageName, string name) where T : Control =>
        FindVisual<WebPageDetail>(page, pageName + "Detail").FindControl<T>(name)!;

    public static T FindRowControl<T>(Visual parent, object row, string name) where T : Control
    {
        // A folded container has not been measured yet, so its template may not exist.
        foreach (var presenter in parent.GetVisualDescendants().OfType<ContentPresenter>()
            .Where(presenter => ReferenceEquals(presenter.Content, row)).ToArray())
            presenter.UpdateChild();
        return parent.GetVisualDescendants().OfType<T>()
            .Single(control => ReferenceEquals(control.DataContext, row) && control.Name == name);
    }

    /// <summary>The containers an open menu made for its ItemsSource; they live in the popup's own visual tree.</summary>
    public static IEnumerable<MenuItem> MenuItems(MenuFlyout menu) =>
        menu.Popup.Child!.GetVisualDescendants().OfType<MenuItem>();

    /// <summary>Shows a navigation page and returns its visible ComboBox, scrolled into view.</summary>
    public static ComboBox VisibleComboBox(MainWindow window, int page, string name)
    {
        window.Navigation.SelectedIndex = page;
        Flush();
        var input = window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == name && x.IsEffectivelyVisible);
        input.BringIntoView();
        return input;
    }

    /// <summary>What a screen reader reads for each visible TextBlock, so text split into Runs still counts.</summary>
    public static IEnumerable<string?> VisibleText(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => new TextBlockAutomationPeer(x).GetName());

    /// <summary>An activity record's time sits on its header's first baseline, whatever font each text falls back to.</summary>
    // Layout rounding puts each text on whole pixels, so the baselines can differ by up to half a pixel.
    public static void AssertSameBaseline(TextBlock header, TextBlock time, Visual root) =>
        Assert.Equal(header.TranslatePoint(new Point(0, header.TextLayout.Baseline), root)!.Value.Y,
            time.TranslatePoint(new Point(0, time.TextLayout.Baseline), root)!.Value.Y, 0.5);

    /// <summary>Clicks the named button, with the layout settled before and after.</summary>
    public static void Click(Visual parent, string name)
    {
        Flush();
        FindVisual<Button>(parent, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Flush();
    }

    /// <summary>Clicks a settings page's 保存 and waits until the save is done.</summary>
    public static async Task SaveAsync(SettingsPage page)
    {
        Click(page, "PART_SaveButton");
        await page.PendingSave;
    }

    /// <summary>Waits for the account read under way; the window starts its first once the splash has cleaned up after the last update.</summary>
    public static async Task AccountReadAsync(MainWindowViewModel viewModel)
    {
        await WaitForAsync(() => viewModel.ReadAccountCommand.ExecutionTask is not null);
        await viewModel.ReadAccountCommand.ExecutionTask!;
    }

    /// <summary>Waits until Core has run every step queued, then lets the window show what they reported.</summary>
    public async Task IdleAsync()
    {
        await client.IdleAsync();
        Flush();
    }

    public static Task WaitForAsync(Func<bool> condition) => WaitForAsync(() => Task.FromResult(condition()));

    public static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition())
        {
            await Task.Delay(10, timeout.Token);
            Dispatcher.UIThread.RunJobs();
        }
    }

    public void Dispose()
    {
        // The shell stops the web service, and Core's steps write data/, deleted below; neither needs this thread, so waiting here
        // cannot hang.
        client.WebService.StopAsync().GetAwaiter().GetResult();
        client.IdleAsync().GetAwaiter().GetResult();
        client.Dispose();
        if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
        if (Directory.Exists(ProgramDirectory)) Directory.Delete(ProgramDirectory, recursive: true);
        Assert.Empty(http.RejectedRequests);
    }

    private sealed class GuiHttp : HttpMessageHandler
    {
        public Func<CancellationToken, Task<HttpResponseMessage>> NavResponse { get; set; } =
            _ => Task.FromResult(AccountResponse());
        public int NavRequests;
        public Func<Uri, CancellationToken, Task<HttpResponseMessage>>? LoginResponse { get; set; }
        public Func<Uri, CancellationToken, Task<HttpResponseMessage>>? RoomResponse { get; set; }
        public Func<Uri, CancellationToken, Task<HttpResponseMessage>>? OnlineRankResponse { get; set; }
        public List<Uri> RejectedRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.GetLeftPart(UriPartial.Path) ==
                "https://api.live.bilibili.com/xlive/general-interface/v1/rank/queryContributionRank" && OnlineRankResponse is { } onlineRankResponse)
                return onlineRankResponse(request.RequestUri, cancellationToken);
            if (request.Method == HttpMethod.Get && request.RequestUri is { } uri &&
                (uri.GetLeftPart(UriPartial.Path) is "https://api.bilibili.com/x/frontend/finger/spi"
                    or "https://passport.bilibili.com/x/passport-login/web/qrcode/generate"
                    or "https://passport.bilibili.com/x/passport-login/web/qrcode/poll") && LoginResponse is { } loginResponse)
            {
                return loginResponse(uri, cancellationToken);
            }
            if (request.Method == HttpMethod.Get && request.RequestUri?.GetLeftPart(UriPartial.Path) ==
                "https://api.live.bilibili.com/room/v1/Room/room_init" && RoomResponse is { } roomResponse)
                return roomResponse(request.RequestUri, cancellationToken);
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == "https://api.bilibili.com/x/web-interface/nav")
            {
                Interlocked.Increment(ref NavRequests);
                return NavResponse(cancellationToken);
            }
            RejectedRequests.Add(request.RequestUri!);
            throw new InvalidOperationException($"GUI tests must not send HTTP requests: {request.RequestUri}");
        }
    }
}
