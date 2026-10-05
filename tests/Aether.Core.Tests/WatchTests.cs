using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class WatchTests
{
    [Fact]
    public async Task Danmaku_stream_uses_local_receive_time_and_emoticon_text_and_ends_normally_on_cancellation()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
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
    public async Task Valid_credential_skips_qr_and_uses_nav_mid_saved_cookies_and_signed_real_room()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Http.Responses["https://api.bilibili.com/x/frontend/finger/spi"] =
            """{"code":0,"data":{"b_3":"must-not-replace-saved-buvid"}}""";
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connected>(updates.Current);
        var packet = await h.Server.NextRequestAsync();
        Assert.Equal(7, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(8)));
        using var auth = System.Text.Json.JsonDocument.Parse(packet.AsMemory(16));
        var root = auth.RootElement;
        Assert.Equal(9876543210, root.GetProperty("uid").GetInt64());
        Assert.Equal(7734200, root.GetProperty("roomid").GetInt64());
        Assert.Equal(3, root.GetProperty("protover").GetInt32());
        Assert.Equal("web", root.GetProperty("platform").GetString());
        Assert.Equal(2, root.GetProperty("type").GetInt32());
        Assert.Equal("room-token", root.GetProperty("key").GetString());
        Assert.Equal("saved-buvid", root.GetProperty("buvid").GetString());
        Assert.Equal(new Uri("wss://danmaku.example/sub"), h.Server.ConnectedUri);
        Assert.Equal("/x/web-interface/nav", h.Http.Requests[0].Uri.AbsolutePath);
        Assert.Equal("?id=6", Assert.Single(h.Http.Requests, r => r.Uri.AbsolutePath.EndsWith("room_init")).Uri.Query);
        var request = Assert.Single(h.Http.Requests, r => r.Uri.AbsolutePath.EndsWith("getDanmuInfo"));
        Assert.All(h.Http.Requests, r =>
        {
            Assert.Contains("SESSDATA=saved-session", r.Cookie);
            Assert.Contains("bili_jct=saved-csrf", r.Cookie);
            Assert.Contains("DedeUserID=123", r.Cookie);
            Assert.Contains("buvid3=saved-buvid", r.Cookie);
            Assert.Contains("sid=extra-cookie", r.Cookie);
        });
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.AbsolutePath.EndsWith("/spi"));
        Assert.Equal("?id=7734200&type=0&web_location=444.8&wts=1702204169&w_rid=1bb9dceebb99493b57a534797732eba7", request.Uri.Query);
    }

    [Fact]
    public async Task Nonexistent_room_ends_with_clear_error_without_connecting_or_retrying()
    {
        await using var h = new WatchHarness((_, _) => throw new Xunit.Sdk.XunitException("Must not connect"));
        await h.LoginAsync();
        h.Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] =
            """{"code":60004,"message":"直播间不存在"}""";
        await using var updates = h.Client.WatchAsync(999, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await updates.MoveNextAsync());
        Assert.Contains("直播间 999 查询失败", error.Message);
        Assert.Contains("直播间不存在", error.Message);
        Assert.Equal(new[] { "/x/web-interface/nav", "/room/v1/Room/room_init" }, h.Http.Requests.Select(r => r.Uri.AbsolutePath));
    }
}
