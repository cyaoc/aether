using Aether.Core.Tests.Support;
using System.Net;
using System.Text.Json;
using static Aether.Core.Tests.Support.FakeBilibiliHttp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Core.Tests;

public sealed class LoginTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "aether-tests-" + Guid.NewGuid());
    private readonly FakeTimeProvider time = new(DateTimeOffset.FromUnixTimeSeconds(1702204169));
    private readonly FakeBilibiliHttp http = new();
    private readonly RecordingLogger logger = new();
    private const string Generate = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate";
    private const string Poll = "https://passport.bilibili.com/x/passport-login/web/qrcode/poll";
    private const string Spi = "https://api.bilibili.com/x/frontend/finger/spi";
    private const string Exit = "https://passport.bilibili.com/login/exit/v2";

    private void SuccessfulLogin(string sessdata) => http.ConfigureLogin(sessdata, "csrf", "device-buvid",
        "DedeUserID__ckMd5=checksum; Path=/; Domain=.bilibili.com");

    private AetherClient CreateClient() => new(http,
        (_, _) => throw new InvalidOperationException("Login must not connect a WebSocket."),
        time, logger, dataDirectory);

    [Fact]
    public async Task First_credential_operation_creates_version_three_database_in_wal_mode()
    {
        using var client = CreateClient();
        Assert.False(Directory.Exists(dataDirectory));
        await client.LogoutAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(dataDirectory, "aether.db")));
        using var connection = TestDatabase.Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(3L, command.ExecuteScalar());
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM credential";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Login_saves_all_cookies_refresh_token_and_save_time_before_reporting_success()
    {
        SuccessfulLogin("encoded%2Csessdata");
        using var client = CreateClient();
        await using var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("https://example.test/scan", Assert.IsType<LoginQrCode>(updates.Current).Content);
        var next = updates.MoveNextAsync().AsTask();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.IsType<LoggedIn>(updates.Current);
        var (cookies, refreshToken, savedAt) = TestDatabase.SavedCredential(dataDirectory);
        Assert.Equal(6, cookies.Count);
        Assert.Equal("encoded%2Csessdata", cookies["SESSDATA"]);
        Assert.Equal("csrf", cookies["bili_jct"]);
        Assert.Equal("123", cookies["DedeUserID"]);
        Assert.Equal("checksum", cookies["DedeUserID__ckMd5"]);
        Assert.Equal("extra-cookie", cookies["sid"]);
        Assert.Equal("device-buvid", cookies["buvid3"]);
        Assert.Equal("refresh-token", refreshToken);
        Assert.Equal(time.GetUtcNow(), savedAt);
        Assert.False(await updates.MoveNextAsync());
        var poll = Assert.Single(http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Poll);
        Assert.Equal("?qrcode_key=test-key", poll.Uri.Query);
        Assert.Equal("buvid3=device-buvid", poll.Cookie);
    }

    [Fact]
    public async Task Expired_qr_codes_are_replaced_repeatedly_until_login_succeeds()
    {
        SuccessfulLogin("sessdata");
        var generated = 0;
        http.Intercept(Generate, (_, _, _) =>
        {
            generated++;
            return Task.FromResult(Json($$$"""{"code":0,"data":{"url":"https://example.test/scan/{{{generated}}}","qrcode_key":"key-{{{generated}}}"}}"""));
        });
        http.Intercept(Poll, (_, _, next) => generated < 3 ? Task.FromResult(Json("""{"code":0,"data":{"code":86038}}""")) : next());
        using var client = CreateClient();
        await using var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await updates.MoveNextAsync());
        for (var i = 1; i <= 3; i++)
        {
            Assert.Equal($"https://example.test/scan/{i}", Assert.IsType<LoginQrCode>(updates.Current).Content);
            Assert.True(await AdvancePollAsync(updates));
        }
        Assert.IsType<LoggedIn>(updates.Current);
        Assert.False(await updates.MoveNextAsync());
        Assert.Equal(new[] { "?qrcode_key=key-1", "?qrcode_key=key-2", "?qrcode_key=key-3" },
            http.Requests.Where(r => r.Uri.GetLeftPart(UriPartial.Path) == Poll).Select(r => r.Uri.Query));
    }

    [Fact]
    public async Task Logging_in_again_replaces_the_entire_credential()
    {
        using var client = CreateClient();
        SuccessfulLogin("old-sessdata");
        await CompleteLoginAsync(client);
        time.Advance(TimeSpan.FromMinutes(5));
        SuccessfulLogin("new-sessdata");
        http.Intercept(Poll, (_, _, _) => Task.FromResult(Json("""{"code":0,"data":{"code":0,"refresh_token":"new-refresh"}}""",
            "SESSDATA=new-sessdata; Path=/; Domain=.bilibili.com",
            "bili_jct=new-csrf; Path=/; Domain=.bilibili.com",
            "DedeUserID=456; Path=/; Domain=.bilibili.com",
            "buvid3=new-buvid; Path=/; Domain=.bilibili.com")));
        await CompleteLoginAsync(client);
        var (cookies, refreshToken, savedAt) = TestDatabase.SavedCredential(dataDirectory);
        Assert.Equal(4, cookies.Count);
        Assert.Equal("new-sessdata", cookies["SESSDATA"]);
        Assert.Equal("new-csrf", cookies["bili_jct"]);
        Assert.Equal("456", cookies["DedeUserID"]);
        Assert.Equal("new-buvid", cookies["buvid3"]);
        Assert.Equal("new-refresh", refreshToken);
        Assert.Equal(time.GetUtcNow(), savedAt);
    }

    [Fact]
    public async Task Logout_signs_out_on_bilibili_then_removes_credentials_and_is_repeatable()
    {
        SuccessfulLogin("logout-sessdata");
        using (var client = CreateClient()) await CompleteLoginAsync(client);
        HttpMethod? method = null;
        string? form = null;
        http.Intercept(Exit, async (request, token, next) =>
        {
            (method, form) = (request.Method, await request.Content!.ReadAsStringAsync(token));
            return await next();
        });
        using var nextClient = CreateClient();
        await nextClient.LogoutAsync(TestContext.Current.CancellationToken);
        await nextClient.LogoutAsync(TestContext.Current.CancellationToken);
        var exit = Assert.Single(http.Requests, r => r.Uri.AbsoluteUri == Exit);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("biliCSRF=csrf", form);
        Assert.Contains("SESSDATA=logout-sessdata", exit.Cookie);
        Assert.Contains("DedeUserID=123", exit.Cookie);
        Assert.Contains("bili_jct=csrf", exit.Cookie);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
        AssertNoCredential();
        // A deleted row must not leave readable cookie or token bytes in the database files.
        foreach (var file in Directory.GetFiles(dataDirectory))
        {
            var bytes = File.ReadAllText(file, System.Text.Encoding.Latin1);
            Assert.DoesNotContain("logout-sessdata", bytes);
            Assert.DoesNotContain("refresh-token", bytes);
        }
    }

    [Theory]
    [InlineData("http")]
    [InlineData("api")]
    [InlineData("expired")]
    public async Task Failed_bilibili_logout_still_removes_local_credentials_with_a_warning(string failure)
    {
        SuccessfulLogin("sessdata");
        using var client = CreateClient();
        await CompleteLoginAsync(client);
        http.Intercept(Exit, (_, _, _) => Task.FromResult(failure switch
        {
            "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "api" => Json("""{"code":2202,"message":"csrf 请求非法"}"""),
            // B站 answers an already-expired cookie with its login page instead of JSON.
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<!DOCTYPE html><html></html>") }
        }));
        await client.LogoutAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("仍删除本地登录凭据")).Exception);
        AssertNoCredential();
    }

    [Fact]
    public async Task Cancelling_logout_during_bilibili_sign_out_keeps_local_credentials()
    {
        SuccessfulLogin("sessdata");
        using var client = CreateClient();
        await CompleteLoginAsync(client);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        http.Intercept(Exit, (_, token, _) => HangUntilCancelledAsync(requested, token));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var logout = client.LogoutAsync(stop.Token);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => logout.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(1, TestDatabase.CredentialCount(dataDirectory));
    }

    [Fact]
    public async Task Failed_database_open_is_retried_by_the_next_operation_on_the_same_client()
    {
        using (var setup = CreateClient()) await setup.LogoutAsync(TestContext.Current.CancellationToken);
        SetUserVersion(4);
        using var client = CreateClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LogoutAsync(TestContext.Current.CancellationToken));
        SetUserVersion(3);
        await client.LogoutAsync(TestContext.Current.CancellationToken);
        AssertNoCredential();
    }

    [Fact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Database_files_are_readable_only_by_their_owner()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Unix file modes only.");
        using var client = CreateClient();
        await client.LogoutAsync(TestContext.Current.CancellationToken);
        var database = Path.Combine(dataDirectory, "aether.db");
        // A database left world-readable (e.g. by an older build) is tightened on a client's first use.
        File.SetUnixFileMode(database, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        using var nextClient = CreateClient();
        await nextClient.LogoutAsync(TestContext.Current.CancellationToken);
        using var connection = TestDatabase.Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM credential";
        command.ExecuteScalar();
        var files = Directory.GetFiles(dataDirectory);
        Assert.Equal(3, files.Length); // The open connection keeps -wal and -shm, created after the tightening.
        Assert.All(files, file => Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file)));
    }

    [Theory]
    [InlineData(86101)]
    [InlineData(86090)]
    public async Task Waiting_for_scan_or_confirmation_is_paced_and_can_be_cancelled(int status)
    {
        SuccessfulLogin("sessdata");
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        http.Intercept(Poll, (_, _, _) =>
        {
            polled.SetResult();
            return Task.FromResult(Json(JsonSerializer.Serialize(new { code = 0, data = new { code = status } })));
        });
        using var client = CreateClient();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var updates = client.LoginAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        Assert.True(await updates.MoveNextAsync());
        var next = updates.MoveNextAsync().AsTask();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Poll);
        time.Advance(TimeSpan.FromSeconds(1));
        await polled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(next.IsCompleted);
        await stop.CancelAsync();
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var count = http.Requests.Count;
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(count, http.Requests.Count);
        AssertNoCredential();
    }

    [Theory]
    [InlineData(Generate)]
    [InlineData(Poll)]
    [InlineData(Spi)]
    public async Task Cancellation_during_http_ends_normally_without_saving(string endpoint)
    {
        SuccessfulLogin("sessdata");
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        http.Intercept(endpoint, (_, token, _) => HangUntilCancelledAsync(requested, token));
        using var client = CreateClient();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var updates = client.LoginAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        Task<bool> next;
        if (endpoint is Spi or Generate) next = updates.MoveNextAsync().AsTask();
        else
        {
            Assert.True(await updates.MoveNextAsync());
            next = updates.MoveNextAsync().AsTask();
            time.Advance(TimeSpan.FromSeconds(2));
        }
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        AssertNoCredential();
    }

    [Fact]
    public async Task Disposing_qr_stream_stops_login_without_polling()
    {
        SuccessfulLogin("sessdata");
        using var client = CreateClient();
        await using (var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken))
            Assert.True(await updates.MoveNextAsync());
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.DoesNotContain(http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Poll);
        AssertNoCredential();
    }

    [Theory]
    [InlineData("api", typeof(InvalidOperationException))]
    [InlineData("status", typeof(InvalidOperationException))]
    [InlineData("cookies", typeof(InvalidDataException))]
    [InlineData("token", typeof(InvalidDataException))]
    public async Task Failed_login_keeps_previous_credential_and_never_reports_success(string failure, Type expected)
    {
        SuccessfulLogin("old-sessdata");
        using var client = CreateClient();
        await CompleteLoginAsync(client);
        http.Intercept(Poll, (_, _, _) => Task.FromResult(failure switch
            {
                "api" => Json("""{"code":-400,"message":"bad request"}"""),
                "status" => Json("""{"code":0,"data":{"code":12345,"message":"unknown"}}"""),
                "cookies" => Json("""{"code":0,"data":{"code":0,"refresh_token":"new-refresh"}}""",
                    "bili_jct=new-csrf; Path=/; Domain=.bilibili.com"),
                _ => Json("""{"code":0,"data":{"code":0,"refresh_token":""}}""",
                    "SESSDATA=new-sessdata; Path=/; Domain=.bilibili.com",
                    "bili_jct=new-csrf; Path=/; Domain=.bilibili.com",
                    "DedeUserID=456; Path=/; Domain=.bilibili.com")
            }));
        await using var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await updates.MoveNextAsync());
        var error = await Record.ExceptionAsync(async () => await AdvancePollAsync(updates));
        Assert.IsType(expected, error);
        Assert.Equal("old-sessdata", TestDatabase.SavedCredential(dataDirectory).Cookies["SESSDATA"]);
    }

    [Theory]
    [InlineData(Spi, false)]
    [InlineData(Generate, false)]
    [InlineData(Poll, false)]
    [InlineData(Poll, true)]
    public async Task Network_failures_are_retried_until_login_succeeds(string endpoint, bool timeout)
    {
        SuccessfulLogin("sessdata");
        var failed = false;
        http.Intercept(endpoint, (_, _, next) =>
        {
            if (failed) return next();
            failed = true;
            return timeout
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException()))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        using var client = CreateClient();
        await using var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var next = updates.MoveNextAsync().AsTask();
        if (endpoint == Poll)
        {
            Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            next = updates.MoveNextAsync().AsTask();
            time.Advance(TimeSpan.FromSeconds(2));
        }
        Assert.False(next.IsCompleted);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("2 秒后重试"));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        if (endpoint != Poll)
        {
            Assert.IsType<LoginQrCode>(updates.Current);
            Assert.True(await AdvancePollAsync(updates));
        }
        Assert.IsType<LoggedIn>(updates.Current);
        Assert.Equal(1, TestDatabase.CredentialCount(dataDirectory));
        if (timeout) Assert.IsType<TimeoutException>(Assert.IsType<TaskCanceledException>(warning.Exception).InnerException);
        else Assert.IsType<HttpRequestException>(warning.Exception);
    }

    [Fact]
    public async Task Buvid_failure_ends_login_before_any_qr_code_is_shown()
    {
        SuccessfulLogin("sessdata");
        http.Intercept(Spi, (_, _, _) => Task.FromResult(Json("""{"code":-352,"message":"risk control"}""")));
        using var client = CreateClient();
        await using var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await updates.MoveNextAsync());
        Assert.DoesNotContain(http.Requests, r => r.Uri.AbsoluteUri == Generate);
    }

    private void SetUserVersion(int version)
    {
        using var connection = TestDatabase.Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version}";
        command.ExecuteNonQuery();
    }

    private void AssertNoCredential() => Assert.Equal(0, TestDatabase.CredentialCount(dataDirectory));

    private async Task<bool> AdvancePollAsync(IAsyncEnumerator<LoginUpdate> updates)
    {
        var next = updates.MoveNextAsync().AsTask();
        time.Advance(TimeSpan.FromSeconds(2));
        return await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task CompleteLoginAsync(AetherClient client)
    {
        await using var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<LoginQrCode>(updates.Current);
        Assert.True(await AdvancePollAsync(updates));
        Assert.IsType<LoggedIn>(updates.Current);
        Assert.False(await updates.MoveNextAsync());
    }

    public void Dispose()
    {
        http.Dispose();
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
    }
}
