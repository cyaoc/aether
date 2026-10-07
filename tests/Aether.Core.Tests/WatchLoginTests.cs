using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class WatchLoginTests
{
    private const string Nav = "https://api.bilibili.com/x/web-interface/nav";
    private const string Generate = "https://passport.bilibili.com/x/passport-login/web/qrcode/generate";
    private const string Poll = "https://passport.bilibili.com/x/passport-login/web/qrcode/poll";
    private const string LoggedOutNav = """{"code":-101,"data":{"isLogin":false}}""";

    [Fact]
    public async Task Clients_sharing_a_data_directory_share_login_and_logout()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        using var other = h.ClientSharingData(h.Server.ConnectAsync);
        await using (var updates = h.Watch(other))
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Connecting>(updates.Current); // The other shell uses the saved credential, without a QR code.
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Connected>(updates.Current);
        }
        await other.LogoutAsync(h.Stop.Token);
        await using var loggedOut = h.Watch();
        Assert.True(await loggedOut.MoveNextAsync());
        Assert.IsType<WatchQrCode>(loggedOut.Current);
    }

    [Fact]
    public async Task Missing_credential_prompts_for_qr_saves_login_and_continues_connecting()
    {
        await using var h = new WatchHarness();
        var authenticated = h.Http.Responses[Nav];
        h.Http.Responses[Nav] = LoggedOutNav;
        await using var updates = h.Watch();
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("https://example.test/scan", Assert.IsType<WatchQrCode>(updates.Current).Content);
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.AbsoluteUri == Nav);
        Assert.Null(h.Server.ConnectedUri);
        h.Http.Responses[Nav] = authenticated;
        Assert.True(await h.AdvancePollAsync(updates));
        Assert.IsType<Connecting>(updates.Current);
        var credential = h.SavedCredential();
        Assert.Equal("saved-session", credential.Cookies["SESSDATA"]);
        Assert.Equal("saved-buvid", credential.Cookies["buvid3"]);
        Assert.Equal("refresh-token", credential.RefreshToken);
        Assert.Equal(h.Time.GetUtcNow(), credential.SavedAt);
        Assert.Contains("SESSDATA=saved-session", Assert.Single(h.Http.Requests, r => r.Uri.AbsoluteUri == Nav).Cookie);
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
        await using var updates = h.Watch();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<WatchQrCode>(updates.Current);
        Assert.Contains("SESSDATA=saved-session", Assert.Single(h.Http.Requests, r => r.Uri.AbsoluteUri == Nav).Cookie);
        Assert.DoesNotContain("SESSDATA", Assert.Single(h.Http.Requests, r => r.Uri.AbsoluteUri == Generate).Cookie ?? "");
        for (var i = 1; i <= 2; i++)
        {
            h.Http.Responses[Poll] = """{"code":0,"data":{"code":86038}}""";
            h.Http.Responses[Generate] = $$$"""{"code":0,"data":{"url":"https://example.test/scan-{{{i}}}","qrcode_key":"key-{{{i}}}"}}""";
            Assert.True(await h.AdvancePollAsync(updates));
            Assert.Equal($"https://example.test/scan-{i}", Assert.IsType<WatchQrCode>(updates.Current).Content);
            Assert.Null(h.Server.ConnectedUri);
        }
        h.Http.Responses[Poll] = """{"code":0,"data":{"code":0,"refresh_token":"refresh-token"}}""";
        h.Http.Responses[Nav] = authenticated;
        Assert.True(await h.AdvancePollAsync(updates));
        Assert.IsType<Connecting>(updates.Current);
        Assert.Equal("?qrcode_key=key-2", h.Http.Requests.Last(r => r.Uri.GetLeftPart(UriPartial.Path) == Poll).Uri.Query);
        Assert.Equal(h.Time.GetUtcNow(), h.SavedCredential().SavedAt);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connected>(updates.Current);
    }

    [Fact]
    public async Task Every_room_connection_checks_nav_and_logout_makes_the_next_prompt_for_qr()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        for (var i = 0; i < 2; i++)
        {
            await using var updates = h.Watch();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Connecting>(updates.Current);
        }
        Assert.Equal(2, h.Http.Requests.Count(r => r.Uri.AbsoluteUri == Nav));
        await h.Client.LogoutAsync(h.Stop.Token);
        h.Http.Responses[Nav] = LoggedOutNav;
        await using var loggedOut = h.Watch();
        Assert.True(await loggedOut.MoveNextAsync());
        Assert.IsType<WatchQrCode>(loggedOut.Current);
        Assert.Equal(2, h.Http.Requests.Count(r => r.Uri.AbsoluteUri == Nav)); // No credential, so no nav before the QR code.
    }

    [Theory]
    [InlineData(Nav)]
    [InlineData(Generate)]
    [InlineData(Poll)]
    public async Task Cancellation_during_login_ends_room_connection_without_connecting(string endpoint)
    {
        await using var h = new WatchHarness();
        if (endpoint == Nav) await h.LoginAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Http.Intercept(endpoint, (_, token, _) => FakeBilibiliHttp.HangUntilCancelledAsync(requested, token));
        await using var updates = h.Watch();
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
    public async Task Disposing_or_cancelling_room_connection_at_qr_stops_polling_and_never_connects(bool cancel)
    {
        await using var h = new WatchHarness();
        await using (var updates = h.Watch())
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
        await using var updates = h.Watch();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await updates.MoveNextAsync());
        Assert.Contains("-352", error.Message);
        Assert.Equal(new[] { "/room/v1/Room/room_init", "/x/web-interface/nav" }, h.Http.Requests.Select(r => r.Uri.AbsolutePath));
        Assert.Null(h.Server.ConnectedUri);
    }

    [Fact]
    public async Task Unauthenticated_nav_after_scan_never_connects_anonymously()
    {
        await using var h = new WatchHarness();
        h.Http.Responses[Nav] = LoggedOutNav;
        await using var updates = h.Watch();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<WatchQrCode>(updates.Current);
        Assert.True(await h.AdvancePollAsync(updates));
        Assert.IsType<WatchQrCode>(updates.Current);
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("新的登录凭据未生效"));
        Assert.Null(h.Server.ConnectedUri);
        Assert.Single(h.Http.Requests, r => r.Uri.AbsolutePath.EndsWith("room_init"));
        Assert.DoesNotContain(h.Http.Requests, r => r.Uri.AbsolutePath.EndsWith("getDanmuInfo"));
    }
}
