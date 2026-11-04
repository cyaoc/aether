namespace Aether.Core;

/// <summary>A viewer's paid gift, or a 大航海 purchase. Amounts are integer 金瓜子: Spend is its 投入, Price the price of one
/// gift (of one month for a purchase). Tid identifies the push and is null when B站 gave none; a purchase's is its payflow_id.
/// GuardLevel is 0 for a gift; for a purchase it is B站's guard_level (1 总督, 2 提督, 3 舰长) and IconUrl stays empty.</summary>
internal sealed record Gift(long Uid, string Nickname, long Spend, BlindBox? BlindBox)
{
    public long GiftId { get; init; }
    public string GiftName { get; init; } = "";
    public long Price { get; init; }
    public long Num { get; init; }
    public string FaceUrl { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string? Tid { get; init; }
    public int GuardLevel { get; init; }
}

/// <summary>A viewer's paid gifts after three seconds of silence; amounts are in gold coins.</summary>
public sealed record GiftRoundEnded(long RoomId, long Uid, string Nickname, long Spend,
    DateTimeOffset EndedAt, long EndedTimestamp) : WatchUpdate;
