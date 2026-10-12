using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class WelcomeLengthTests
{
    private const string InfoUrl = "https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByUser";

    [Theory]
    [InlineData(80, 36)]
    [InlineData(30, 11)]
    public async Task Welcomes_use_the_accounts_limit_for_the_real_room(int limit, int nicknameLength)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.enabled"] = "true" });
        h.Http.Responses[InfoUrl] = System.Text.Json.JsonSerializer.Serialize(new { code = 0, data = new { property = new { danmu = new { length = limit } } } });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(nickname: new string('名', 50)));
        Assert.Equal("欢迎舰长" + new string('名', nicknameLength), (await NextSendAsync(sent))["msg"]);
        var request = Assert.Single(h.Http.Requests, request => request.Uri.GetLeftPart(UriPartial.Path) == InfoUrl);
        Assert.Equal("?room_id=7734200&from=0", request.Uri.Query);
        Assert.Contains("SESSDATA=saved-session", request.Cookie);
        Assert.Contains("bili_jct=saved-csrf", request.Cookie);
        Assert.True(h.Http.Requests.FindIndex(request => request.Uri.AbsolutePath.EndsWith("getDanmuInfo"))
            < h.Http.Requests.IndexOf(request));
        Assert.Contains(h.Logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message == $"弹幕字数上限：{limit}");
    }

    [Theory]
    [InlineData("{\"code\":-352,\"message\":\"风控\"}", "-352")]
    [InlineData("{\"code\":0,\"data\":{}}", "data.property.danmu.length")]
    [InlineData("{\"code\":0,\"data\":null}", "data.property.danmu.length")]
    [InlineData("{\"code\":0,\"data\":{\"property\":[]}}", "data.property.danmu.length")]
    [InlineData("{\"code\":0,\"data\":{\"property\":{\"danmu\":{}}}}", "data.property.danmu.length")]
    [InlineData("{\"code\":0,\"data\":{\"property\":{\"danmu\":{\"length\":0}}}}", "正整数")]
    [InlineData("{\"code\":0,\"data\":{\"property\":{\"danmu\":{\"length\":-1}}}}", "正整数")]
    [InlineData("{\"code\":0,\"data\":{\"property\":{\"danmu\":{\"length\":1.5}}}}", "正整数")]
    [InlineData("{\"code\":0,\"data\":{\"property\":{\"danmu\":{\"length\":\"80\"}}}}", "正整数")]
    [InlineData("{\"code\":0,\"data\":{\"property\":{\"danmu\":{\"length\":2147483648}}}}", "正整数")]
    [InlineData(NetworkFailure, "test network failure")]
    [InlineData("timeout", "test request timeout")]
    public async Task Unavailable_limits_fall_back_to_twenty_without_blocking_the_connection(string response, string reason)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.enabled"] = "true" });
        h.Http.Responses[InfoUrl] = response;
        if (response == NetworkFailure) h.Http.FailOnce(InfoUrl, new HttpRequestException("test network failure"));
        if (response == "timeout") h.Http.FailOnce(InfoUrl, new TaskCanceledException("test request timeout"));
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(nickname: new string('名', 50)));
        Assert.Equal("欢迎舰长名名名名名名", (await NextSendAsync(sent))["msg"]);
        var warning = Assert.Single(h.Logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("弹幕字数上限", warning.Message);
        Assert.Contains("20", warning.Message);
        Assert.Contains(reason, warning.Message);
    }

    [Theory]
    [InlineData("{\"code\":0,\"data\":{\"property\":{\"danmu\":{\"length\":30}}}}", 11)]
    [InlineData("{\"code\":0,\"data\":{}}", 6)]
    public async Task Reconnect_reads_a_fresh_limit_instead_of_reusing_the_previous_one(string response, int nicknameLength)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.enabled"] = "true" });
        h.Http.Responses[InfoUrl] = """{"code":0,"data":{"property":{"danmu":{"length":80}}}}""";
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(nickname: new string('名', 50)));
        Assert.Equal("欢迎舰长" + new string('名', 36), (await NextSendAsync(sent))["msg"]);
        await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎");
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        h.Http.Responses[InfoUrl] = response;
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(nickname: new string('名', 50)));
        Assert.Equal("欢迎舰长" + new string('名', nicknameLength), (await NextSendAsync(sent))["msg"]);
        Assert.Equal(2, h.Http.Requests.Count(request => request.Uri.GetLeftPart(UriPartial.Path) == InfoUrl));
    }

    [Fact]
    public async Task Cancelling_the_limit_request_ends_watching_without_a_fallback_warning()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.enabled"] = "true" });
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Http.Intercept(InfoUrl, (_, token, _) => FakeBilibiliHttp.HangUntilCancelledAsync(requested, token));
        await using var updates = h.Watch();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await h.Stop.CancelAsync();
        Assert.False(await updates.MoveNextAsync());
        Assert.Null(h.Server.ConnectedUri);
        Assert.DoesNotContain(h.Logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("{@}欢迎{大航海}{昵称}", 7, 8)]
    [InlineData("一二三四五六七八九十百{昵称}", 20, 22)]
    public async Task Fixed_text_over_the_limit_is_not_queued(string text, int limit, int length)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.text"] = text
        });
        h.Http.Responses[InfoUrl] = System.Text.Json.JsonSerializer.Serialize(new { code = 0, data = new { property = new { danmu = new { length = limit } } } });
        var sent = CaptureSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            Assert.Contains(h.Logger.Entries, entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains($"计数为 {length}，超过上限 {limit}"));
            await AssertNoSendAsync(sent);
        }
        Assert.Equal(0, SendAttempts(h));
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("丢掉 0 条"));
    }
}
