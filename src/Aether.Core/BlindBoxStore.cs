using Microsoft.Extensions.Logging;

namespace Aether.Core;

internal sealed record BlindBox(long Uid, string Nickname, long BlindGiftId, string BlindGiftName,
    long BlindGiftPrice, long GiftId, string GiftName, long GiftPrice, long Num, long Spend,
    long OpenedValue, long Timestamp, string? Tid, string RawMessage);

internal sealed class BlindBoxStore(Database database, ILogger logger)
{
    public void Save(long roomId, BlindBox gift)
    {
        if (gift.Uid == 0)
        {
            logger.LogDebug("忽略 uid 为 0 的盲盒，tid：{Tid}", gift.Tid);
            return;
        }
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO blind_box (room_id, uid, nickname, blind_gift_id, blind_gift_name, blind_gift_price,
                gift_id, gift_name, gift_price, num, spend, opened_value, timestamp, tid, raw_message)
            VALUES ($roomId, $uid, $nickname, $blindGiftId, $blindGiftName, $blindGiftPrice,
                $giftId, $giftName, $giftPrice, $num, $spend, $openedValue, $timestamp, $tid, $rawMessage)
            ON CONFLICT (tid) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$roomId", roomId);
        command.Parameters.AddWithValue("$uid", gift.Uid);
        command.Parameters.AddWithValue("$nickname", gift.Nickname);
        command.Parameters.AddWithValue("$blindGiftId", gift.BlindGiftId);
        command.Parameters.AddWithValue("$blindGiftName", gift.BlindGiftName);
        command.Parameters.AddWithValue("$blindGiftPrice", gift.BlindGiftPrice);
        command.Parameters.AddWithValue("$giftId", gift.GiftId);
        command.Parameters.AddWithValue("$giftName", gift.GiftName);
        command.Parameters.AddWithValue("$giftPrice", gift.GiftPrice);
        command.Parameters.AddWithValue("$num", gift.Num);
        command.Parameters.AddWithValue("$spend", gift.Spend);
        command.Parameters.AddWithValue("$openedValue", gift.OpenedValue);
        command.Parameters.AddWithValue("$timestamp", gift.Timestamp);
        command.Parameters.AddWithValue("$tid", (object?)gift.Tid ?? DBNull.Value);
        command.Parameters.AddWithValue("$rawMessage", gift.RawMessage);
        if (command.ExecuteNonQuery() == 0)
        {
            logger.LogWarning("重复推送的礼物，已忽略，tid：{Tid}", gift.Tid);
            return;
        }
        // Compare without overflowing on an untrusted unit price times quantity.
        if (gift.Spend / gift.Num != gift.BlindGiftPrice || gift.Spend % gift.Num != 0)
            logger.LogWarning("盲盒投入 {Spend} 不等于盲盒单价 {Price} × 个数 {Num}，仍按 total_coin 记录，tid：{Tid}",
                gift.Spend, gift.BlindGiftPrice, gift.Num, gift.Tid);
        logger.LogInformation("记录盲盒：观众 {Nickname}（{Uid}），盲盒 {BlindGift}，开出礼物 {Gift}，个数 {Num}，投入 {Spend} 金瓜子，开出价值 {OpenedValue} 金瓜子",
            gift.Nickname, gift.Uid, gift.BlindGiftName, gift.GiftName, gift.Num, gift.Spend, gift.OpenedValue);
    }
}
