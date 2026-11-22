using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.OpponentCardFixture;

namespace Aether.Core.Tests;

/// <summary>The 对手情报卡's numbers, asked of B站 about the other streamer of a PK (made-up streamer and viewers).</summary>
public sealed class OpponentCardTests
{
    /// <summary>A harness where B站 answers all four as the 2026-11-22 answers looked, with this GetPkBattleMain.</summary>
    private static WatchHarness Answering(string? battle = null)
    {
        var h = new WatchHarness();
        Answer(h.Http, battle);
        return h;
    }

    private static Task<OpponentCard> AskAsync(WatchHarness h) => h.Client.GetOpponentCardAsync(new(OpponentUid, OpponentRoom, OpponentName), h.Stop.Token);

    [Fact]
    public async Task Asks_all_four_at_once_anonymously_from_the_other_room_and_fills_the_card()
    {
        await using var h = Answering();
        await h.LoginAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var referers = new List<string>();
        foreach (var url in new[] { Battle, RoomInfo, Guards, OnlineRankUrl })
            h.Http.Intercept(url, async (request, token, next) =>
            {
                lock (referers)
                {
                    referers.Add(request.Headers.Referrer!.AbsoluteUri);
                    if (referers.Count == 4) requested.SetResult();
                }
                // Each answer waits for the other three to be asked, so asking one after another never finishes.
                await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                return await next();
            });
        var requestsBefore = h.Http.Requests.Count;

        var card = await AskAsync(h);

        Assert.Equal(OpponentName, card.Name);
        Assert.Equal(new PkRank("钻石传说", 3, false, 458), card.Rank);
        Assert.False(card.FirstWin);
        Assert.Equal(34613, card.Fans);
        Assert.Equal(123, card.Guards);
        Assert.Equal([new OpponentViewer(12345, 31, 3, 25), new OpponentViewer(5200, 22, 0, 0), new OpponentViewer(880, 12, 0, 0)], card.Top3);
        Assert.All(referers, referer => Assert.Equal("https://live.bilibili.com/2468000", referer));
        var requests = h.Http.Requests[requestsBefore..];
        Assert.All(requests, request => Assert.Null(request.Cookie));
        Assert.Equal([
            "https://api.live.bilibili.com/room/v1/Room/get_info?room_id=2468000",
            "https://api.live.bilibili.com/xlive/app-room/v2/guardTab/topListNew?roomid=2468000&ruid=13579&page=1",
            "https://api.live.bilibili.com/xlive/general-interface/v1/rank/queryContributionRank?ruid=13579&room_id=2468000&page=1&page_size=100&type=online_rank&switch=contribution_rank&platform=web&web_location=444.8",
            "https://api.live.bilibili.com/xlive/pk-interface/v1/battle/GetPkBattleMain?ruid=13579&room_id=2468000",
        ], requests.Select(request => request.Uri.AbsoluteUri).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("""{"level1_name":"苍穹主宰","level2_count":0,"is_top_level":true,"rank_info":"全站排名40"}""", "苍穹主宰", 0, true, 40)]
    [InlineData("""{"level1_name":"黄金战神","level2_count":2,"is_top_level":false,"rank_info":""}""", "黄金战神", 2, false, 0)]
    [InlineData("""{"level1_name":"青铜萌新","level2_count":1,"is_top_level":false,"rank_info":"全站排名9999+"}""", "青铜萌新", 1, false, 0)]
    [InlineData("""{"level1_name":"","level2_count":0,"is_top_level":false,"rank_info":"全站排名9999+"}""", null, 0, false, 0)]
    public async Task Reads_the_rank_its_stars_and_its_place(string level, string? name, int stars, bool top, long place)
    {
        await using var h = Answering(BattleMain(level));
        var card = await AskAsync(h);
        Assert.Equal(name is null ? null : new PkRank(name, stars, top, place), card.Rank);
    }

    [Fact]
    public async Task First_win_is_done_once_its_task_is()
    {
        await using var h = Answering(BattleMain(firstWins: 1));
        Assert.True((await AskAsync(h)).FirstWin);
    }

    [Fact]
    public async Task Fewer_than_three_on_the_online_rank_are_all_there_are()
    {
        await using var h = Answering();
        h.Http.Responses[OnlineRankUrl] = """{"code":0,"data":{"count":1,"item":[{"uid":1,"name":"甲","score":52,"wealth_level":3,"guard_level":0,"uinfo":{"medal":null}}]}}""";
        Assert.Equal([new OpponentViewer(52, 3, 0, 0)], (await AskAsync(h)).Top3);
    }

    [Fact]
    public async Task One_of_the_three_without_a_score_leaves_out_the_online_rank()
    {
        await using var h = Answering();
        h.Http.Responses[OnlineRankUrl] = """{"code":0,"data":{"count":2,"item":[{"uid":1,"name":"甲","score":52,"wealth_level":3},{"uid":2,"name":"乙","wealth_level":3}]}}""";
        Assert.Equal("top3", LeftOut(await AskAsync(h)));
    }

    [Theory]
    [InlineData(Battle, "api", "rank firstWin")]
    [InlineData(Battle, "http", "rank firstWin")]
    [InlineData(Battle, "missing", "rank firstWin")]
    [InlineData(RoomInfo, "api", "fans")]
    [InlineData(RoomInfo, "http", "fans")]
    [InlineData(RoomInfo, "missing", "fans")]
    [InlineData(Guards, "api", "guards")]
    [InlineData(Guards, "http", "guards")]
    [InlineData(Guards, "missing", "guards")]
    [InlineData(OnlineRankUrl, "api", "top3")]
    [InlineData(OnlineRankUrl, "http", "top3")]
    [InlineData(OnlineRankUrl, "missing", "top3")]
    public async Task What_fails_or_reads_wrong_is_left_out_and_the_rest_still_comes(string url, string failure, string leftOut)
    {
        await using var h = Answering();
        h.Http.Intercept(url, (_, _, _) => Task.FromResult(failure switch
        {
            "api" => FakeBilibiliHttp.Json("""{"code":-400,"message":"请求错误"}"""),
            "http" => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway),
            _ => FakeBilibiliHttp.Json("""{"code":0,"data":{}}"""),
        }));
        Assert.Equal(leftOut, LeftOut(await AskAsync(h)));
    }

