using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Gifts;

internal static class GiftMessages
{
    private static readonly Dictionary<string, Func<JsonElement, IEnumerable<Gift>>> Decoders = new()
    {
        ["SEND_GIFT"] = SendGiftV1.Decode
    };

    // Defer detail reads so ignored ordinary/non-gold gifts need not have valid blind box fields.
    internal sealed record Gift(bool IsBlindBox, Func<string?> CoinType, Func<BlindBox> Read);

    public static BlindBox[] Decode(string command, JsonElement root, ReadOnlyMemory<byte> body, ILogger logger)
    {
        if (!Decoders.TryGetValue(command, out var decode)) return [];
        List<BlindBox> result = [];
        foreach (var gift in decode(root.GetProperty("data")))
        {
            if (!gift.IsBlindBox) continue;
            if (gift.CoinType() is var coinType && coinType != "gold")
            {
                logger.LogWarning("收到 coin_type 为 {CoinType} 的盲盒，从未见过这种情况，未记录", coinType);
                continue;
            }
            var box = gift.Read();
            if (box.Uid < 0 || box.BlindGiftId <= 0 || box.OpenedGiftId <= 0 || box.BlindGiftPrice < 0
                || box.OpenedGiftPrice < 0 || box.Num <= 0 || box.Spend < 0 || box.Timestamp < 0)
                throw new JsonException("盲盒的标识、金额、个数或送出时间无效。");
            result.Add(box with
            {
                OpenedValue = checked(box.OpenedGiftPrice * box.Num),
                Tid = string.IsNullOrEmpty(box.Tid) ? null : box.Tid,
                RawMessage = Encoding.UTF8.GetString(body.Span)
            });
        }
        return result.ToArray();
    }
}
