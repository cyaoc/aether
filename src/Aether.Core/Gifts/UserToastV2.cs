using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Gifts;

/// <summary>A viewer's 大航海 purchase (开通 or 续费, on the 大航海 panel or as a 舰长一号, 提督一号 or 总督一号 from the gift panel) as a
/// Gift.</summary>
internal static class UserToastV2
{
    // option.source of a purchase made on the 大航海 panel, and of a 舰长一号, 提督一号 or 总督一号 sent from the gift panel: the
    // only two ever captured (2026-11-27).
    private const long PanelSource = 0, GiftPanelSource = 1;

    // pay_info.unit of an ordinary purchase; with any other unit (e.g. "5天") the purchase counts once.
    private const string MonthUnit = "月";

    // One month's list price in 金瓜子 (10 电池 = 1 元 = 1000 金瓜子), from the price list the streamer gave in 2026-11;
    // change them when B站 reprices.
    private const long CaptainMonthlyPrice = 198000; // 舰长 1980 电池
    private const long AdmiralMonthlyPrice = 1998000; // 提督 19980 电池
    private const long GovernorMonthlyPrice = 19998000; // 总督 199980 电池

    public static Gift[] Decode(JsonElement data, ILogger logger)
    {
        // Every push counts whatever its option.source (#427). One of a source never captured, or of none that reads as a
        // number, is logged whole and goes by the same rules; one pushed again with the same payflow_id counts once, as any
        // Tid does.
        var source = GiftMessages.OptionalNumber(data, "option", "source");
        if (source is not (PanelSource or GiftPanelSource))
            logger.LogInformation("大航海的 source 是 {Source}，没有见过这种，按一般的规则算，上实战时核对；原始 JSON：{Json}",
                source, data.GetRawText());
        var pay = data.GetProperty("pay_info");
        var level = data.GetProperty("guard_info").GetProperty("guard_level").GetInt32();
        var price = pay.GetProperty("price").GetInt64();
        var num = pay.GetProperty("num").GetInt64();
        var unit = pay.GetProperty("unit").GetString() ?? throw new JsonException("缺少大航海单位。");
        if (num <= 0 || price < 0) throw new FormatException("大航海的个数或价格无效。");
        var (spend, months) = Cost(MonthlyPrice(level), price, num, unit);
        // Free like a free gift: nothing was spent, so it neither counts toward a round nor goes on the board.
        if (price == 0) return [];
        if (num > 1 || unit != MonthUnit)
            logger.LogInformation("大航海的 num 大于 1 或 unit 不是“月”，price 的含义未验证，上实战时核对：guard_level {GuardLevel}，num {Num}，price {Price}，unit {Unit}",
                level, num, price, unit);
        return [ToGift(data, level, spend, months)];
    }

    private static Gift ToGift(JsonElement data, int level, long spend, long months)
    {
        var sender = data.GetProperty("sender_uinfo");
        var viewer = sender.GetProperty("base");
        var uid = sender.GetProperty("uid").GetInt64();
        if (uid < 0) throw new FormatException("大航海的 uid 无效。");
        return new Gift(uid, viewer.GetProperty("name").GetString() ?? throw new JsonException("缺少观众昵称。"), spend)
        {
            GiftId = data.GetProperty("gift_info").GetProperty("gift_id").GetInt64(),
            GiftName = Guard.Name(Guard.TierFromPrivilegeType(level)), Price = spend / months, Num = months, Value = spend,
            FaceUrl = GiftMessages.OptionalText(viewer, "face"),
            Tid = data.GetProperty("pay_info").GetProperty("payflow_id").GetString() is { Length: > 0 } payflowId ? payflowId : null,
            GuardLevel = level, ViewerGuardLevel = level
        };
    }

    /// <summary>The 投入 and the months of a purchase. A 12-month 续费 captured 2026-11-27 pushed the total as price
    /// (2016000 for 12); whether B站 ever pushes a unit price for num above 1 is not known, so up to the one-month list price
    /// it is read as the unit price and above it as the total (a month's list price is the most any unit price can be; any
    /// total above one month is more than that).</summary>
    private static (long Spend, long Months) Cost(long monthlyPrice, long price, long num, string unit) =>
        unit != MonthUnit ? (price, 1)
        : price <= monthlyPrice ? (checked(price * num), num)
        : (price, num);

    // B站's guard_level: 1 总督, 2 提督, 3 舰长.
    private static long MonthlyPrice(int level) => level switch
    {
        1 => GovernorMonthlyPrice,
        2 => AdmiralMonthlyPrice,
        3 => CaptainMonthlyPrice,
        _ => throw new FormatException("大航海等级无效。")
    };
}
