using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Gifts;

internal static class GiftMessages
{
    private static readonly Dictionary<string, (string Version, Func<JsonElement, IEnumerable<(string? CoinType, BlindBox Box)>> Decode)> Decoders = new()
    {
        ["SEND_GIFT"] = ("V1", SendGiftV1.Decode),
        ["SEND_GIFT_V2"] = ("V2", SendGiftV2.Decode)
    };

    public static string? Version(string command) => Decoders.TryGetValue(command, out var decoder) ? decoder.Version : null;

    public static Gift[] Decode(string command, JsonElement root, ILogger logger)
    {
        if (command == "USER_TOAST_MSG_V2") return UserToastV2.Decode(root.GetProperty("data"), logger);
        if (!Decoders.TryGetValue(command, out var decoder)) return [];
        List<Gift> result = [];
        foreach (var (coinType, box) in decoder.Decode(root.GetProperty("data")))
        {
            // No source has ever shown a non-gold blind box; skip it, but loudly.
            if (coinType != "gold")
            {
                if (box.BlindGiftId != 0) logger.LogWarning("收到 coin_type 为 {CoinType} 的盲盒，从未见过这种情况，未记录", coinType);
                continue;
            }
            Validate(box);
            var tid = string.IsNullOrEmpty(box.Tid) ? null : box.Tid;
            result.Add(new Gift(box.Uid, box.Nickname, box.Spend, box.BlindGiftId == 0 ? null : box with
            {
                OpenedValue = checked(box.OpenedGiftPrice * box.Num),
                Tid = tid
            })
            {
                GiftId = box.OpenedGiftId, GiftName = box.OpenedGiftName, Price = box.OpenedGiftPrice, Num = box.Num,
                FaceUrl = box.FaceUrl, IconUrl = box.IconUrl, Tid = tid
            });
        }
        return result.ToArray();
    }

    // Checked here for every version: protobuf leaves zero and empty fields out, so a missing send time or name
    // arrives as 0 or "" rather than failing to parse.
    private static void Validate(BlindBox box)
    {
        if (box.Uid < 0 || box.BlindGiftId < 0 || box.OpenedGiftId <= 0 || box.BlindGiftPrice < 0
            || box.OpenedGiftPrice < 0 || box.Num <= 0 || box.Spend < 0 || box.Timestamp <= 0
            || (box.BlindGiftId != 0 && box.BlindGiftName.Length == 0) || box.OpenedGiftName.Length == 0)
            throw new FormatException("盲盒的标识、名称、金额、个数或送出时间无效。");
    }
}
