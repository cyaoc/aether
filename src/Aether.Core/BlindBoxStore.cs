using System.Globalization;

namespace Aether.Core;

internal enum BlindBoxPeriod { Daily, Monthly }

internal static class BlindBoxPeriodName
{
    /// <summary>How replies and logs name the period.</summary>
    public static string Name(this BlindBoxPeriod period) => period == BlindBoxPeriod.Daily ? "今日" : "本月";
}

/// <summary>投入 and 开出价值 in 金瓜子: one viewer's 盲盒统计, or the part of a 一轮盲盒 one announcement reports.</summary>
internal sealed record BlindBoxTally(long Spend, long OpenedValue)
{
    /// <summary>The 盲盒查询 reply; a null tally means no blind box was recorded.</summary>
    public static string Reply(BlindBoxTally? tally, BlindBoxPeriod period) =>
        period.Name() + (tally is null ? "没有盲盒记录" : Amounts(tally));

    /// <summary>The 盲盒播报 for a 一轮盲盒's amounts, worded like the reply so viewers can compare the two.</summary>
    public static string Announcement(BlindBoxTally round) => "本次" + Amounts(round);

    private static string Amounts(BlindBoxTally tally)
    {
        var spend = Batteries(tally.Spend);
        var profit = Batteries(tally.OpenedValue - tally.Spend);
        var result = profit == 0 ? "不赚不亏" : $"{(profit > 0 ? "赚" : "亏")}{Text(Math.Abs(profit))}电池";
        return $"投入{Text(spend)}电池 {result}";

        // Round to one decimal first, so whole numbers drop the ".0" and the words always match the number shown.
        static decimal Batteries(long coins) => Math.Round(Battery.FromCoins(coins), 1, MidpointRounding.AwayFromZero);
        static string Text(decimal batteries) => batteries.ToString(batteries % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
    }
}

internal sealed class BlindBoxStore(Database database)
{
    /// <summary>The viewer's 盲盒统计 in this room for the Beijing day or month containing <paramref name="at"/>: the 盲盒 the
    /// 收礼记录 holds for the 盲盒统计 (ADR 0013). Null when they have none.</summary>
    public BlindBoxTally? Tally(long roomId, long uid, DateTimeOffset at, BlindBoxPeriod period)
    {
        var beijing = at.ToOffset(TimeSpan.FromHours(8));
        var start = new DateTimeOffset(beijing.Year, beijing.Month, period == BlindBoxPeriod.Daily ? beijing.Day : 1, 0, 0, 0, beijing.Offset);
        var end = period == BlindBoxPeriod.Daily ? start.AddDays(1) : start.AddMonths(1);
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SUM(spend), SUM(value) FROM gift_record
            WHERE room_id = $roomId AND uid = $uid AND for_blind_box = 1 AND timestamp >= $start AND timestamp < $end;
            """;
        command.Parameters.AddWithValue("$roomId", roomId);
        command.Parameters.AddWithValue("$uid", uid);
        command.Parameters.AddWithValue("$start", start.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$end", end.ToUnixTimeSeconds());
        using var reader = command.ExecuteReader();
        reader.Read();
        return reader.IsDBNull(0) ? null : new BlindBoxTally(reader.GetInt64(0), reader.GetInt64(1));
    }
}
