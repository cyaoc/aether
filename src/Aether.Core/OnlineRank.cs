namespace Aether.Core;

public sealed record OnlineRank(int Count, IReadOnlyList<OnlineRankViewer> Viewers);

/// <summary>A viewer on the 在线榜. The 对手情报卡 also shows their score (本场消费), 荣耀等级, 大航海 (guard_level: 1 总督,
/// 2 提督, 3 舰长) and the 灯牌 they wear, its level and whose it is; 0 for what they have none of or B站 left out, but the
/// score, null when left out, which the card would show as 0.</summary>
public sealed record OnlineRankViewer(long Uid, string Nickname)
{
    public long? Score { get; init; }
    public int Wealth { get; init; }
    public int Guard { get; init; }
    public int MedalLevel { get; init; }
    public long MedalOwner { get; init; }
}
