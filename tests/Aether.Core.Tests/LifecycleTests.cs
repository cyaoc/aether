using System.Buffers.Binary;
using System.Net.WebSockets;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Aborted_socket_reconnects_unless_caller_cancelled(bool cancel)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var server = new FakeDanmakuServer();
        await using var h = new WatchHarness(async (uri, token) =>
        {
            var socket = await server.ConnectAsync(uri, token);
            if (cancel) await stop.CancelAsync();
            socket.Abort();
            return socket;
        });
        await h.LoginAsync();
        await using var updates = h.Client.WatchAsync(6, stop.Token).GetAsyncEnumerator(stop.Token);
        Assert.True(await updates.MoveNextAsync());
        if (cancel) Assert.False(await updates.MoveNextAsync());
        else
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
        }
        await server.WaitForDisconnectAsync();
    }

    [Fact]
    public async Task Cancellation_discards_buffered_danmaku_and_closes_connection()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var packet = FakeDanmakuServer.Packet(5, System.Text.Encoding.UTF8.GetBytes(
            """{"cmd":"DANMU_MSG","info":[[],"你好",[0,"观***"]]}"""), 0);
        await h.Server.PushAsync([.. packet, .. packet]);
        Assert.True(await updates.MoveNextAsync());
        await h.Stop.CancelAsync();
        Assert.False(await updates.MoveNextAsync());
        await h.Server.WaitForDisconnectAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_authentication_never_reports_connected(bool invalidPacket)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        if (invalidPacket)
            h.Http.Responses["https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo"] =
                """{"code":0,"data":{"token":"","host_list":[{"host":"danmaku.example","wss_port":443}]}}""";
        else h.Server.AuthenticationCode = -101;
        await using var updates = h.Watch();
        Assert.True(await updates.MoveNextAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await updates.MoveNextAsync());
        Assert.Contains("认证失败", error.Message);
        await h.Server.WaitForDisconnectAsync();
    }

    [Fact]
    public async Task Server_disconnect_reports_reconnecting()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.Server.WaitForDisconnectAsync();
    }

    [Fact]
    public async Task Disposing_stream_closes_connection_even_without_cancelling_token()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using (var updates = h.Watch())
        {
            await updates.MoveNextAsync();
            await updates.MoveNextAsync();
        }
        await h.Server.WaitForDisconnectAsync();
        Assert.True(h.Server.Disconnected);
    }

    [Fact]
    public async Task Heartbeats_are_sent_every_thirty_seconds_and_stop_on_cancellation()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.Server.NextRequestAsync(); // Authentication.
        var next = updates.MoveNextAsync().AsTask();
        for (var i = 0; i < 2; i++)
        {
            h.Time.Advance(TimeSpan.FromSeconds(29));
            Assert.False(await h.Server.ReceivesRequestWithinAsync(TimeSpan.FromMilliseconds(200)));
            h.Time.Advance(TimeSpan.FromSeconds(1));
            var heartbeat = await h.Server.NextRequestAsync();
            Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(heartbeat.AsSpan(8)));
        }
        await h.Stop.CancelAsync();
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await h.Server.WaitForDisconnectAsync();
        h.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(await h.Server.ReceivesRequestWithinAsync(TimeSpan.FromMilliseconds(200)));
    }
}
