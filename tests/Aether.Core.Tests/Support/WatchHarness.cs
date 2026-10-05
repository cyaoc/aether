using Microsoft.Extensions.Time.Testing;

namespace Aether.Core.Tests.Support;

internal sealed class WatchHarness : IAsyncDisposable
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "aether-tests-" + Guid.NewGuid());
    public FakeBilibiliHttp Http { get; } = new();
    public FakeDanmakuServer Server { get; } = new();
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.FromUnixTimeSeconds(1702204169));
    public RecordingLogger Logger { get; } = new();
    public CancellationTokenSource Stop { get; } = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    public AetherClient Client { get; }

    public WatchHarness(Func<Uri, CancellationToken, Task<System.Net.WebSockets.WebSocket>>? connectWebSocket = null)
    {
        Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] =
            """{"code":0,"data":{"room_id":7734200}}""";
        Http.Responses["https://api.bilibili.com/x/frontend/finger/spi"] =
            """{"code":0,"data":{"b_3":"anonymous-buvid"}}""";
        Http.Responses["https://api.bilibili.com/x/web-interface/nav"] =
            """{"code":-101,"data":{"wbi_img":{"img_url":"https://i0.hdslb.com/bfs/wbi/7cd084941338484aae1ad9425b84077c.png","sub_url":"https://i0.hdslb.com/bfs/wbi/4932caff0ff746eab6f01bf08b70ac45.png"}}}""";
        Http.Responses["https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo"] =
            """{"code":0,"data":{"token":"anonymous-token","host_list":[{"host":"danmaku.example","wss_port":443}]}}""";
        Client = new AetherClient(Http, connectWebSocket ?? Server.ConnectAsync, Time, Logger, DataDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        await Stop.CancelAsync();
        Client.Dispose();
        await Server.DisposeAsync();
        Stop.Dispose();
    }
}
