using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One blind box gift item: the box bought (BlindGift*) and the gift it opened (OpenedGift*). Amounts are
/// integer 金瓜子, Timestamp is B站's send time in Unix seconds, and Shown is the message's switch.</summary>
internal sealed record BlindBox(long Uid, string Nickname, long BlindGiftId, string BlindGiftName,
    long BlindGiftPrice, long OpenedGiftId, string OpenedGiftName, long OpenedGiftPrice, long Num, long Spend,
    long OpenedValue, long Timestamp, string? Tid, bool Shown, string RawMessage);

/// <summary>投入 and 开出价值 in 金瓜子: one viewer's 盲盒统计, or the part of a 一轮盲盒 one announcement reports.</summary>
internal sealed record BlindBoxTally(long Spend, long OpenedValue)
{
    /// <summary>The 盲盒查询 reply; a null tally means no blind box was recorded.</summary>
    public static string Reply(BlindBoxTally? tally) => tally is null ? "今日没有盲盒记录" : Amounts(tally);

    /// <summary>The 盲盒播报 for a 一轮盲盒's amounts, worded like the reply so viewers can compare the two.</summary>
    public static string Announcement(BlindBoxTally round) => "本次" + Amounts(round);

    private static string Amounts(BlindBoxTally tally)
    {
        var spend = Batteries(tally.Spend);
        var profit = Batteries(tally.OpenedValue - tally.Spend);
        var result = profit == 0 ? "不赚不亏" : $"{(profit > 0 ? "赚" : "亏")}{Text(Math.Abs(profit))}电池";
        return $"投入{Text(spend)}电池 {result}";

        // Round to one decimal first, so whole numbers drop the ".0" and the words always match the number shown.
        static decimal Batteries(long coins) => Math.Round(coins / 100m, 1, MidpointRounding.AwayFromZero);
        static string Text(decimal batteries) => batteries.ToString(batteries % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
    }
}

internal sealed class BlindBoxStore(Database database, ILogger logger)
{
    /// <summary>The viewer's 盲盒统计 in this room for the Beijing day containing <paramref name="at"/>; null when they have none.</summary>
    public BlindBoxTally? Tally(long roomId, long uid, DateTimeOffset at)
    {
        var day = at.ToOffset(TimeSpan.FromHours(8));
        var start = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, day.Offset);
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SUM(spend), SUM(opened_value) FROM blind_box
            WHERE room_id = $roomId AND uid = $uid AND timestamp >= $start AND timestamp < $end;
            """;
        command.Parameters.AddWithValue("$roomId", roomId);
        command.Parameters.AddWithValue("$uid", uid);
        command.Parameters.AddWithValue("$start", start.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$end", start.AddDays(1).ToUnixTimeSeconds());
        using var reader = command.ExecuteReader();
        reader.Read();
        return reader.IsDBNull(0) ? null : new BlindBoxTally(reader.GetInt64(0), reader.GetInt64(1));
    }

    /// <summary>True only when <paramref name="blindBox"/> is newly recorded: not uid 0 and not a tid seen before.</summary>
    public bool Save(long roomId, BlindBox blindBox)
    {
        if (blindBox.Uid == 0)
        {
            // Shown at Information: whether 神秘人 gifts arrive with uid 0 is unverified, and these go uncounted.
            logger.LogInformation("uid 为 0 的盲盒未记录（可能是神秘人），盲盒 {BlindGift}，开出礼物 {Gift}，个数 {Num}，投入 {Spend} 金瓜子，tid：{Tid}",
                blindBox.BlindGiftName, blindBox.OpenedGiftName, blindBox.Num, blindBox.Spend, blindBox.Tid);
            return false;
        }
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO blind_box (room_id, uid, nickname, blind_gift_id, blind_gift_name, blind_gift_price,
                opened_gift_id, opened_gift_name, opened_gift_price, num, spend, opened_value, timestamp, tid, raw_message)
            VALUES ($roomId, $uid, $nickname, $blindGiftId, $blindGiftName, $blindGiftPrice,
                $openedGiftId, $openedGiftName, $openedGiftPrice, $num, $spend, $openedValue, $timestamp, $tid, $rawMessage)
            ON CONFLICT (tid) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$roomId", roomId);
        command.Parameters.AddWithValue("$uid", blindBox.Uid);
        command.Parameters.AddWithValue("$nickname", blindBox.Nickname);
        command.Parameters.AddWithValue("$blindGiftId", blindBox.BlindGiftId);
        command.Parameters.AddWithValue("$blindGiftName", blindBox.BlindGiftName);
        command.Parameters.AddWithValue("$blindGiftPrice", blindBox.BlindGiftPrice);
        command.Parameters.AddWithValue("$openedGiftId", blindBox.OpenedGiftId);
        command.Parameters.AddWithValue("$openedGiftName", blindBox.OpenedGiftName);
        command.Parameters.AddWithValue("$openedGiftPrice", blindBox.OpenedGiftPrice);
        command.Parameters.AddWithValue("$num", blindBox.Num);
        command.Parameters.AddWithValue("$spend", blindBox.Spend);
        command.Parameters.AddWithValue("$openedValue", blindBox.OpenedValue);
        command.Parameters.AddWithValue("$timestamp", blindBox.Timestamp);
        command.Parameters.AddWithValue("$tid", (object?)blindBox.Tid ?? DBNull.Value);
        command.Parameters.AddWithValue("$rawMessage", blindBox.RawMessage);
        if (command.ExecuteNonQuery() == 0)
        {
            // Name the ignored amounts, so a shared tid (a broken key assumption) can be told apart and recovered from the log.
            command.CommandText = """
                SELECT COUNT(*) FROM blind_box WHERE tid = $tid AND room_id = $roomId AND uid = $uid
                    AND blind_gift_id = $blindGiftId AND opened_gift_id = $openedGiftId AND num = $num AND spend = $spend;
                """;
            if ((long)command.ExecuteScalar()! > 0)
                logger.LogWarning("重复推送的礼物，已忽略，tid：{Tid}，开出礼物 {Gift}，个数 {Num}，投入 {Spend} 金瓜子",
                    blindBox.Tid, blindBox.OpenedGiftName, blindBox.Num, blindBox.Spend);
            else
                logger.LogWarning("tid {Tid} 已记录过另一份不同的盲盒，这一条未记录，tid 可能不是按礼物项唯一："
                    + "观众 {Nickname}（{Uid}），盲盒 {BlindGift}，开出礼物 {Gift}，个数 {Num}，投入 {Spend} 金瓜子，开出价值 {OpenedValue} 金瓜子",
                    blindBox.Tid, blindBox.Nickname, blindBox.Uid, blindBox.BlindGiftName, blindBox.OpenedGiftName, blindBox.Num, blindBox.Spend, blindBox.OpenedValue);
            return false;
        }
        // Compare without overflowing on an untrusted unit price times quantity.
        if (blindBox.Spend / blindBox.Num != blindBox.BlindGiftPrice || blindBox.Spend % blindBox.Num != 0)
            logger.LogWarning("盲盒投入 {Spend} 不等于盲盒单价 {Price} × 个数 {Num}，仍按 total_coin 记录，tid：{Tid}",
                blindBox.Spend, blindBox.BlindGiftPrice, blindBox.Num, blindBox.Tid);
        // B站's web player only shows a gift when switch is true; what false means is unverified, so keep it and say so.
        if (!blindBox.Shown)
            logger.LogWarning("盲盒消息的 switch 为 false，含义未验证，仍照常记录，tid：{Tid}", blindBox.Tid);
        logger.LogInformation("记录盲盒：观众 {Nickname}（{Uid}），盲盒 {BlindGift}，开出礼物 {Gift}，个数 {Num}，投入 {Spend} 金瓜子，开出价值 {OpenedValue} 金瓜子",
            blindBox.Nickname, blindBox.Uid, blindBox.BlindGiftName, blindBox.OpenedGiftName, blindBox.Num, blindBox.Spend, blindBox.OpenedValue);
        return true;
    }
}
