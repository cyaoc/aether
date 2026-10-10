using System.Text.Json;

namespace Aether.Core.Gifts;

internal static class SendGiftV2
{
    // Field numbers: blivedm PR #86, issuecomment-5104705486 (SendGiftBroadcast, BlindGift, GiftItem).
    public static IEnumerable<GiftMessages.Gift> Decode(JsonElement data)
    {
        var pb = data.TryGetProperty("pb", out var direct) ? direct : data.GetProperty("data").GetProperty("pb");
        var reader = new ProtobufReader(Convert.FromBase64String(pb.GetString() ?? throw new JsonException("缺少 pb。")));
        var box = new BlindBox(0, "", 0, "", 0, 0, "", 0, 0, 0, 0, 0, null, false, "");
        List<ReadOnlyMemory<byte>> items = [];
        while (reader.TryRead(out var field, out var type))
        {
            switch (field)
            {
                case 1: box = box with { Uid = reader.Int64(type) }; break;
                case 2: box = box with { Nickname = reader.Text(type) }; break;
                case 9: box = ReadBlindGift(box, reader.Bytes(type)); break;
                case 10: items.Add(reader.Bytes(type)); break;
                case 11: box = box with { Shown = reader.Int64(type) != 0 }; break;
                default: reader.Skip(field, type); break;
            }
        }
        foreach (var item in items)
        {
            var (coinType, opened) = ReadItem(box, item);
            yield return new GiftMessages.Gift(box.BlindGiftId != 0, () => coinType, () => opened);
        }
    }

    private static BlindBox ReadBlindGift(BlindBox box, ReadOnlyMemory<byte> bytes)
    {
        var reader = new ProtobufReader(bytes);
        while (reader.TryRead(out var field, out var type))
        {
            switch (field)
            {
                case 2: box = box with { BlindGiftId = reader.Int64(type) }; break;
                case 3: box = box with { BlindGiftName = reader.Text(type) }; break;
                case 6: box = box with { BlindGiftPrice = reader.Int64(type) }; break;
                default: reader.Skip(field, type); break;
            }
        }
        return box;
    }

    private static (string CoinType, BlindBox Box) ReadItem(BlindBox box, ReadOnlyMemory<byte> bytes)
    {
        var coinType = "";
        var reader = new ProtobufReader(bytes);
        while (reader.TryRead(out var field, out var type))
        {
            switch (field)
            {
                case 1: box = box with { OpenedGiftId = reader.Int64(type) }; break;
                case 2: box = box with { OpenedGiftName = reader.Text(type) }; break;
                case 3: box = box with { Num = reader.Int64(type) }; break;
                case 5: box = box with { OpenedGiftPrice = reader.Int64(type) }; break;
                case 7: box = box with { Spend = reader.Int64(type) }; break;
                case 8: coinType = reader.Text(type); break;
                case 9: box = box with { Tid = reader.Text(type) }; break;
                case 10: box = box with { Timestamp = reader.Int64(type) }; break;
                default: reader.Skip(field, type); break;
            }
        }
        return (coinType, box);
    }
}
