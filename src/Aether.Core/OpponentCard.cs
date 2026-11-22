using System.Globalization;
using System.Text.Json;

namespace Aether.Core;

/// <summary>The 对手情报卡's numbers about the other streamer of a 1-on-1 PK, as the 桌宠页's pkMatch has them: the name as
/// PK_BATTLE_PRE_NEW gave it; null for what B站 did not answer, and no 在线榜前三 when it did not answer that.</summary>
internal sealed record OpponentCard(string Name, PkRank? Rank, bool? FirstWin, long? Fans, long? Guards, IReadOnlyList<OpponentViewer> Top3)
{
    /// <summary>今日首胜 from GetPkBattleMain's tasks: its task_type 1 is done.</summary>
    public static bool FirstWinOf(JsonElement tasks)
    {
        var task = tasks.EnumerateArray().First(task => task.GetProperty("task_type").GetInt32() == 1);
        return task.GetProperty("current_num").GetInt64() >= task.GetProperty("need_num").GetInt64();
    }
}

/// <summary>A PK 段位: its name, stars (苍穹主宰, the top one, has none) and place in 全站排名, 0 when not on it.</summary>
internal sealed record PkRank(string Name, int Stars, bool Top, long Place)
{
    private const string PlacePrefix = "全站排名";

    /// <summary>GetPkBattleMain's battle_level; none for a streamer without a 段位 this season. Its rank_info is
    /// "全站排名458", or "全站排名9999+" when not among the first 9999, which is not a place.</summary>
    public static PkRank? Of(JsonElement level)
    {
        if (level.GetProperty("level1_name").GetString() is not { Length: > 0 } name) return null;
        var rankInfo = level.GetProperty("rank_info").GetString() ?? "";
        var place = rankInfo.StartsWith(PlacePrefix, StringComparison.Ordinal)
            && long.TryParse(rankInfo[PlacePrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
        return new(name, level.GetProperty("level2_count").GetInt32(), level.GetProperty("is_top_level").GetBoolean(), place);
    }
}

/// <summary>One of the other room's 在线榜前三, without their name: 本场消费, 荣耀等级, 大航海 (guard_level), and the level of
/// the 灯牌 they wear only when it is the other streamer's, 0 otherwise.</summary>
internal sealed record OpponentViewer(long Score, int Wealth, int Guard, int Medal)
{
    public static OpponentViewer Of(OnlineRankViewer viewer, long opponentUid) => new(viewer.Score, viewer.Wealth, viewer.Guard,
        viewer.MedalOwner == opponentUid ? viewer.MedalLevel : 0);
}
