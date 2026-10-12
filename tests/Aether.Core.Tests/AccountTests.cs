using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class AccountTests
{
    private const string Nav = "https://api.bilibili.com/x/web-interface/nav";

    [Fact]
    public async Task Missing_credential_returns_null_without_any_request()
    {
        await using var h = new WatchHarness();

        Assert.Null(await h.Client.GetAccountAsync(h.Stop.Token));

        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Logged_in_account_returns_uid_and_nickname_using_saved_cookies()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Http.Responses[Nav] = """{"code":0,"data":{"isLogin":true,"mid":9876543210,"uname":"测试账号"}}""";

        var account = await h.Client.GetAccountAsync(h.Stop.Token);

        Assert.NotNull(account);
        Assert.Equal(9876543210, account.Uid);
        Assert.Equal("测试账号", account.Nickname);
        var request = Assert.Single(h.Http.Requests);
        Assert.Equal(Nav, request.Uri.AbsoluteUri);
        Assert.Contains("SESSDATA=saved-session", request.Cookie);
        Assert.Contains("bili_jct=saved-csrf", request.Cookie);
        Assert.Contains("DedeUserID=123", request.Cookie);
    }

    [Theory]
    [InlineData("""{"code":0,"data":{"isLogin":true,"mid":9876543210,"uname":"测试账号"}}""", true)]
    [InlineData("""{"code":0,"data":{"isLogin":false}}""", false)]
    [InlineData("""{"code":-101,"message":"未登录"}""", false)]
    public async Task Reading_account_neither_refreshes_nor_changes_saved_credential(string response, bool loggedIn)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var saved = h.SavedCredential();
        h.Time.Advance(TimeSpan.FromDays(2));
        h.Http.Intercept(Nav, (_, _, _) => Task.FromResult(FakeBilibiliHttp.Json(response,
            "SESSDATA=nav-cookie; Path=/; Domain=.bilibili.com")));

        var account = await h.Client.GetAccountAsync(h.Stop.Token);

        Assert.Equal(loggedIn, account is not null);
        Assert.Equal(Nav, Assert.Single(h.Http.Requests).Uri.AbsoluteUri);
        var after = h.SavedCredential();
        Assert.Equal(saved.Cookies, after.Cookies);
        Assert.Equal(saved.RefreshToken, after.RefreshToken);
        Assert.Equal(saved.SavedAt, after.SavedAt);
        using var connection = TestDatabase.Open(h.DataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT checked_at FROM credential";
        Assert.Equal(saved.SavedAt.ToString("O"), command.ExecuteScalar());
    }

    [Fact]
    public async Task Network_failure_propagates_without_retrying()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var failure = new HttpRequestException("offline");
        h.Http.FailOnce(Nav, failure);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => h.Client.GetAccountAsync(h.Stop.Token));

        Assert.Same(failure, error);
        Assert.Equal(Nav, Assert.Single(h.Http.Requests).Uri.AbsoluteUri);
        Assert.Equal("saved-session", h.SavedCredential().Cookies["SESSDATA"]);
    }

    [Theory]
    [InlineData("""{"code":-400,"message":"bad request"}""", typeof(InvalidOperationException))]
    [InlineData("not json", typeof(System.Text.Json.JsonException))]
    [InlineData("""{"code":0,"data":{"isLogin":true,"mid":123,"uname":null}}""", typeof(InvalidDataException))]
    public async Task Other_errors_propagate_instead_of_reporting_logged_out(string response, Type expected)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Http.Responses[Nav] = response;

        var error = await Record.ExceptionAsync(() => h.Client.GetAccountAsync(h.Stop.Token));

        Assert.IsAssignableFrom(expected, error);
        Assert.Equal(Nav, Assert.Single(h.Http.Requests).Uri.AbsoluteUri);
        Assert.Equal("saved-session", h.SavedCredential().Cookies["SESSDATA"]);
    }

    [Fact]
    public async Task Each_read_uses_the_current_credential_and_fetches_the_account_again()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Http.Responses[Nav] = """{"code":0,"data":{"isLogin":true,"mid":123,"uname":"原账号"}}""";
        Assert.Equal(new Account(123, "原账号"), await h.Client.GetAccountAsync(h.Stop.Token));
        h.Http.ConfigureLogin(sessdata: "new-session");
        await h.LoginAsync();
        h.Http.Responses[Nav] = """{"code":0,"data":{"isLogin":true,"mid":456,"uname":"新账号"}}""";

        Assert.Equal(new Account(456, "新账号"), await h.Client.GetAccountAsync(h.Stop.Token));
        Assert.Contains("SESSDATA=new-session", Assert.Single(h.Http.Requests).Cookie);

        await h.Client.LogoutAsync(h.Stop.Token);
        h.Http.Requests.Clear();
        Assert.Null(await h.Client.GetAccountAsync(h.Stop.Token));
        Assert.Empty(h.Http.Requests);
    }

    [Fact]
    public async Task Pending_account_read_and_its_cancellation_leave_the_room_connection_receiving()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Http.Intercept(Nav, (_, token, _) => FakeBilibiliHttp.HangUntilCancelledAsync(requested, token));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(h.Stop.Token);
        var reading = h.Client.GetAccountAsync(stop.Token);
        try
        {
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
            Assert.False(reading.IsCompleted);
            await h.PushAndSyncAsync(updates);
        }
        finally
        {
            await stop.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => reading.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        }
        await h.PushAndSyncAsync(updates);
    }
}
