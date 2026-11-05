using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Aether.Core;

/// <summary>One viewer's accumulated gift of one kind on the 礼物榜. Amounts are integer 金瓜子, and Value, what the 礼物榜 ranks
/// by, is everything the row's gifts were worth. Id stays the same while the row lives, so the 礼物榜页 can tell a new row from a
/// changed one. GuardLevel is 0 for a gift, else B站's guard_level (1 总督, 2 提督, 3 舰长) and IconUrl is empty.</summary>
public sealed record GiftBoardRow(string Id, long Uid, string Nickname, string FaceUrl, string GiftName, string IconUrl,
    int GuardLevel, long Value, long Num)
{
    /// <summary>The unit price, averaged over the count when the row's gifts were bought at different prices.</summary>
    [JsonIgnore] public long Price => Value / Num;
}

/// <summary>A 期's options (ADR 0014): the 上榜门槛 in 电池 and what it compares (<see cref="ByUnitPrice"/> or
/// <see cref="ByTotalPrice"/>), whether the 礼物榜页 shows the mascot, and the volume (0–100) of the sound for a new first place.
/// They apply at once, and a new 期 starts with the last one's.</summary>
public sealed record GiftBoardOptions(int Threshold, int ThresholdBy, bool Mascot, int Volume)
{
    public const int ByUnitPrice = 1, ByTotalPrice = 2;

    public static readonly GiftBoardOptions Default = new(100, ByUnitPrice, true, 60);

    /// <summary>Why the options cannot be used, or null when they can.</summary>
    public string? Error => Threshold < 1 ? "上榜门槛必须是正整数。" : ThresholdBy is not (ByUnitPrice or ByTotalPrice) ? "门槛比法只能是 1 或 2。"
        : Volume is < 0 or > 100 ? "音量必须是 0 到 100 的整数。" : null;
}

/// <summary>One 期 of a room's 礼物榜: when it began, and when it ended, null while it is running.</summary>
public sealed record GiftBoardPeriod(DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

/// <summary>What the 礼物榜页 and the GUI show: the rows of the room's latest 期 in rank order and its options. Shown is true
/// only while that 期 is running; otherwise the 礼物榜页 draws nothing, while the GUI still lists an ended 期's rows. Connected is
/// whether a room is connected; Period is the latest 期, null when the room has never had one or no room is connected.</summary>
public sealed record GiftBoardState(ImmutableArray<GiftBoardRow> Rows, bool Shown, GiftBoardOptions Options, bool Connected,
    GiftBoardPeriod? Period)
{
    /// <summary>True only for the state a received gift made. The 礼物榜页 plays the change as an animation only then: any other
    /// state, such as new options, a 期 started, ended or continued, or a subscriber's first, is drawn as it is.</summary>
    public bool FromGift { get; init; }

    // What the 礼物榜页 reads of the options.
    public bool Mascot => Options.Mascot;
    public int Volume => Options.Volume;
}