    [Fact]
    public async Task Tasks_missing_leave_out_only_the_first_win()
    {
        await using var h = Answering("""{"code":0,"data":{"battle_level":{"level1_name":"钻石传说","level2_count":3,"is_top_level":false,"rank_info":"全站排名458"}}}""");
        Assert.Equal("firstWin", LeftOut(await AskAsync(h)));
    }

    [Fact]
    public async Task One_not_answered_in_3_seconds_is_left_out_and_the_rest_still_comes()
    {
        await using var h = Answering();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = CancellationToken.None;
        h.Http.Intercept(Guards, (_, token, _) =>
        {
            waiting = token;
            return FakeBilibiliHttp.HangUntilCancelledAsync(requested, token);
        });
        var asking = AskAsync(h);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        // The fake clock cancels the request as it advances, so whether it was given up is known at once.
        h.Time.Advance(TimeSpan.FromSeconds(3) - TimeSpan.FromMilliseconds(1));
        Assert.False(waiting.IsCancellationRequested);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(waiting.IsCancellationRequested);
        Assert.Equal("guards", LeftOut(await asking.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)));
    }

    private static string LeftOut(OpponentCard card) => string.Join(" ", new[]
    {
        card.Rank is null ? "rank" : null, card.FirstWin is null ? "firstWin" : null, card.Fans is null ? "fans" : null,
        card.Guards is null ? "guards" : null, card.Top3.Count == 0 ? "top3" : null,
    }.OfType<string>());
}
