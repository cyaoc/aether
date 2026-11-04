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

/// <summary>One 期 of a room's 礼物榜. Viewers counts everyone with a gift in it, whatever the 上榜门槛, so 0 means the 期 is empty.</summary>
public sealed record GiftBoardPeriod(DateTimeOffset StartedAt, int Viewers);

/// <summary>What the 礼物榜页 and the GUI show: the rows in rank order, and the settings the page follows. Period is the
/// 期 the rows come from and Previous the one <see cref="GiftBoardController.Restore"/> would merge it into; both are null
/// while no room is connected, Previous also when there is no earlier 期. The 礼物榜页 does not use them.</summary>
public sealed record GiftBoardState(ImmutableArray<GiftBoardRow> Rows, bool Mascot, int Volume,
    GiftBoardPeriod? Period, GiftBoardPeriod? Previous)
{
    /// <summary>True only for the state a received gift made. The 礼物榜页 plays the change as an animation only then: any other
    /// state, such as a saved setting, a clear, a restore or a subscriber's first, is drawn as it is.</summary>
    public bool FromGift { get; init; }
}
