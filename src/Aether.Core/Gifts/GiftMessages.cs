using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Gifts;

internal static class GiftMessages
{
    private static readonly Dictionary<string, (string Version, Func<JsonElement, IEnumerable<(string? CoinType, Gift Gift)>> Decode)> Decoders = new()
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
        foreach (var (coinType, decoded) in decoder.Decode(root.GetProperty("data")))
        {
            // A box of id 0 is no box: the gift was sent directly.
            var gift = decoded.Blind is { Id: 0 } ? decoded with { Blind = null } : decoded;
            // No source has ever shown a non-gold blind box; skip it, but loudly.
            if (coinType != "gold")
            {
                if (gift.Blind is not null) logger.LogWarning("收到 coin_type 为 {CoinType} 的盲盒，从未见过这种情况，未记录", coinType);
                continue;
            }
            Validate(gift);
            result.Add(gift with { Value = checked(gift.Price * gift.Num), Tid = string.IsNullOrEmpty(gift.Tid) ? null : gift.Tid });
        }
        return result.ToArray();
    }

    /// <summary>The text at <paramref name="path"/>, "" when any step is missing or null: avatars and icons are optional, so
    /// a gift without them still counts.</summary>
    internal static string OptionalText(JsonElement element, params string[] path)
    {
        foreach (var name in path)
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return "";
        return element.GetString() ?? "";
    }

    // Checked here for every version: protobuf leaves zero and empty fields out, so a missing send time or name
    // arrives as 0 or "" rather than failing to parse.
    private static void Validate(Gift gift)
    {
        if (gift.Uid < 0 || gift.GiftId <= 0 || gift.Price < 0 || gift.Num <= 0 || gift.Spend < 0 || gift.Timestamp is not > 0
            || gift.GiftName.Length == 0 || gift.Blind is { Id: < 0 } or { Price: < 0 } or { Name.Length: 0 })
            throw new FormatException("盲盒的标识、名称、金额、个数或送出时间无效。");
    }
}
