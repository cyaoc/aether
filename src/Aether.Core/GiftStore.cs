using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The 收礼记录 (ADR 0013): one row per paid gift, marked by whether the 盲盒统计 wanted it and the 期 the 礼物榜
/// counted it in. The two read it each in their own way; a push B站 repeats is written once.</summary>
internal sealed class GiftStore(Database database, ILogger logger)
{
    /// <summary>Writes <paramref name="gift"/> marked for the 盲盒统计 and with the board's 期 as given. Returns whether it was
    /// newly written: not for uid 0, nor for a tid written before.</summary>
    public bool Save(long roomId, Gift gift, bool forBlindBox, long? boardPeriod)
    {
        if (gift.Uid == 0)
        {
            // Shown at Information: whether 神秘人 gifts arrive with uid 0 is unverified, and these go uncounted.
            if (gift.Blind is { } box)
                logger.LogInformation("uid 为 0 的盲盒未记录（可能是神秘人），盲盒 {BlindGift}，开出礼物 {Gift}，个数 {Num}，投入 {Spend} 金瓜子，tid：{Tid}",
                    box.Name, gift.GiftName, gift.Num, gift.Spend, gift.Tid);
            else
                logger.LogInformation("uid 为 0 的礼物未记录（可能是神秘人），礼物 {Gift}，个数 {Num}，tid：{Tid}", gift.GiftName, gift.Num, gift.Tid);
            return false;
        }
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO gift_record (room_id, uid, nickname, face_url, gift_id, gift_name, icon_url, guard_level, viewer_guard_level,
                gift_key, num, spend, value, blind_gift_id, blind_gift_name, blind_gift_price, tid, timestamp, for_blind_box, board_period)
            VALUES ($roomId, $uid, $nickname, $faceUrl, $giftId, $giftName, $iconUrl, $guardLevel, $viewerGuardLevel,
                $giftKey, $num, $spend, $value, $blindGiftId, $blindGiftName, $blindGiftPrice, $tid, $timestamp, $forBlindBox, $boardPeriod)
            ON CONFLICT (tid) DO NOTHING
            RETURNING 1;
            """;
        command.Parameters.AddWithValue("$roomId", roomId);
        command.Parameters.AddWithValue("$uid", gift.Uid);
        command.Parameters.AddWithValue("$nickname", gift.Nickname);
        command.Parameters.AddWithValue("$faceUrl", gift.FaceUrl);
        command.Parameters.AddWithValue("$giftId", gift.GiftId);
        command.Parameters.AddWithValue("$giftName", gift.GiftName);
        command.Parameters.AddWithValue("$iconUrl", gift.IconUrl);
        command.Parameters.AddWithValue("$guardLevel", gift.GuardLevel);
        command.Parameters.AddWithValue("$viewerGuardLevel", gift.ViewerGuardLevel);
        command.Parameters.AddWithValue("$giftKey", gift.BoardKey);
        command.Parameters.AddWithValue("$num", gift.Num);
        command.Parameters.AddWithValue("$spend", gift.Spend);
        command.Parameters.AddWithValue("$value", gift.Value);
        command.Set("$blindGiftId", gift.Blind?.Id);
        command.Set("$blindGiftName", gift.Blind?.Name);
        command.Set("$blindGiftPrice", gift.Blind?.Price);
        command.Set("$tid", gift.Tid);
        command.Set("$timestamp", gift.Timestamp);
        command.Parameters.AddWithValue("$forBlindBox", forBlindBox);
        command.Set("$boardPeriod", boardPeriod);
        if (command.ExecuteScalar() is null)
        {
            // Name the ignored amounts, so a shared tid (a broken key assumption) can be told apart and recovered from the log.
            command.CommandText = """
                SELECT COUNT(*) FROM gift_record WHERE tid = $tid AND room_id = $roomId AND uid = $uid
                    AND blind_gift_id IS $blindGiftId AND gift_id = $giftId AND num = $num AND spend = $spend;
                """;
            if ((long)command.ExecuteScalar()! > 0)
                logger.LogWarning("重复推送的礼物，已忽略，tid：{Tid}，开出礼物 {Gift}，个数 {Num}，投入 {Spend} 金瓜子",
                    gift.Tid, gift.GiftName, gift.Num, gift.Spend);
            else
                logger.LogWarning("tid {Tid} 已记录过另一份不同的礼物，这一条未记录，tid 可能不是按礼物项唯一："
                    + "观众 {Nickname}（{Uid}），盲盒 {BlindGift}，开出礼物 {Gift}，个数 {Num}，投入 {Spend} 金瓜子，开出价值 {OpenedValue} 金瓜子",
                    gift.Tid, gift.Nickname, gift.Uid, gift.Blind?.Name, gift.GiftName, gift.Num, gift.Spend, gift.Value);
            return false;
        }
        if (forBlindBox && gift.Blind is { } blind) LogBlindBox(gift, blind);
        return true;
    }

    private void LogBlindBox(Gift gift, BlindGift blind)
    {
        // Compare without overflowing on an untrusted unit price times quantity.
        if (gift.Spend / gift.Num != blind.Price || gift.Spend % gift.Num != 0)
            logger.LogWarning("盲盒投入 {Spend} 不等于盲盒单价 {Price} × 个数 {Num}，仍按 total_coin 记录，tid：{Tid}",
                gift.Spend, blind.Price, gift.Num, gift.Tid);
        // B站's web player only shows a gift when switch is true; what false means is unverified, so keep it and say so.
        if (!gift.Shown)
            logger.LogWarning("盲盒消息的 switch 为 false，含义未验证，仍照常记录，tid：{Tid}", gift.Tid);
        logger.LogInformation("记录盲盒：观众 {Nickname}（{Uid}），盲盒 {BlindGift}，开出礼物 {Gift}，个数 {Num}，投入 {Spend} 金瓜子，开出价值 {OpenedValue} 金瓜子",
            gift.Nickname, gift.Uid, blind.Name, gift.GiftName, gift.Num, gift.Spend, gift.Value);
    }
}
