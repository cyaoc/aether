using System.Text.Json;

namespace Aether.Core.Gifts;

internal static class SendGiftV1
{
    public static IEnumerable<(string? CoinType, BlindBox Box)> Decode(JsonElement data)
    {
        if (!data.TryGetProperty("blind_gift", out var blind) || blind.ValueKind == JsonValueKind.Null) yield break;
        yield return (data.GetProperty("coin_type").GetString(), new BlindBox(
            Uid: data.GetProperty("uid").GetInt64(),
            Nickname: data.GetProperty("uname").GetString() ?? throw new JsonException("缺少观众昵称。"),
            BlindGiftId: blind.GetProperty("original_gift_id").GetInt64(),
            BlindGiftName: blind.GetProperty("original_gift_name").GetString() ?? throw new JsonException("缺少盲盒名称。"),
            BlindGiftPrice: blind.GetProperty("original_gift_price").GetInt64(),
            OpenedGiftId: data.GetProperty("giftId").GetInt64(),
            OpenedGiftName: data.GetProperty("giftName").GetString() ?? throw new JsonException("缺少开出礼物名称。"),
            OpenedGiftPrice: data.GetProperty("price").GetInt64(),
            Num: data.GetProperty("num").GetInt64(),
            Spend: data.GetProperty("total_coin").GetInt64(),
            OpenedValue: 0,
            Timestamp: data.GetProperty("timestamp").GetInt64(),
            // BAC types tid as num although every sample so far is a string; either way it is kept as text.
            Tid: !data.TryGetProperty("tid", out var id) ? null
                : id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString(),
            // JSON spells false out, so only an explicit false hides the gift; a message without switch counts as shown.
            Shown: !data.TryGetProperty("switch", out var shown) || shown.ValueKind != JsonValueKind.False,
            RawMessage: ""));
    }
}
