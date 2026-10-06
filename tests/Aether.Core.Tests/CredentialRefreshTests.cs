using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class CredentialRefreshTests
{
    private const string Info = "https://passport.bilibili.com/x/passport-login/web/cookie/info";
    private const string Refresh = "https://passport.bilibili.com/x/passport-login/web/cookie/refresh";
    private const string Confirm = "https://passport.bilibili.com/x/passport-login/web/confirm/refresh";
    private const string Poll = "https://passport.bilibili.com/x/passport-login/web/qrcode/poll";
    private const string Nav = "https://api.bilibili.com/x/web-interface/nav";

    [Fact]
    public async Task Credential_saved_elsewhere_while_old_credential_nav_is_pending_is_adopted_during_qr_wait()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var respond = h.Http.Respond!;
        h.Http.Respond = async (request, token) =>
        {
            if (request.RequestUri!.AbsoluteUri == Nav
                && request.Headers.GetValues("Cookie").Single().Contains("SESSDATA=saved-session"))
            {
                await LoginElsewhereAsync(h);
                return FakeBilibiliHttp.Json("""{"code":-101,"data":{"isLogin":false}}""");
            }
            return await respond(request, token);
        };
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<WatchQrCode>(updates.Current);
        Assert.True(await h.AdvancePollAsync(updates));
        Assert.IsType<Connecting>(updates.Current);
        Assert.Equal("external-token", h.SavedCredential().RefreshToken);
        Assert.Contains("SESSDATA=external-session", h.Http.Requests.Last(r => r.Uri.AbsoluteUri == Nav).Cookie);
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Poll);
    }

    [Fact]
    public async Task Rejected_credential_discards_danmaku_buffered_before_the_check()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(10));
        ConfigureRefresh(h, refreshCode: 86095);
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        var packet = DanmakuPacket();
        await h.Server.PushAsync([.. packet, .. packet]);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Danmaku>(updates.Current);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => h.Logger.Entries.Any(e => e.Message.Contains("请重新扫码")));
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Refresh_does_not_overwrite_or_delete_another_process_credential(bool deleted, bool rejected)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1));
        ConfigureRefresh(h);
        var respond = h.Http.Respond!;
        h.Http.Respond = async (request, token) =>
        {
            if (request.RequestUri!.AbsoluteUri == Refresh)
            {
                if (deleted) await h.Client.LogoutAsync(token);
                else await LoginElsewhereAsync(h);
                if (rejected) return FakeBilibiliHttp.Json("""{"code":86095,"message":"mismatch"}""");
            }
            return await respond(request, token);
        };
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        if (deleted)
        {
            Assert.IsType<WatchQrCode>(updates.Current);
            Assert.Null(CheckedAt(h));
        }
        else
        {
            Assert.IsType<Connecting>(updates.Current);
            Assert.Equal("external-token", h.SavedCredential().RefreshToken);
            Assert.Contains("SESSDATA=external-session", h.Http.Requests.Last(r => r.Uri.AbsoluteUri == Nav).Cookie);
        }
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.AbsoluteUri == Confirm);
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("请重新扫码"));
    }

    [Theory]
    [InlineData(-101)]
    [InlineData(86095)]
    [InlineData(-400)]
    public async Task Rejected_refresh_stops_receiving_prompts_for_qr_and_resumes_the_same_room(int code)
    {
        await using var first = new FakeDanmakuServer();
        await using var second = new FakeDanmakuServer();
        var attempts = 0;
        await using var h = new WatchHarness((uri, token) => (++attempts == 1 ? first : second).ConnectAsync(uri, token));
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(10));
        ConfigureRefresh(h, refreshCode: code);
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        var next = updates.MoveNextAsync().AsTask();
        h.Time.Advance(TimeSpan.FromSeconds(10));
        try
        {
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.IsType<Reconnecting>(updates.Current);
            Assert.Null(CheckedAt(h));
            Assert.Contains(h.Logger.Entries, e => e.Message.Contains("请重新扫码"));
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<WatchQrCode>(updates.Current);
            Assert.Null(second.ConnectedUri);
            Assert.True(await h.AdvancePollAsync(updates));
            Assert.IsType<Connected>(updates.Current);
            using var auth = System.Text.Json.JsonDocument.Parse((await second.NextRequestAsync()).AsMemory(16));
            Assert.Equal(7734200, auth.RootElement.GetProperty("roomid").GetInt64());
            Assert.Equal(9876543210, auth.RootElement.GetProperty("uid").GetInt64());
        }
        finally
        {
            await h.Stop.CancelAsync();
            await next;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Waiting_for_qr_adopts_another_process_credential_only_after_nav_validates_it(bool valid)
    {
        await using var h = new WatchHarness();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<WatchQrCode>(updates.Current);
        await LoginElsewhereAsync(h);
        if (!valid) h.Http.Responses[Nav] = """{"code":-101,"data":{"isLogin":false}}""";
        Assert.True(await h.AdvancePollAsync(updates));
        Assert.IsType(valid ? typeof(Connecting) : typeof(WatchQrCode), updates.Current);
        Assert.Equal(!valid, h.Logger.Entries.Any(e => e.Message.Contains("新的登录凭据未生效")));
        Assert.Contains("SESSDATA=external-session", Assert.Single(h.Http.Requests, r => r.Uri.AbsoluteUri == Nav).Cookie);
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Poll);
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Info);
        Assert.Equal("external-token", h.SavedCredential().RefreshToken);
    }

    [Fact]
    public async Task Logout_during_qr_wait_does_not_complete_login_and_network_retry_can_adopt_external_credential()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var validNav = h.Http.Responses[Nav];
        h.Http.Responses[Nav] = """{"code":-101,"data":{"isLogin":false}}""";
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        Assert.IsType<WatchQrCode>(updates.Current);
        await h.Client.LogoutAsync(h.Stop.Token);
        h.Http.Responses[Poll] = """{"code":0,"data":{"code":86101}}""";
        h.Http.FailOnce("/x/passport-login/web/qrcode/poll", new HttpRequestException("offline"));
        var next = updates.MoveNextAsync().AsTask();
        try
        {
            h.Time.Advance(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => h.Logger.Entries.Any(e => e.Message.Contains("2 秒后重试")));
            Assert.False(next.IsCompleted);
            Assert.Null(h.Server.ConnectedUri);
            h.Http.Responses[Nav] = validNav;
            await LoginElsewhereAsync(h);
            h.Time.Advance(TimeSpan.FromSeconds(2));
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.IsType<Connecting>(updates.Current);
            Assert.Equal("external-token", h.SavedCredential().RefreshToken);
        }
        finally
        {
            await h.Stop.CancelAsync();
            await next;
        }
    }

    private static async Task LoginElsewhereAsync(WatchHarness h)
    {
        var http = new FakeBilibiliHttp();
        http.ConfigureLogin("external-session");
        http.Responses[Poll] = """{"code":0,"data":{"code":0,"refresh_token":"external-token"}}""";
        using var client = new AetherClient(http, h.Server.ConnectAsync, h.Time, h.Logger, h.DataDirectory);
        await using var login = client.LoginAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await login.MoveNextAsync());
        Assert.True(await h.AdvancePollAsync(login));
        Assert.IsType<LoggedIn>(login.Current);
    }

    [Fact]
    public async Task Version_one_database_preserves_credential_and_checks_immediately_after_migration()
    {
        await using var h = new WatchHarness();
        Directory.CreateDirectory(h.DataDirectory);
        using (var connection = TestDatabase.Open(h.DataDirectory))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE credential (id INTEGER PRIMARY KEY CHECK(id = 1), cookies TEXT NOT NULL,
                    refresh_token TEXT NOT NULL, saved_at TEXT NOT NULL);
                INSERT INTO credential VALUES (1,
                    '{"SESSDATA":"v1-session","bili_jct":"v1-csrf","DedeUserID":"123","buvid3":"v1-buvid"}',
                    'v1-token', '2023-12-10T00:00:00.0000000+00:00');
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        h.Http.Responses[Info] = """{"code":0,"data":{"refresh":false,"timestamp":1702204169000}}""";
        await CheckConnectingAsync(h);
        Assert.Single(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Info);
        var saved = h.SavedCredential();
        Assert.Equal("v1-session", saved.Cookies["SESSDATA"]);
        Assert.Equal("v1-token", saved.RefreshToken);
        Assert.Equal(DateTimeOffset.Parse("2023-12-10T00:00:00Z"), saved.SavedAt);
        Assert.Equal(h.Time.GetUtcNow(), CheckedAt(h));
        using var migrated = TestDatabase.Open(h.DataDirectory);
        using var version = migrated.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, version.ExecuteScalar());
    }

    [Theory]
    [InlineData("/x/passport-login/web/cookie/info")]
    [InlineData("/correspond/")]
    [InlineData("/x/passport-login/web/cookie/refresh")]
    public async Task Transient_failure_keeps_credential_and_check_time_for_the_next_opportunity(string path)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var saved = h.SavedCredential();
        h.Time.Advance(TimeSpan.FromDays(1));
        ConfigureRefresh(h);
        var respond = h.Http.Respond!;
        var failed = false;
        h.Http.Respond = (request, token) =>
        {
            if (!failed && request.RequestUri!.AbsolutePath.StartsWith(path, StringComparison.Ordinal))
            {
                failed = true;
                throw new HttpRequestException("unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable);
            }
            return respond(request, token);
        };
        await CheckConnectingAsync(h);
        Assert.True(failed);
        Assert.Equal(saved.SavedAt, CheckedAt(h));
        Assert.Equal(saved.RefreshToken, h.SavedCredential().RefreshToken);
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("下次再试"));
        await CheckConnectingAsync(h);
        Assert.Equal("new-token", h.SavedCredential().RefreshToken);
        Assert.Equal(2, h.Http.Requests.Count(r => r.Uri.GetLeftPart(UriPartial.Path) == Info));
    }

    [Fact]
    public async Task Reconnect_checks_when_due_even_if_the_previous_connection_never_authenticated()
    {
        await using var h = new WatchHarness((_, _) => throw new System.Net.WebSockets.WebSocketException("offline"));
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(1));
        h.Http.Responses[Info] = """{"code":0,"data":{"refresh":false,"timestamp":1702204169000}}""";
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        Assert.IsType<Reconnecting>(updates.Current);
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Info);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Reconnecting>(updates.Current);
        Assert.Single(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Info);
        Assert.Equal(h.Time.GetUtcNow(), CheckedAt(h));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ending_connection_waits_for_inflight_credential_check_before_reconnecting_or_disposing(bool dispose)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(10));
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var respond = h.Http.Respond!;
        h.Http.Respond = async (request, token) =>
        {
            if (request.RequestUri!.GetLeftPart(UriPartial.Path) != Info) return await respond(request, token);
            requested.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally
            {
                cancelled.SetResult();
                await release.Task;
            }
            throw new InvalidOperationException("Cancelled request must not complete.");
        };
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
        Task ending;
        if (dispose) ending = updates.DisposeAsync().AsTask();
        else
        {
            ending = updates.MoveNextAsync().AsTask();
            await h.Server.DisconnectAsync();
        }
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
            Assert.False(ending.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await ending.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
        if (!dispose) Assert.IsType<Reconnecting>(updates.Current);
        var requests = h.Http.Requests.Count;
        h.Time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(requests, h.Http.Requests.Count);
    }

    [Fact]
    public async Task Ending_connection_lets_an_inflight_refresh_save_the_new_credential()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(10));
        ConfigureRefresh(h);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var respond = h.Http.Respond!;
        h.Http.Respond = async (request, token) =>
        {
            if (request.RequestUri!.AbsoluteUri == Refresh)
            {
                requested.SetResult();
                await release.Task.WaitAsync(token);
            }
            return await respond(request, token);
        };
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
        var ending = updates.MoveNextAsync().AsTask();
        await h.Server.DisconnectAsync();
        await Task.Delay(100, h.Stop.Token); // Let the disconnect reach the connection's finally before B站 answers.
        Assert.False(ending.IsCompleted);
        release.SetResult();
        Assert.True(await ending.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.IsType<Reconnecting>(updates.Current);
        Assert.Equal("new-token", h.SavedCredential().RefreshToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connected_room_checks_when_due_without_disconnecting_and_reconnect_uses_new_cookie(bool networkFailure)
    {
        await using var second = new FakeDanmakuServer();
        await using var first = new FakeDanmakuServer();
        var attempts = 0;
        await using var h = new WatchHarness((uri, token) => (++attempts == 1 ? first : second).ConnectAsync(uri, token));
        await h.LoginAsync();
        var savedAt = h.SavedCredential().SavedAt;
        h.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(10));
        ConfigureRefresh(h);
        if (networkFailure) h.Http.FailOnce("/x/passport-login/web/cookie/info", new HttpRequestException("offline"));
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connected>(updates.Current);
        var next = updates.MoveNextAsync().AsTask();
        try
        {
            h.Time.Advance(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => networkFailure
                ? h.Logger.Entries.Any(e => e.Message.Contains("下次再试"))
                : h.SavedCredential().RefreshToken == "new-token");
            Assert.False(next.IsCompleted);
            Assert.Equal(networkFailure ? savedAt : h.Time.GetUtcNow(), CheckedAt(h));
            await first.DisconnectAsync();
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            Assert.Contains("SESSDATA=new-session", h.Http.Requests.Last(r => r.Uri.AbsolutePath.EndsWith("getDanmuInfo")).Cookie);
            Assert.Equal(networkFailure ? 2 : 1, h.Http.Requests.Count(r => r.Uri.GetLeftPart(UriPartial.Path) == Info));
        }
        finally
        {
            await h.Stop.CancelAsync();
            await next;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_saves_new_credential_before_confirming_with_new_csrf_and_old_token(bool confirmFails)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1));
        var steps = new List<string>();
        ConfigureRefresh(h);
        var respond = h.Http.Respond!;
        h.Http.Respond = async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/correspond/"))
            {
                steps.Add("correspond");
                Assert.Matches("^/correspond/1/[0-9a-f]{256}$", path);
                Assert.Contains("SESSDATA=saved-session", request.Headers.GetValues("Cookie").Single());
            }
            if (request.RequestUri.AbsoluteUri == Refresh)
            {
                steps.Add("refresh");
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("csrf=saved-csrf&refresh_csrf=page-csrf&source=main_web&refresh_token=refresh-token",
                    await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("refresh-token", h.SavedCredential().RefreshToken);
            }
            if (request.RequestUri.AbsoluteUri == Confirm)
            {
                steps.Add("confirm");
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("csrf=new-csrf&refresh_token=refresh-token", await request.Content!.ReadAsStringAsync(token));
                Assert.Contains("SESSDATA=new-session", request.Headers.GetValues("Cookie").Single());
                var saved = h.SavedCredential();
                Assert.Equal("new-session", saved.Cookies["SESSDATA"]);
                Assert.Equal("saved-buvid", saved.Cookies["buvid3"]);
                Assert.Equal("new-token", saved.RefreshToken);
                Assert.Equal(h.Time.GetUtcNow(), saved.SavedAt);
                Assert.Equal(saved.SavedAt, CheckedAt(h));
                if (confirmFails) return FakeBilibiliHttp.Json("""{"code":-111,"message":"failed"}""");
            }
            return await respond(request, token);
        };
        await CheckConnectingAsync(h);
        Assert.Equal(new[] { "correspond", "refresh", "confirm" }, steps);
        Assert.Contains("SESSDATA=new-session", h.Http.Requests.Last(r => r.Uri.AbsolutePath.EndsWith("/nav")).Cookie);
        Assert.Equal("new-token", h.SavedCredential().RefreshToken);
        Assert.Equal(confirmFails, h.Logger.Entries.Any(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    private static void ConfigureRefresh(WatchHarness h, int refreshCode = 0)
    {
        h.Http.Responses[Info] = """{"code":0,"data":{"refresh":true,"timestamp":1702204169000}}""";
        h.Http.Responses[Confirm] = """{"code":0}""";
        var respond = h.Http.Respond!;
        h.Http.Respond = (request, token) => request.RequestUri!.AbsolutePath.StartsWith("/correspond/", StringComparison.Ordinal)
            ? Task.FromResult(FakeBilibiliHttp.Json("""<html><div id="1-name">page-csrf</div></html>"""))
            : request.RequestUri.AbsoluteUri == Refresh
                ? Task.FromResult(refreshCode != 0
                    ? FakeBilibiliHttp.Json($$"""{"code":{{refreshCode}},"message":"rejected"}""")
                    : FakeBilibiliHttp.Json("""{"code":0,"data":{"refresh_token":"new-token"}}""",
                        "SESSDATA=new-session; Path=/; Domain=bilibili.com",
                        "bili_jct=new-csrf; Path=/; Domain=bilibili.com"))
                : respond(request, token);
    }

    private static byte[] DanmakuPacket() => FakeDanmakuServer.Packet(5, System.Text.Encoding.UTF8.GetBytes(
        """{"cmd":"DANMU_MSG","info":[[],"hello",[123,"viewer"]]}"""), 0);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unexpected_check_or_refresh_failure_keeps_credential_and_waits_a_day(bool infoFails)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1));
        ConfigureRefresh(h);
        if (infoFails) h.Http.Responses[Info] = """{"code":-101,"message":"账号未登录"}""";
        else
        {
            var respond = h.Http.Respond!;
            h.Http.Respond = (request, token) => request.RequestUri!.AbsolutePath.StartsWith("/correspond/", StringComparison.Ordinal)
                ? Task.FromResult(FakeBilibiliHttp.Json("<html></html>"))
                : respond(request, token);
        }
        await CheckConnectingAsync(h);
        Assert.Equal("refresh-token", h.SavedCredential().RefreshToken);
        Assert.Equal(h.Time.GetUtcNow(), CheckedAt(h));
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("24 小时后再试"));
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.AbsoluteUri == Refresh);
        await CheckConnectingAsync(h);
        Assert.Single(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Info);
    }

    [Fact]
    public async Task Connected_room_keeps_going_when_a_rejected_refresh_finds_another_process_credential()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(10));
        ConfigureRefresh(h, refreshCode: 86095);
        var respond = h.Http.Respond!;
        h.Http.Respond = async (request, token) =>
        {
            if (request.RequestUri!.AbsoluteUri == Refresh) await LoginElsewhereAsync(h);
            return await respond(request, token);
        };
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        var next = updates.MoveNextAsync().AsTask();
        try
        {
            h.Time.Advance(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => h.Http.Requests.Any(r => r.Uri.AbsoluteUri == Refresh));
            await Task.Delay(100, h.Stop.Token); // Let the rejection settle before proving the connection outlived it.
            await h.Server.PushAsync(DanmakuPacket());
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.IsType<Danmaku>(updates.Current);
            Assert.Equal("external-token", h.SavedCredential().RefreshToken);
            Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("请重新扫码"));
        }
        finally
        {
            await h.Stop.CancelAsync();
            await next;
        }
    }

    [Fact]
    public async Task Logout_elsewhere_during_connection_neither_checks_nor_disconnects()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(10));
        ConfigureRefresh(h);
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        await h.Client.LogoutAsync(h.Stop.Token);
        var next = updates.MoveNextAsync().AsTask();
        try
        {
            h.Time.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(100, h.Stop.Token); // Let the due check wake before proving the connection outlived it.
            await h.Server.PushAsync(DanmakuPacket());
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
            Assert.IsType<Danmaku>(updates.Current);
            Assert.DoesNotContain(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Info);
        }
        finally
        {
            await h.Stop.CancelAsync();
            await next;
        }
    }

    [Fact]
    public async Task Watch_checks_only_after_twenty_four_hours_and_records_a_negative_answer()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Http.Responses[Info] = """{"code":0,"data":{"refresh":false,"timestamp":1702204169000}}""";
        h.Time.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMilliseconds(1));
        await CheckConnectingAsync(h);
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Info);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        await CheckConnectingAsync(h);
        Assert.Single(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Info);
        Assert.Equal(h.Time.GetUtcNow(), CheckedAt(h));
        await CheckConnectingAsync(h);
        Assert.Single(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Info);
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.AbsolutePath.Contains("correspond"));
    }

    private static async Task CheckConnectingAsync(WatchHarness h)
    {
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
    }

    private static DateTimeOffset? CheckedAt(WatchHarness h)
    {
        using var connection = TestDatabase.Open(h.DataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT checked_at FROM credential";
        return command.ExecuteScalar() is string value ? DateTimeOffset.Parse(value) : null;
    }
}
