using Microsoft.Extensions.Time.Testing;

namespace Aether.Core.Tests.Support;

internal sealed class WatchHarness : IAsyncDisposable
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "aether-tests-" + Guid.NewGuid());
    public FakeBilibiliHttp Http { get; } = new();
    public FakeDanmakuServer Server { get; } = new();
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.FromUnixTimeSeconds(1702204167));
    public RecordingLogger Logger { get; } = new();
    public CancellationTokenSource Stop { get; } = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    public AetherClient Client { get; }

    public WatchHarness(Func<Uri, CancellationToken, Task<System.Net.WebSockets.WebSocket>>? connectWebSocket = null)
    {
        Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] =
            """{"code":0,"data":{"room_id":7734200}}""";
        Http.Responses["https://api.bilibili.com/x/frontend/finger/spi"] =
            """{"code":0,"data":{"b_3":"saved-buvid"}}""";
        Http.Responses["https://api.bilibili.com/x/web-interface/nav"] =
            """{"code":0,"data":{"isLogin":true,"mid":9876543210,"wbi_img":{"img_url":"https://i0.hdslb.com/bfs/wbi/7cd084941338484aae1ad9425b84077c.png","sub_url":"https://i0.hdslb.com/bfs/wbi/4932caff0ff746eab6f01bf08b70ac45.png"}}}""";
        Http.Responses["https://passport.bilibili.com/x/passport-login/web/qrcode/generate"] =
            """{"code":0,"data":{"url":"https://example.test/scan","qrcode_key":"test-key"}}""";
        Http.Responses["https://passport.bilibili.com/x/passport-login/web/qrcode/poll"] =
            """{"code":0,"data":{"code":0,"refresh_token":"refresh-token"}}""";
        Http.Responses["https://passport.bilibili.com/login/exit/v2"] = """{"code":0}""";
        Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.GetLeftPart(UriPartial.Path);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new StringContent(Http.Responses[path]) };
            if (path.EndsWith("/qrcode/poll", StringComparison.Ordinal))
                response.Headers.Add("Set-Cookie", new[]
                {
                    "SESSDATA=saved-session; Path=/; Domain=.bilibili.com",
                    "bili_jct=saved-csrf; Path=/; Domain=.bilibili.com",
                    "DedeUserID=123; Path=/; Domain=.bilibili.com",
                    "sid=extra-cookie; Path=/; Domain=.bilibili.com"
                });
            return Task.FromResult(response);
        };
        Http.Responses["https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo"] =
            """{"code":0,"data":{"token":"anonymous-token","host_list":[{"host":"danmaku.example","wss_port":443}]}}""";
        Client = new AetherClient(Http, connectWebSocket ?? Server.ConnectAsync, Time, Logger, DataDirectory);
    }

    public async Task LoginAsync()
    {
        await using var updates = Client.LoginAsync(Stop.Token).GetAsyncEnumerator(Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<LoginQrCode>(updates.Current);
        var next = updates.MoveNextAsync().AsTask();
        Time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), Stop.Token));
        Assert.IsType<LoggedIn>(updates.Current);
        Assert.False(await updates.MoveNextAsync());
        Http.Requests.Clear();
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
