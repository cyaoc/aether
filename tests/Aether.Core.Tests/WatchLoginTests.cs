using System.Text.Json;
using Aether.Core.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Aether.Core.Tests;

public sealed class WatchLoginTests
{
    private const string Nav = "https://api.bilibili.com/x/web-interface/nav";
    private const string Generate = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate";
    private const string Poll = "https://passport.bilibili.com/x/passport-login/web/qrcode/poll";

    [Fact]
    public async Task Missing_credential_prompts_for_qr_saves_login_and_continues_connecting()
    {
        await using var h = new WatchHarness();
        var authenticated = h.Http.Responses[Nav];
        h.Http.Responses[Nav] = """{"code":-101,"data":{"isLogin":false}}""";
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("https://example.test/scan", Assert.IsType<WatchQrCode>(updates.Current).Content);
        Assert.Equal(Nav, h.Http.Requests[0].Uri.AbsoluteUri);
        Assert.Null(h.Server.ConnectedUri);
        h.Http.Responses[Nav] = authenticated;
        Assert.True(await PollAsync(h, updates));
        Assert.IsType<Connecting>(updates.Current);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(h.DataDirectory, "aether.db"), Pooling = false
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT cookies, refresh_token, saved_at FROM credential";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(0))!;
            Assert.Equal("saved-session", cookies["SESSDATA"]);
            Assert.Equal("saved-buvid", cookies["buvid3"]);
            Assert.Equal("refresh-token", reader.GetString(1));
            Assert.Equal(h.Time.GetUtcNow(), DateTimeOffset.Parse(reader.GetString(2)));
            Assert.False(reader.Read());
        }
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connected>(updates.Current);
        using var auth = JsonDocument.Parse((await h.Server.NextRequestAsync()).AsMemory(16));
        Assert.Equal(9876543210, auth.RootElement.GetProperty("uid").GetInt64());
        Assert.Equal("saved-buvid", auth.RootElement.GetProperty("buvid").GetString());
        var request = Assert.Single(h.Http.Requests, r => r.Uri.AbsolutePath.EndsWith("getDanmuInfo"));
        Assert.Contains("SESSDATA=saved-session", request.Cookie);
    }

    [Theory]
    [InlineData(-101)]
    [InlineData(0)]
    public async Task Invalid_credential_prompts_for_fresh_login_and_replaces_expired_qr_codes(int code)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var authenticated = h.Http.Responses[Nav];
        h.Http.Responses[Nav] = $$$"""{"code":{{{code}}},"data":{"isLogin":false}}""";
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<WatchQrCode>(updates.Current);
        Assert.Contains("SESSDATA=saved-session", h.Http.Requests[0].Cookie);
        Assert.DoesNotContain("SESSDATA", Assert.Single(h.Http.Requests, r => r.Uri.AbsoluteUri == Generate).Cookie ?? "");
        for (var i = 1; i <= 2; i++)
        {
            h.Http.Responses[Poll] = """{"code":0,"data":{"code":86038}}""";
            h.Http.Responses[Generate] = $$$"""{"code":0,"data":{"url":"https://example.test/scan-{{{i}}}","qrcode_key":"key-{{{i}}}"}}""";
            Assert.True(await PollAsync(h, updates));
            Assert.Equal($"https://example.test/scan-{i}", Assert.IsType<WatchQrCode>(updates.Current).Content);
            Assert.Null(h.Server.ConnectedUri);
        }
        h.Http.Responses[Poll] = """{"code":0,"data":{"code":0,"refresh_token":"refresh-token"}}""";
        h.Http.Responses[Nav] = authenticated;
        Assert.True(await PollAsync(h, updates));
        Assert.IsType<Connecting>(updates.Current);
        Assert.Equal("?qrcode_key=key-2", h.Http.Requests.Last(r => r.Uri.GetLeftPart(UriPartial.Path) == Poll).Uri.Query);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connected>(updates.Current);
    }

    [Fact]
    public async Task Every_watch_checks_nav_and_logout_makes_next_watch_prompt_for_qr()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        for (var i = 0; i < 2; i++)
        {
            await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Connecting>(updates.Current);
        }
        Assert.Equal(2, h.Http.Requests.Count(r => r.Uri.AbsoluteUri == Nav));
        await h.Client.LogoutAsync(h.Stop.Token);
        h.Http.Responses[Nav] = """{"code":-101,"data":{"isLogin":false}}""";
        await using var loggedOut = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await loggedOut.MoveNextAsync());
        Assert.IsType<WatchQrCode>(loggedOut.Current);
        Assert.Null(h.Http.Requests.Last(r => r.Uri.AbsoluteUri == Nav).Cookie);
    }

    [Theory]
    [InlineData(Nav)]
    [InlineData(Generate)]
    [InlineData(Poll)]
    public async Task Cancellation_during_login_ends_watch_without_connecting(string endpoint)
    {
        await using var h = new WatchHarness();
        var respond = h.Http.Respond!;
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Http.Respond = async (request, token) =>
        {
            if (request.RequestUri!.GetLeftPart(UriPartial.Path) != endpoint) return await respond(request, token);
            requested.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Cancelled request must not complete.");
        };
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        if (endpoint == Poll)
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<WatchQrCode>(updates.Current);
        }
        var next = updates.MoveNextAsync().AsTask();
        if (endpoint == Poll) h.Time.Advance(TimeSpan.FromSeconds(2));
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
        await h.Stop.CancelAsync();
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Null(h.Server.ConnectedUri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposing_or_cancelling_watch_at_qr_stops_polling_and_never_connects(bool cancel)
    {
        await using var h = new WatchHarness();
        await using (var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token))
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<WatchQrCode>(updates.Current);
            if (cancel)
            {
                var next = updates.MoveNextAsync().AsTask();
                await h.Stop.CancelAsync();
                Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            }
        }
        h.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.GetLeftPart(UriPartial.Path) == Poll);
        Assert.Null(h.Server.ConnectedUri);
    }

    [Fact]
    public async Task Nav_api_failure_is_reported_without_prompting_for_login_or_connecting()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Http.Responses[Nav] = """{"code":-352,"message":"risk control"}""";
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await updates.MoveNextAsync());
        Assert.Contains("-352", error.Message);
        Assert.Single(h.Http.Requests);
        Assert.Null(h.Server.ConnectedUri);
    }

    [Fact]
    public async Task Unauthenticated_nav_after_scan_never_connects_anonymously()
    {
        await using var h = new WatchHarness();
        h.Http.Responses[Nav] = """{"code":-101,"data":{"isLogin":false}}""";
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<WatchQrCode>(updates.Current);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PollAsync(h, updates));
        Assert.Contains("登录凭据未生效", error.Message);
        Assert.Null(h.Server.ConnectedUri);
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.AbsolutePath.EndsWith("room_init"));
    }

    private static async Task<bool> PollAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates)
    {
        var next = updates.MoveNextAsync().AsTask();
        h.Time.Advance(TimeSpan.FromSeconds(2));
        return await next.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
    }
}
