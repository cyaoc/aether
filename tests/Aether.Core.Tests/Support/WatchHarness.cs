using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Core.Tests.Support;

internal sealed class WatchHarness : IAsyncDisposable
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "aether-tests-" + Guid.NewGuid());
    public FakeBilibiliHttp Http { get; } = new();
    public FakeDanmakuServer Server { get; } = new();
    // LoginAsync advances 2s, so watching after it signs with the captured wts=1702204169.
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.FromUnixTimeSeconds(1702204167));
    public RecordingLogger Logger { get; } = new();
    public CancellationTokenSource Stop { get; } = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    public AetherClient Client { get; }

    public WatchHarness(Func<Uri, CancellationToken, Task<System.Net.WebSockets.WebSocket>>? connectWebSocket = null)
    {
        Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] =
            """{"code":0,"data":{"room_id":7734200}}""";
        Http.ConfigureLogin();
        Http.Responses["https://api.bilibili.com/x/web-interface/nav"] =
            """{"code":0,"data":{"isLogin":true,"mid":9876543210,"wbi_img":{"img_url":"https://i0.hdslb.com/bfs/wbi/7cd084941338484aae1ad9425b84077c.png","sub_url":"https://i0.hdslb.com/bfs/wbi/4932caff0ff746eab6f01bf08b70ac45.png"}}}""";
        Http.Responses["https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo"] =
            """{"code":0,"data":{"token":"room-token","host_list":[{"host":"danmaku.example","wss_port":443}]}}""";
        Client = new AetherClient(Http, connectWebSocket ?? Server.ConnectAsync, Time, Logger, DataDirectory);
    }

    public async Task LoginAsync()
    {
        await using var updates = Client.LoginAsync(Stop.Token).GetAsyncEnumerator(Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<LoginQrCode>(updates.Current);
        Assert.True(await AdvancePollAsync(updates));
        Assert.IsType<LoggedIn>(updates.Current);
        Assert.False(await updates.MoveNextAsync());
        Http.Requests.Clear();
    }

    public async Task<bool> AdvancePollAsync<T>(IAsyncEnumerator<T> updates)
    {
        var next = updates.MoveNextAsync().AsTask();
        Time.Advance(TimeSpan.FromSeconds(2));
        return await next.WaitAsync(TimeSpan.FromSeconds(5), Stop.Token);
    }

    /// <summary>Proves the next update waits exactly <paramref name="seconds"/> of reconnect backoff.</summary>
    public async Task AdvanceRetryAsync<T>(IAsyncEnumerator<T> updates, int seconds)
    {
        var next = updates.MoveNextAsync().AsTask();
        try
        {
            Time.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1));
            Assert.False(next.IsCompleted);
            Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), Stop.Token));
        }
        finally
        {
            if (!next.IsCompleted)
            {
                await Stop.CancelAsync();
                await next;
            }
        }
    }

    public (Dictionary<string, string> Cookies, string RefreshToken, DateTimeOffset SavedAt) SavedCredential()
    {
        using var connection = TestDatabase.Open(DataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cookies, refresh_token, saved_at FROM credential";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        var credential = (JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(0))!,
            reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture));
        Assert.False(reader.Read());
        return credential;
    }

    public async ValueTask DisposeAsync()
    {
        await Stop.CancelAsync();
        Client.Dispose();
        await Server.DisposeAsync();
        Stop.Dispose();
        if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
    }
}
