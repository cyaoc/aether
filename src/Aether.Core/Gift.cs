namespace Aether.Core;

/// <summary>A viewer's paid gift, or a 大航海 purchase. Amounts are integer 金瓜子: Spend is its 投入, Price the price of one
/// gift (of one month for a purchase) and Value what it is worth: Price × Num for a gift, which for one opened from a 盲盒 is
/// its 开出价值, and Spend for a purchase. Blind is the 盲盒 a gift was opened from, null for any other. Timestamp is B站's
/// send time in Unix seconds, null for a purchase, whose push has none that is read. Tid identifies the push and is null
/// when B站 gave none; a purchase's is its payflow_id. GuardLevel is 0 for a gift; for a purchase it is B站's guard_level
/// (1 总督, 2 提督, 3 舰长) and IconUrl stays empty. Shown is the message's switch.</summary>
internal sealed record Gift(long Uid, string Nickname, long Spend)
{
    public long GiftId { get; init; }
    public string GiftName { get; init; } = "";
    public long Price { get; init; }
    public long Num { get; init; }
    public long Value { get; init; }
    public BlindGift? Blind { get; init; }
    public long? Timestamp { get; init; }
    public string FaceUrl { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string? Tid { get; init; }
    public int GuardLevel { get; init; }
    public bool Shown { get; init; } = true;

    /// <summary>Its 礼物键 on the 礼物榜: an opened gift and the same gift sent directly share one, and a 大航海 has one per level.</summary>
    public string BoardKey => GuardLevel > 0 ? $"guard:{GuardLevel}" : $"gift:{GiftId}";
}

/// <summary>The 盲盒 a gift was opened from: the box bought and the price of one.</summary>
internal sealed record BlindGift(long Id, string Name, long Price);

/// <summary>A viewer's paid gifts after three seconds of silence; amounts are in gold coins.</summary>
public sealed record GiftRoundEnded(long RoomId, long Uid, string Nickname, long Spend,
    DateTimeOffset EndedAt, long EndedTimestamp) : WatchUpdate;
