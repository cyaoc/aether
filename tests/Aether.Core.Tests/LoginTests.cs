using Aether.Core.Tests.Support;
using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Core.Tests;

public sealed class LoginTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "aether-tests-" + Guid.NewGuid());
    private readonly FakeTimeProvider time = new(DateTimeOffset.FromUnixTimeSeconds(1702204169));
    private readonly FakeBilibiliHttp http = new();
    private const string Generate = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate";
    private const string Poll = "https://passport.bilibili.com/x/passport-login/web/qrcode/poll";
    private const string Spi = "https://api.bilibili.com/x/frontend/finger/spi";

    private void SuccessfulLogin(string session)
    {
        http.Respond = (request, _) => Task.FromResult(request.RequestUri!.GetLeftPart(UriPartial.Path) switch
        {
            Generate => Json("""{"code":0,"data":{"url":"https://example.test/scan","qrcode_key":"test-key"}}"""),
            Poll => Json("""{"code":0,"data":{"code":0,"refresh_token":"refresh-token"}}""",
                $"SESSDATA={session}; Path=/; Domain=.bilibili.com; HttpOnly; Secure",
                "bili_jct=csrf; Path=/; Domain=.bilibili.com",
                "DedeUserID=123; Path=/; Domain=.bilibili.com",
                "DedeUserID__ckMd5=checksum; Path=/; Domain=.bilibili.com",
                "sid=extra-cookie; Path=/; Domain=.bilibili.com"),
            Spi => Json("""{"code":0,"data":{"b_3":"device-buvid"}}"""),
            _ => throw new InvalidOperationException("Unexpected login request.")
        });
    }

    private static HttpResponseMessage Json(string body, params string[] cookies)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        if (cookies.Length > 0) response.Headers.Add("Set-Cookie", cookies);
        return response;
    }

    private AetherClient CreateClient() => new(http,
        (_, _) => throw new InvalidOperationException("Login must not connect a WebSocket."),
        time, new RecordingLogger(), dataDirectory);

    private SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "aether.db"), Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    [Fact]
    public void First_run_creates_version_one_database_in_wal_mode()
    {
        using var client = CreateClient();
        Assert.True(File.Exists(Path.Combine(dataDirectory, "aether.db")));
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM credential";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Login_saves_all_cookies_refresh_token_and_local_save_time_before_reporting_success()
    {
        SuccessfulLogin("encoded%2Csession");
        using var client = CreateClient();
        await using var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("https://example.test/scan", Assert.IsType<LoginQrCode>(updates.Current).Content);
        var next = updates.MoveNextAsync().AsTask();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.IsType<LoggedIn>(updates.Current);
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cookies, refresh_token, saved_at FROM credential";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(0))!;
        Assert.Equal(6, cookies.Count);
        Assert.Equal("encoded%2Csession", cookies["SESSDATA"]);
        Assert.Equal("csrf", cookies["bili_jct"]);
        Assert.Equal("123", cookies["DedeUserID"]);
        Assert.Equal("checksum", cookies["DedeUserID__ckMd5"]);
        Assert.Equal("extra-cookie", cookies["sid"]);
        Assert.Equal("device-buvid", cookies["buvid3"]);
        Assert.Equal("refresh-token", reader.GetString(1));
        Assert.Equal(time.GetUtcNow(), DateTimeOffset.Parse(reader.GetString(2)));
        Assert.False(reader.Read());
        Assert.False(await updates.MoveNextAsync());
        Assert.Contains(http.Requests, r => r.Uri.AbsoluteUri == Poll + "?qrcode_key=test-key");
    }

    [Fact]
    public async Task Expired_qr_codes_are_replaced_repeatedly_until_login_succeeds()
    {
        SuccessfulLogin("session");
        var success = http.Respond!;
        var generated = 0;
        http.Respond = (request, token) =>
        {
            if (request.RequestUri!.AbsoluteUri == Generate)
            {
                generated++;
                return Task.FromResult(Json($$$"""{"code":0,"data":{"url":"https://example.test/scan/{{{generated}}}","qrcode_key":"key-{{{generated}}}"}}"""));
            }
            if (request.RequestUri.GetLeftPart(UriPartial.Path) == Poll && generated < 3)
                return Task.FromResult(Json("""{"code":0,"data":{"code":86038}}"""));
            return success(request, token);
        };
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
        SuccessfulLogin("old-session");
        await CompleteLoginAsync(client);
        time.Advance(TimeSpan.FromMinutes(5));
        SuccessfulLogin("new-session");
        var success = http.Respond!;
        http.Respond = (request, token) => request.RequestUri!.GetLeftPart(UriPartial.Path) == Poll
            ? Task.FromResult(Json("""{"code":0,"data":{"code":0,"refresh_token":"new-refresh"}}""",
                "SESSDATA=new-session; Path=/; Domain=.bilibili.com",
                "bili_jct=new-csrf; Path=/; Domain=.bilibili.com",
                "DedeUserID=456; Path=/; Domain=.bilibili.com",
                "buvid3=new-buvid; Path=/; Domain=.bilibili.com"))
            : success(request, token);
        await CompleteLoginAsync(client);
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cookies, refresh_token, saved_at FROM credential";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(0))!;
        Assert.Equal(4, cookies.Count);
        Assert.Equal("new-session", cookies["SESSDATA"]);
        Assert.Equal("new-csrf", cookies["bili_jct"]);
        Assert.Equal("456", cookies["DedeUserID"]);
        Assert.Equal("new-buvid", cookies["buvid3"]);
        Assert.Equal("new-refresh", reader.GetString(1));
        Assert.Equal(time.GetUtcNow(), DateTimeOffset.Parse(reader.GetString(2)));
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Logout_removes_persisted_credentials_even_in_a_new_client_and_is_repeatable()
    {
        SuccessfulLogin("session");
        using (var client = CreateClient()) await CompleteLoginAsync(client);
        using var nextClient = CreateClient();
        await nextClient.LogoutAsync(TestContext.Current.CancellationToken);
        await nextClient.LogoutAsync(TestContext.Current.CancellationToken);
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM credential";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Theory]
    [InlineData(86101)]
    [InlineData(86090)]
    public async Task Waiting_for_scan_or_confirmation_is_paced_and_can_be_cancelled(int status)
    {
        SuccessfulLogin("session");
        var success = http.Respond!;
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        http.Respond = (request, token) =>
        {
            if (request.RequestUri!.GetLeftPart(UriPartial.Path) != Poll) return success(request, token);
            polled.SetResult();
            return Task.FromResult(Json(JsonSerializer.Serialize(new { code = 0, data = new { code = status } })));
        };
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
        SuccessfulLogin("session");
        var success = http.Respond!;
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        http.Respond = async (request, token) =>
        {
            if (request.RequestUri!.GetLeftPart(UriPartial.Path) != endpoint) return await success(request, token);
            requested.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Cancelled request must not complete.");
        };
        using var client = CreateClient();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var updates = client.LoginAsync(stop.Token).GetAsyncEnumerator(stop.Token);
        Task<bool> next;
        if (endpoint == Generate) next = updates.MoveNextAsync().AsTask();
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
        SuccessfulLogin("session");
        using var client = CreateClient();
        await using (var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken))
            Assert.True(await updates.MoveNextAsync());
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(http.Requests);
        AssertNoCredential();
    }

    [Theory]
    [InlineData("http")]
    [InlineData("api")]
    [InlineData("status")]
    [InlineData("cookies")]
    public async Task Failed_login_keeps_previous_credential_and_never_reports_success(string failure)
    {
        SuccessfulLogin("old-session");
        using var client = CreateClient();
        await CompleteLoginAsync(client);
        var success = http.Respond!;
        http.Respond = (request, token) => request.RequestUri!.GetLeftPart(UriPartial.Path) == Poll
            ? Task.FromResult(failure switch
            {
                "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "api" => Json("""{"code":-400,"message":"bad request"}"""),
                "status" => Json("""{"code":0,"data":{"code":12345}}"""),
                _ => Json("""{"code":0,"data":{"code":0,"refresh_token":"new-refresh"}}""",
                    "bili_jct=new-csrf; Path=/; Domain=.bilibili.com")
            })
            : success(request, token);
        await using var updates = client.LoginAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await updates.MoveNextAsync());
        var error = await Record.ExceptionAsync(async () => await AdvancePollAsync(updates));
        if (failure == "http") Assert.IsType<HttpRequestException>(error);
        else if (failure == "cookies") Assert.IsType<InvalidDataException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cookies FROM credential";
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>((string)command.ExecuteScalar()!)!;
        Assert.Equal("old-session", cookies["SESSDATA"]);
    }

    private void AssertNoCredential()
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM credential";
        Assert.Equal(0L, command.ExecuteScalar());
    }

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
