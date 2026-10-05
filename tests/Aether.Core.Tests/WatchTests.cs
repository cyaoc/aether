using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Core.Tests;

public sealed class WatchTests
{
    [Fact]
    public async Task Danmaku_stream_uses_local_receive_time_and_emoticon_text_and_ends_normally_on_cancellation()
    {
        await using var h = new WatchHarness();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        var next = updates.MoveNextAsync().AsTask();
        await h.Server.PushAsync(FakeDanmakuServer.Packet(5,
            System.Text.Encoding.UTF8.GetBytes("""{"cmd":"DANMU_MSG","info":[[],"[dog]你好",[0,"观***"]]}"""), 0));
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(new Danmaku(h.Time.GetUtcNow(), "观***", "[dog]你好"), updates.Current);
        next = updates.MoveNextAsync().AsTask();
        await h.Stop.CancelAsync();
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Short_room_id_authenticates_anonymously_with_real_room_and_signed_request()
    {
        await using var h = new WatchHarness();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connected>(updates.Current);
        var packet = await h.Server.NextRequestAsync();
        Assert.Equal(7, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(8)));
        using var auth = System.Text.Json.JsonDocument.Parse(packet.AsMemory(16));
        var root = auth.RootElement;
        Assert.Equal(0, root.GetProperty("uid").GetInt64());
        Assert.Equal(7734200, root.GetProperty("roomid").GetInt64());
        Assert.Equal(3, root.GetProperty("protover").GetInt32());
        Assert.Equal("web", root.GetProperty("platform").GetString());
        Assert.Equal(2, root.GetProperty("type").GetInt32());
        Assert.Equal("anonymous-token", root.GetProperty("key").GetString());
        Assert.Equal("anonymous-buvid", root.GetProperty("buvid").GetString());
        Assert.Equal(new Uri("wss://danmaku.example/sub"), h.Server.ConnectedUri);
        Assert.Equal("?id=6", h.Http.Requests[0].Uri.Query);
        var request = Assert.Single(h.Http.Requests, r => r.Uri.AbsolutePath.EndsWith("getDanmuInfo"));
        Assert.Equal("buvid3=anonymous-buvid", request.Cookie);
        Assert.Equal("?id=7734200&type=0&web_location=444.8&wts=1702204169&w_rid=1bb9dceebb99493b57a534797732eba7", request.Uri.Query);
    }

    [Fact]
    public async Task Nonexistent_room_ends_with_clear_error_without_connecting_or_retrying()
    {
        var http = new FakeBilibiliHttp();
        http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] =
            """{"code":60004,"message":"直播间不存在"}""";
        using var client = new AetherClient(http, (_, _) => throw new Xunit.Sdk.XunitException("Must not connect"),
            new FakeTimeProvider(), NullLogger<AetherClient>.Instance);
        await using var updates = client.WatchAsync(999, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await updates.MoveNextAsync());
        Assert.Contains("直播间不存在", error.Message);
        Assert.Single(http.Requests);
    }
}
