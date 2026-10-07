using System.Text;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class RoomLockTests
{
    [Theory]
    [InlineData(6, false)]
    [InlineData(7734200, false)]
    [InlineData(6, true)]
    [InlineData(7734200, true)]
    public async Task Same_room_conflicts_before_login_and_leaves_first_connection_running(long secondRoomId, bool loggedIn)
    {
        await using var h = new WatchHarness();
        if (loggedIn) await h.LoginAsync();
        await using var first = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await first.MoveNextAsync());
        if (loggedIn)
        {
            Assert.True(await first.MoveNextAsync());
            Assert.IsType<Connected>(first.Current);
        }
        else Assert.IsType<WatchQrCode>(first.Current);

        var http = new FakeBilibiliHttp { Respond = h.Http.Respond };
        using var other = h.ClientSharingData(
            (_, _) => throw new Xunit.Sdk.XunitException("Conflicting connection must not open a socket"), http);
        await using var second = other.WatchAsync(secondRoomId, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await second.MoveNextAsync());
        Assert.Contains("直播间 7734200 已有直播间连接", error.Message);
        Assert.Equal("/room/v1/Room/room_init", Assert.Single(http.Requests).Uri.AbsolutePath);
        Assert.False(await second.MoveNextAsync());
        // The lock deletes itself on close; a refused open must not take the owner's file with it.
        Assert.True(File.Exists(Path.Combine(h.DataDirectory, "room-7734200.lock")));
        await using var third = other.WatchAsync(secondRoomId, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await third.MoveNextAsync());

        if (loggedIn)
        {
            await h.Server.PushAsync(FakeDanmakuServer.Packet(5,
                Encoding.UTF8.GetBytes("""{"cmd":"DANMU_MSG","info":[[],"仍在接收",[0,"观众"]]}"""), 0));
            Assert.True(await first.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.Equal("仍在接收", Assert.IsType<Danmaku>(first.Current).Content);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Ending_the_stream_releases_the_lock_whether_connected_or_waiting_for_qr(bool loggedIn, bool cancel)
    {
        await using var h = new WatchHarness();
        if (loggedIn) await h.LoginAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(h.Stop.Token);
        await using (var first = h.Client.WatchAsync(6, stop.Token).GetAsyncEnumerator(stop.Token))
        {
            Assert.True(await first.MoveNextAsync());
            if (loggedIn)
            {
                Assert.True(await first.MoveNextAsync());
                Assert.IsType<Connected>(first.Current);
            }
            else Assert.IsType<WatchQrCode>(first.Current);
            if (cancel)
            {
                var next = first.MoveNextAsync().AsTask();
                await stop.CancelAsync();
                Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
                // Assert release on cancellation before DisposeAsync runs.
                using var contender = h.ClientSharingData(h.Server.ConnectAsync);
                await using var probe = contender.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
                Assert.True(await probe.MoveNextAsync());
            }
        }
        Assert.False(File.Exists(Path.Combine(h.DataDirectory, "room-7734200.lock")));

        await using var secondServer = new FakeDanmakuServer();
        using var other = h.ClientSharingData(secondServer.ConnectAsync);
        await using var second = other.WatchAsync(7734200, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await second.MoveNextAsync());
        if (!loggedIn)
        {
            Assert.IsType<WatchQrCode>(second.Current);
            Assert.True(await h.AdvancePollAsync(second));
        }
        Assert.IsType<Connecting>(second.Current);
        Assert.True(await second.MoveNextAsync());
        Assert.IsType<Connected>(second.Current);
    }

    [Fact]
    public async Task Fatal_error_releases_the_lock_before_the_stream_is_disposed()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Server.AuthenticationCode = -101;
        await using var first = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await first.MoveNextAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await first.MoveNextAsync());

        await using var secondServer = new FakeDanmakuServer();
        using var other = h.ClientSharingData(secondServer.ConnectAsync);
        await using var second = other.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await second.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        Assert.IsType<Connected>(second.Current);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Different_rooms_or_data_directories_can_connect_independently(bool sharedDirectory)
    {
        await using var h = new WatchHarness();
        await using var secondHarness = new WatchHarness();
        await h.LoginAsync();
        await secondHarness.LoginAsync();
        var secondRoomId = sharedDirectory ? 7734201 : 7734200;
        secondHarness.Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] =
            $$$"""{"code":0,"data":{"room_id":{{{secondRoomId}}}}}""";
        using var other = new AetherClient(secondHarness.Http, secondHarness.Server.ConnectAsync,
            secondHarness.Time, secondHarness.Logger, sharedDirectory ? h.DataDirectory : secondHarness.DataDirectory);
        await using var first = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await using var second = other.WatchAsync(secondRoomId, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await first.MoveNextAsync());
        Assert.True(await first.MoveNextAsync());
        Assert.IsType<Connected>(first.Current);
        Assert.True(await second.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        Assert.IsType<Connected>(second.Current);
        foreach (var (server, updates) in new[] { (h.Server, first), (secondHarness.Server, second) })
        {
            await server.PushAsync(FakeDanmakuServer.Packet(5,
                Encoding.UTF8.GetBytes("""{"cmd":"DANMU_MSG","info":[[],"独立接收",[0,"观众"]]}"""), 0));
            Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.Equal("独立接收", Assert.IsType<Danmaku>(updates.Current).Content);
        }
    }

    [Fact]
    public async Task Reconnecting_keeps_the_lock_until_the_stream_is_disposed()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var secondServer = new FakeDanmakuServer();
        using var other = h.ClientSharingData(secondServer.ConnectAsync);
        await using (var first = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token))
        {
            Assert.True(await first.MoveNextAsync());
            Assert.True(await first.MoveNextAsync());
            await h.Server.DisconnectAsync();
            Assert.True(await first.MoveNextAsync());
            Assert.IsType<Reconnecting>(first.Current);
            await using var blocked = other.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await blocked.MoveNextAsync());
            Assert.Contains("直播间 7734200 已有直播间连接", error.Message);
        }
        await using var second = other.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await second.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        Assert.IsType<Connected>(second.Current);
    }

    [Fact]
    public async Task Login_and_logout_in_another_client_do_not_take_or_release_the_room_lock()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var first = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await first.MoveNextAsync());
        Assert.True(await first.MoveNextAsync());
        using var other = h.ClientSharingData(h.Server.ConnectAsync);
        await using (var login = other.LoginAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token))
        {
            Assert.True(await login.MoveNextAsync());
            Assert.IsType<LoginQrCode>(login.Current);
            Assert.True(await h.AdvancePollAsync(login));
            Assert.IsType<LoggedIn>(login.Current);
        }
        await other.LogoutAsync(h.Stop.Token);
        await using var blocked = other.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await blocked.MoveNextAsync());
        Assert.Contains("直播间 7734200 已有直播间连接", error.Message);
        await h.Server.PushAsync(FakeDanmakuServer.Packet(5,
            Encoding.UTF8.GetBytes("""{"cmd":"DANMU_MSG","info":[[],"仍在接收",[0,"观众"]]}"""), 0));
        Assert.True(await first.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.Equal("仍在接收", Assert.IsType<Danmaku>(first.Current).Content);
    }
}
