using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Aether.Core;

/// <summary>One viewer's accumulated gift of one kind on the 礼物榜. Amounts are integer 金瓜子. Id stays the same while the row
/// lives, so the 礼物榜页 can tell a new row from a changed one. GuardLevel is 0 for a gift, else B站's guard_level
/// (1 总督, 2 提督, 3 舰长) and IconUrl is empty.</summary>
public sealed record GiftBoardRow(string Id, string Nickname, string FaceUrl, string GiftName, string IconUrl,
    int GuardLevel, long Price, long Num)
{
    /// <summary>What the 礼物榜 ranks by: the unit price times the count.</summary>
    [JsonIgnore] public long Value => Price * Num;
}

/// <summary>What the 礼物榜页 and the GUI show: the rows in rank order, and the settings the page follows.</summary>
public sealed record GiftBoardState(ImmutableArray<GiftBoardRow> Rows, bool Mascot, int Volume);
