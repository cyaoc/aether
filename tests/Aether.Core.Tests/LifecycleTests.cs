using System.Buffers.Binary;
using System.Net.WebSockets;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Aborted_socket_ends_normally_only_when_caller_cancelled(bool cancel)
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
        await using var updates = h.Client.WatchAsync(6, stop.Token).GetAsyncEnumerator(stop.Token);
        Assert.True(await updates.MoveNextAsync());
        if (cancel) Assert.False(await updates.MoveNextAsync());
        else await Assert.ThrowsAsync<WebSocketException>(async () => await updates.MoveNextAsync());
        await server.WaitForDisconnectAsync();
    }

    [Fact]
    public async Task Cancellation_discards_buffered_danmaku_and_closes_connection()
    {
        await using var h = new WatchHarness();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        var packet = FakeDanmakuServer.Packet(5, System.Text.Encoding.UTF8.GetBytes(
            """{"cmd":"DANMU_MSG","info":[[],"你好",[0,"观***"]]}"""), 0);
        await h.Server.PushAsync([.. packet, .. packet]);
        Assert.True(await updates.MoveNextAsync());
        await h.Stop.CancelAsync();
        Assert.False(await updates.MoveNextAsync());
        await h.Server.WaitForDisconnectAsync();
    }

    [Fact]
    public async Task Rejected_authentication_never_reports_connected()
    {
        await using var h = new WatchHarness();
        h.Server.AuthenticationCode = -101;
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await updates.MoveNextAsync());
        Assert.Contains("认证失败", error.Message);
        await h.Server.WaitForDisconnectAsync();
    }

    [Fact]
    public async Task Server_disconnect_ends_with_error()
    {
        await using var h = new WatchHarness();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        await h.Server.DisconnectAsync();
        var error = await Assert.ThrowsAsync<IOException>(async () => await updates.MoveNextAsync());
        Assert.Contains("断开", error.Message);
        await h.Server.WaitForDisconnectAsync();
    }

    [Fact]
    public async Task Disposing_stream_closes_connection_even_without_cancelling_token()
    {
        await using var h = new WatchHarness();
        await using (var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token))
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
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        await h.Server.NextRequestAsync(); // Authentication.
        var next = updates.MoveNextAsync().AsTask();
        for (var i = 0; i < 2; i++)
        {
            h.Time.Advance(TimeSpan.FromSeconds(29));
            Assert.False(h.Server.HasPendingRequest);
            h.Time.Advance(TimeSpan.FromSeconds(1));
            var heartbeat = await h.Server.NextRequestAsync();
            Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(heartbeat.AsSpan(8)));
        }
        await h.Stop.CancelAsync();
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await h.Server.WaitForDisconnectAsync();
        h.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(h.Server.HasPendingRequest);
    }
}
