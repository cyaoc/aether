using Aether.Core;
using System.Net;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Gui.Tests.Support;

internal sealed class GuiHarness : IDisposable
{
    private readonly AetherClient client;
    private readonly GuiHttp http = new();
    private int received;

    public GuiHarness() => client = new AetherClient(http,
        (_, _) => throw new InvalidOperationException("GUI tests must not connect a WebSocket."),
        TimeProvider.System, NullLogger<AetherClient>.Instance, DataDirectory);

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
        // Initialize the real store, then seed a saved credential without permitting login HTTP in GUI tests.
        await client.GetAccountAsync();
        using var connection = new SqliteConnection($"Data Source={Path.Combine(DataDirectory, "aether.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO credential (id, cookies, refresh_token, saved_at)
            VALUES (1, '{"SESSDATA":"test-session","bili_jct":"test-csrf","DedeUserID":"123"}', 'test-token', $savedAt)
            """;
        command.Parameters.AddWithValue("$savedAt", DateTimeOffset.UtcNow.ToString("O"));
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

    public void Dispose()
    {
        client.Dispose();
        if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
        Assert.Empty(http.RejectedRequests);
    }

    private sealed class GuiHttp : HttpMessageHandler
    {
        public Func<CancellationToken, Task<HttpResponseMessage>> NavResponse { get; set; } =
            _ => Task.FromResult(Json("""{"code":-101}"""));
        public int NavRequests;
        public Func<Uri, CancellationToken, Task<HttpResponseMessage>>? RoomResponse { get; set; }
        public List<Uri> RejectedRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
