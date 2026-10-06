using System.Net;
using System.Net.WebSockets;
using Aether.Core.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Aether.Core.Tests;

public sealed class ReconnectionTests
{
    [Fact]
    public async Task Opening_a_socket_without_successful_authentication_does_not_reset_backoff()
    {
        await using var silent = new FakeDanmakuServer { ReplyToAuthentication = false };
        await using var accepted = new FakeDanmakuServer();
        var attempts = 0;
        await using var h = new WatchHarness((uri, token) => ++attempts switch
        {
            1 => throw new WebSocketException("offline"),
            2 => silent.ConnectAsync(uri, token),
            _ => accepted.ConnectAsync(uri, token)
        });
        await h.LoginAsync();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        var next = updates.MoveNextAsync().AsTask();
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await silent.NextRequestAsync();
        await silent.DisconnectAsync();
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 2);
        Assert.IsType<Connected>(updates.Current);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_during_a_reconnect_request_ends_immediately(bool duringHttp)
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        await using var h = new WatchHarness(async (_, token) =>
        {
            if (++attempts == 1) throw new WebSocketException("offline");
            requested.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Cancelled connection must not complete.");
        });
        await h.LoginAsync();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        Assert.IsType<Reconnecting>(updates.Current);
        if (duringHttp)
            h.Http.Respond = async (_, token) =>
            {
                requested.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("Cancelled request must not complete.");
            };
        var next = updates.MoveNextAsync().AsTask();
        h.Time.Advance(TimeSpan.FromSeconds(1));
        try { await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token); }
        finally { await h.Stop.CancelAsync(); }
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Each_received_fragment_resets_the_inactivity_deadline()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Server.ReplyToHeartbeats = false;
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        var next = updates.MoveNextAsync().AsTask();
        var packet = FakeDanmakuServer.Packet(5,
            System.Text.Encoding.UTF8.GetBytes("""{"cmd":"DANMU_MSG","info":[[],"分片消息",[0,"观众"]]}"""), 0);
        try
        {
            h.Time.Advance(TimeSpan.FromSeconds(59));
            await h.Server.PushAsync(packet[..5], endOfMessage: false);
            await Assert.ThrowsAsync<TimeoutException>(() => next.WaitAsync(TimeSpan.FromMilliseconds(100), h.Stop.Token));
            h.Time.Advance(TimeSpan.FromSeconds(59));
            await h.Server.PushAsync(packet[5..]);
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.Equal("分片消息", Assert.IsType<Danmaku>(updates.Current).Content);
        }
        finally
        {
            await h.Stop.CancelAsync();
            await next;
        }
    }

    [Theory]
    [InlineData("/x/web-interface/nav", "offline")]
    [InlineData("/room/v1/Room/room_init", "offline")]
    [InlineData("/xlive/web-room/v1/index/getDanmuInfo", "offline")]
    [InlineData("/x/web-interface/nav", "timeout")]
    [InlineData("/room/v1/Room/room_init", "timeout")]
    [InlineData("/xlive/web-room/v1/index/getDanmuInfo", "timeout")]
    [InlineData("/xlive/web-room/v1/index/getDanmuInfo", "server error")]
    public async Task Http_network_failure_timeout_or_server_error_reports_reconnecting_and_recovers(string path, string failure)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Http.FailOnce(path, failure switch
        {
            "timeout" => new TaskCanceledException("HTTP timeout"),
            "server error" => new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable),
            _ => new HttpRequestException("offline")
        });
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        if (!path.EndsWith("/nav"))
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Connecting>(updates.Current);
        }
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    public async Task Bilibili_refusal_on_reconnect_ends_room_connection_instead_of_retrying(HttpStatusCode? status)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        if (status is { } code)
            h.Http.FailOnce("/xlive/web-room/v1/index/getDanmuInfo", new HttpRequestException("risk control", null, code));
        else h.Http.Responses["https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo"] =
            """{"code":-352,"message":"风控校验失败"}""";
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        var next = updates.MoveNextAsync().AsTask();
        h.Time.Advance(TimeSpan.FromSeconds(1));
        var error = await Record.ExceptionAsync(() => next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.IsType(status is null ? typeof(InvalidOperationException) : typeof(HttpRequestException), error);
        Assert.Equal(2, h.Http.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("getDanmuInfo")));
    }

    [Fact]
    public async Task Local_failure_ends_room_connection_instead_of_reconnecting_forever()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(h.DataDirectory, "aether.db"), Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 2";
            command.ExecuteNonQuery();
        }
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await updates.MoveNextAsync());
        Assert.Contains("数据库版本", error.Message);
        Assert.Empty(h.Http.Requests);
    }

    [Theory]
    [InlineData("/x/frontend/finger/spi")]
    [InlineData("/x/passport-login/web/qrcode/generate")]
    [InlineData("/x/passport-login/web/qrcode/poll")]
    public async Task Network_failure_while_logging_in_through_watch_retries_in_place_without_wasting_the_qr_code(string path)
    {
        await using var h = new WatchHarness();
        h.Http.FailOnce(path, new HttpRequestException("offline"));
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        var polling = path.EndsWith("/poll");
        if (polling)
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<WatchQrCode>(updates.Current);
        }
        var next = updates.MoveNextAsync().AsTask();
        try
        {
            if (polling) h.Time.Advance(TimeSpan.FromSeconds(2));
            Assert.False(next.IsCompleted);
            h.Time.Advance(TimeSpan.FromSeconds(2));
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            if (!polling)
            {
                Assert.IsType<WatchQrCode>(updates.Current);
                Assert.True(await h.AdvancePollAsync(updates));
            }
            Assert.IsType<Connecting>(updates.Current); // Same QR code, no 重连中 in between.
        }
        finally
        {
            await h.Stop.CancelAsync();
            await next;
        }
    }

    [Theory]
    [InlineData(-101, false)]
    [InlineData(0, false)]
    [InlineData(-101, true)]
    public async Task Reconnect_refreshes_wbi_key_and_token_without_requiring_expired_or_deleted_credentials_to_login(
        int code, bool loggedOut)
    {
        await using var first = new FakeDanmakuServer();
        await using var second = new FakeDanmakuServer();
        var attempts = 0;
        await using var h = new WatchHarness((uri, token) =>
            (++attempts == 1 ? first : second).ConnectAsync(uri, token));
        await h.LoginAsync();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        h.Http.Responses["https://api.bilibili.com/x/web-interface/nav"] = $$$$"""
            {"code":{{{{code}}}},"data":{"isLogin":false,"wbi_img":{
                "img_url":"https://example.test/00000000000000000000000000000000.png",
                "sub_url":"https://example.test/00000000000000000000000000000000.png"}}}
            """;
        h.Http.Responses["https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo"] =
            """{"code":0,"data":{"token":"fresh-token","host_list":[{"host":"fresh.example","wss_port":443}]}}""";
        if (loggedOut) await h.Client.LogoutAsync(h.Stop.Token);
        await first.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        using var auth = System.Text.Json.JsonDocument.Parse((await second.NextRequestAsync()).AsMemory(16));
        Assert.Equal(0, auth.RootElement.GetProperty("uid").GetInt64());
        Assert.Equal("saved-buvid", auth.RootElement.GetProperty("buvid").GetString());
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("以匿名身份重连"));
        Assert.Equal("fresh-token", auth.RootElement.GetProperty("key").GetString());
        Assert.Equal(new Uri("wss://fresh.example/sub"), second.ConnectedUri);
        Assert.Equal(2, h.Http.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("/nav")));
        Assert.Equal(2, h.Http.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("getDanmuInfo")));
        Assert.Equal("?id=7734200&type=0&web_location=444.8&wts=1702204170&w_rid=817b38e14f2980306b2c3c29404ddcde",
            h.Http.Requests.Last(r => r.Uri.AbsolutePath.EndsWith("getDanmuInfo")).Uri.Query);
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.AbsolutePath.EndsWith("/qrcode/generate"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sixty_seconds_without_incoming_data_reconnects_even_before_authentication(bool authenticated)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Server.ReplyToHeartbeats = false;
        h.Server.ReplyToAuthentication = authenticated;
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        if (authenticated)
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Connected>(updates.Current);
        }
        var next = updates.MoveNextAsync().AsTask();
        await h.Server.NextRequestAsync(); // Authentication has been sent, so the receive deadline is active.
        h.Time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(next.IsCompleted);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        try
        {
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.IsType<Reconnecting>(updates.Current);
        }
        finally
        {
            await h.Stop.CancelAsync();
            await next;
        }
    }

    [Fact]
    public async Task Heartbeat_replies_keep_a_quiet_room_connected()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        await h.Server.NextRequestAsync();
        var next = updates.MoveNextAsync().AsTask();
        try
        {
            for (var i = 0; i < 5; i++)
            {
                h.Time.Advance(TimeSpan.FromSeconds(30));
                await h.Server.NextRequestAsync();
                await Assert.ThrowsAsync<TimeoutException>(() => next.WaitAsync(TimeSpan.FromMilliseconds(100), h.Stop.Token));
            }
        }
        finally
        {
            await h.Stop.CancelAsync();
            Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Successful_authentication_resets_backoff_and_danmaku_resumes_on_the_same_stream()
    {
        await using var first = new FakeDanmakuServer();
        await using var second = new FakeDanmakuServer();
        var attempts = 0;
        await using var h = new WatchHarness((uri, token) => ++attempts switch
        {
            3 => first.ConnectAsync(uri, token),
            4 => second.ConnectAsync(uri, token),
            _ => throw new WebSocketException("offline")
        });
        await h.LoginAsync();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 2);
        Assert.IsType<Connected>(updates.Current);
        await first.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        await second.PushAsync(FakeDanmakuServer.Packet(5,
            System.Text.Encoding.UTF8.GetBytes("""{"cmd":"DANMU_MSG","info":[[],"恢复了",[0,"观众"]]}"""), 0));
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("恢复了", Assert.IsType<Danmaku>(updates.Current).Content);
    }

    [Fact]
    public async Task Network_failures_retry_after_one_two_four_eight_sixteen_then_thirty_seconds()
    {
        var attempts = 0;
        await using var h = new WatchHarness((_, _) =>
        {
            attempts++;
            throw new WebSocketException("offline");
        });
        await h.LoginAsync();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        foreach (var seconds in new[] { 1, 2, 4, 8, 16, 30, 30 })
        {
            var before = attempts;
            var next = updates.MoveNextAsync().AsTask();
            h.Time.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1));
            Assert.False(next.IsCompleted);
            Assert.Equal(before, attempts);
            h.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.IsType<Reconnecting>(updates.Current);
            Assert.Equal(before + 1, attempts);
        }
        var pending = updates.MoveNextAsync().AsTask();
        await h.Stop.CancelAsync();
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }
}
