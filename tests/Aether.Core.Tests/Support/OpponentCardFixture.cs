namespace Aether.Core.Tests.Support;

/// <summary>B站's answers about the other streamer of a PK as the 2026-11-22 answers looked (made-up streamer and viewers).</summary>
internal static class OpponentCardFixture
{
    public const long OpponentRoom = 2468000, OpponentUid = 13579;
    public const string OpponentName = "星星点灯";
    public const string Battle = "https://api.live.bilibili.com/xlive/pk-interface/v1/battle/GetPkBattleMain";
    public const string RoomInfo = "https://api.live.bilibili.com/room/v1/Room/get_info";
    public const string Guards = "https://api.live.bilibili.com/xlive/app-room/v2/guardTab/topListNew";
    public const string OnlineRankUrl = "https://api.live.bilibili.com/xlive/general-interface/v1/rank/queryContributionRank";

    public static string BattleMain(string level = """{"level1_name":"钻石传说","level2_count":3,"is_top_level":false,"rank_info":"全站排名458"}""",
        int firstWins = 0) =>
        $$$"""{"code":0,"data":{"battle_level":{{{level}}},"tasks":[{"task_type":2,"current_num":3,"need_num":15},{"task_type":1,"current_num":{{{firstWins}}},"need_num":1}]}}""";

    private const string TopThree = """
        {"code":0,"data":{"count":5,"item":[
          {"uid":1,"name":"甲","score":12345,"wealth_level":31,"guard_level":3,"uinfo":{"medal":{"level":25,"ruid":13579}}},
          {"uid":2,"name":"乙","score":5200,"wealth_level":22,"guard_level":0,"uinfo":{"medal":{"level":21,"ruid":97531}}},
          {"uid":3,"name":"丙","score":880,"wealth_level":12,"guard_level":0,"uinfo":{"medal":null}},
          {"uid":4,"name":"丁","score":10,"wealth_level":5,"guard_level":0,"uinfo":{"medal":null}}]}}
        """;

    /// <summary>Has B站 answer all four, with this GetPkBattleMain.</summary>
    public static void Answer(FakeBilibiliHttp http, string? battle = null)
    {
        http.Responses[Battle] = battle ?? BattleMain();
        http.Responses[RoomInfo] = """{"code":0,"data":{"room_id":2468000,"attention":34613}}""";
        http.Responses[Guards] = """{"code":0,"data":{"info":{"num":123,"page":13,"now":1},"list":[],"top3":[]}}""";
        http.Responses[OnlineRankUrl] = TopThree;
    }
}
