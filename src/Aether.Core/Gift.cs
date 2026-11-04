namespace Aether.Core;

/// <summary>A viewer's paid gift, or a 大航海 purchase. Amounts are integer 金瓜子: Spend is its 投入, Price one gift's price.
/// A purchase has the GuardLevel B站 gives it (1 总督, 2 提督, 3 舰长; 0 for a gift) and no IconUrl; its payflow_id is the Tid.</summary>
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
