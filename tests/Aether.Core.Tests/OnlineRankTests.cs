using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class OnlineRankTests
{
    [Fact]
    public async Task Reads_rank_once_anonymously_with_room_and_broadcaster_and_skips_unaddressable_viewers()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Http.Respond = (request, _) =>
        {
            Assert.Equal("https://live.bilibili.com/7734200", request.Headers.Referrer!.AbsoluteUri);
            return Task.FromResult(FakeBilibiliHttp.Json("""{"code":0,"data":{"count":345,"item":[{"uid":42,"name":"猫"},{"uid":0,"name":"神秘人"},{"uid":-1,"name":"无效"},{"uid":99,"name":"狗"}]}}"""));
        };
        var rank = await h.Client.GetOnlineRankAsync(7734200, 24680, h.Stop.Token);
        Assert.Equal(345, rank.Count);
        Assert.Equal([new OnlineRankViewer(42, "猫"), new OnlineRankViewer(99, "狗")], rank.Viewers);
        var request = Assert.Single(h.Http.Requests);
        Assert.Null(request.Cookie);
        Assert.Equal("https://api.live.bilibili.com/xlive/general-interface/v1/rank/queryContributionRank?ruid=24680&room_id=7734200&page=1&page_size=100&type=online_rank&switch=contribution_rank&platform=web&web_location=444.8", request.Uri.AbsoluteUri);
    }
    [Theory]
    [InlineData("api")]
    [InlineData("http")]
    [InlineData("timeout")]
    public async Task Failures_include_the_operation(string failure)
    {
        await using var h = new WatchHarness();
        h.Http.Respond = (_, _) => failure switch
        {
            "api" => Task.FromResult(FakeBilibiliHttp.Json("""{"code":-400,"message":"请求错误"}""")),
            "http" => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)),
            _ => throw new TaskCanceledException("timeout")
        };
        var error = await Assert.ThrowsAnyAsync<Exception>(() => h.Client.GetOnlineRankAsync(7734200, 24680, h.Stop.Token));
        Assert.StartsWith("读取在线榜失败", error.Message);
        if (failure == "api") Assert.Equal("读取在线榜失败（-400）：请求错误", error.Message);
        if (failure == "timeout") Assert.Equal("读取在线榜失败：请求超时", error.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task Empty_rank_stays_empty(string items)
    {
        await using var h = new WatchHarness();
        h.Http.Respond = (_, _) => Task.FromResult(FakeBilibiliHttp.Json($$$"""{"code":0,"data":{"count":0,"item":{{{items}}}}}"""));
        var rank = await h.Client.GetOnlineRankAsync(7734200, 24680, h.Stop.Token);
        Assert.Equal(0, rank.Count);
        Assert.Empty(rank.Viewers);
    }

    [Fact]
    public async Task Closing_the_request_cancels_immediately()
    {
        await using var h = new WatchHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Http.Respond = (_, token) => FakeBilibiliHttp.HangUntilCancelledAsync(requested, token);
        var read = h.Client.GetOnlineRankAsync(7734200, 24680, h.Stop.Token);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        h.Stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

}
