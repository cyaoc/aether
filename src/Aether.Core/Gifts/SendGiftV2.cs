using System.Text.Json;

namespace Aether.Core.Gifts;

internal static class SendGiftV2
{
    // Field numbers: blivedm PR #86, issuecomment-5104705486 (SendGiftBroadcast, BlindGift, GiftItem); the gift icon
    // (GiftItem 35 -> img_basic 1) is from blivedm's models/pb.py (SendGiftV2GiftMaterialSnapShot).
    public static IEnumerable<(string? CoinType, Gift Gift)> Decode(JsonElement data)
    {
        var pb = data.TryGetProperty("pb", out var direct) ? direct : data.GetProperty("data").GetProperty("pb");
        var reader = new ProtobufReader(Convert.FromBase64String(pb.GetString() ?? throw new JsonException("缺少 pb。")));
        // proto3 leaves zero, empty and false fields out, so these defaults are what an absent field means; in particular
        // an absent switch (field 11) is false. GiftMessages fills Value and rejects missing ones.
        var gift = new Gift(Uid: 0, Nickname: "", Spend: 0) { Timestamp = 0, Shown = false };
        List<ReadOnlyMemory<byte>> items = [];
        while (reader.TryRead(out var field, out var type))
        {
            switch (field)
            {
                case 1: gift = gift with { Uid = reader.Int64(type) }; break;
                case 2: gift = gift with { Nickname = reader.Text(type) }; break;
                case 3: gift = gift with { FaceUrl = reader.Text(type) }; break;
                case 9: gift = gift with { Blind = ReadBlindGift(gift.Blind, reader.Bytes(type)) }; break;
                case 10: items.Add(reader.Bytes(type)); break;
                case 11: gift = gift with { Shown = reader.Int64(type) != 0 }; break;
                default: reader.Skip(field, type); break;
            }
        }
        foreach (var item in items) yield return ReadItem(gift, item);
    }

    private static string ReadIconUrl(ReadOnlyMemory<byte> snapshot) =>
        ProtobufReader.LastValue(snapshot, 1, (reader, type) => reader.Text(type), "");

    // A message field that occurs again is merged into what came before, as protobuf does.
    private static BlindGift ReadBlindGift(BlindGift? before, ReadOnlyMemory<byte> bytes)
    {
        var blind = before ?? new BlindGift(0, "", 0);
        var reader = new ProtobufReader(bytes);
        while (reader.TryRead(out var field, out var type))
        {
            switch (field)
            {
                case 2: blind = blind with { Id = reader.Int64(type) }; break;
                case 3: blind = blind with { Name = reader.Text(type) }; break;
                case 6: blind = blind with { Price = reader.Int64(type) }; break;
                default: reader.Skip(field, type); break;
            }
        }
        return blind;
    }

    private static (string CoinType, Gift Gift) ReadItem(Gift gift, ReadOnlyMemory<byte> bytes)
    {
        var coinType = "";
        var reader = new ProtobufReader(bytes);
        while (reader.TryRead(out var field, out var type))
        {
            switch (field)
            {
                case 1: gift = gift with { GiftId = reader.Int64(type) }; break;
                case 2: gift = gift with { GiftName = reader.Text(type) }; break;
                case 3: gift = gift with { Num = reader.Int64(type) }; break;
                case 5: gift = gift with { Price = reader.Int64(type) }; break;
                case 7: gift = gift with { Spend = reader.Int64(type) }; break;
                case 8: coinType = reader.Text(type); break;
                case 9: gift = gift with { Tid = reader.Text(type) }; break;
                case 10: gift = gift with { Timestamp = reader.Int64(type) }; break;
                case 35: gift = gift with { IconUrl = ReadIconUrl(reader.Bytes(type)) }; break;
                default: reader.Skip(field, type); break;
            }
        }
        return (coinType, gift);
    }
}
