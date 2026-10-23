using Aether.Core;
using System.Net;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
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

    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "aether-gui-tests-" + Guid.NewGuid());
    public string SettingsFile => Path.Combine(DataDirectory, "gui.yml");

    public GuiSettings LoadSettings() => GuiSettings.Load(DataDirectory, NullLogger<GuiSettings>.Instance);

    public MainWindowViewModel CreateViewModel(TimeProvider? timeProvider = null) =>
        new(client, NullLogger<MainWindowViewModel>.Instance, LoadSettings(), timeProvider ?? TimeProvider.System);

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
    public List<Uri> RejectedRequests => http.RejectedRequests;

    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    /// <summary>A nav response for the account that <see cref="SaveCredentialAsync"/> saves (UID 123).</summary>
    public static HttpResponseMessage AccountResponse(string nickname = "测试账号") =>
        Json($$$"""{"code":0,"data":{"isLogin":true,"mid":123,"uname":"{{{nickname}}}"}}""");

    public async Task SaveCredentialAsync()
    {
        // Seed the real store so tests unrelated to login can enter the main window.
        await client.GetAccountAsync();
        using var connection = new SqliteConnection($"Data Source={Path.Combine(DataDirectory, "aether.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO credential (id, cookies, refresh_token, saved_at, checked_at)
            VALUES (1, '{"SESSDATA":"test-session","bili_jct":"test-csrf","DedeUserID":"123"}', 'test-token', $savedAt, $savedAt)
            """;
        command.Parameters.AddWithValue("$savedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    /// <summary>Holds room_init until cancelled, so a connection stays at "connecting"; completes with the request.</summary>
    public Task<Uri> HoldRoomRequest()
    {
        var requested = new TaskCompletionSource<Uri>();
        RoomResponse = async (uri, token) =>
        {
            requested.TrySetResult(uri);
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        return requested.Task;
    }

    public void SaveRecentRooms(params string[] rooms)
    {
        var settings = LoadSettings();
        settings.RecentRooms = rooms;
        settings.Save();
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

    public static T FindVisual<T>(Visual parent, string name) where T : Control =>
        parent.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    public static T FindRowControl<T>(Visual parent, object row, string name) where T : Control
    {
        // A folded container has not been measured yet, so its template may not exist.
        foreach (var presenter in parent.GetVisualDescendants().OfType<ContentPresenter>()
            .Where(presenter => ReferenceEquals(presenter.Content, row)).ToArray())
            presenter.UpdateChild();
        return parent.GetVisualDescendants().OfType<T>()
            .Single(control => ReferenceEquals(control.DataContext, row) && control.Name == name);
    }

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

    public static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
            Dispatcher.UIThread.RunJobs();
        }
    }

    public void Dispose()
    {
        client.Dispose();
        if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
        Assert.Empty(http.RejectedRequests);
    }

    private sealed class GuiHttp : HttpMessageHandler
    {
        public Func<CancellationToken, Task<HttpResponseMessage>> NavResponse { get; set; } =
            _ => Task.FromResult(AccountResponse());
        public int NavRequests;
        public Func<Uri, CancellationToken, Task<HttpResponseMessage>>? LoginResponse { get; set; }
        public Func<Uri, CancellationToken, Task<HttpResponseMessage>>? RoomResponse { get; set; }
        public List<Uri> RejectedRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
