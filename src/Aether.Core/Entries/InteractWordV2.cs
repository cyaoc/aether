using System.Text.Json;

namespace Aether.Core.Entries;

internal static class InteractWordV2
{
    // Field numbers: xuserreward/v1.proto at d487dc3f1a61bc810e8ac4e40e4e9e55b175f959 in
    // https://github.com/pskdje/bilibili_live_message_stream (InteractWordV2), recorded in issue #73.
    public static (Entry Entry, long FanClubRoom) Decode(JsonElement data)
    {
        var reader = new ProtobufReader(Convert.FromBase64String(data.GetProperty("pb").GetString()
            ?? throw new JsonException("缺少 pb。")));
        // proto3 omits zero and empty values, including a missing medal or wealth message.
        var entry = new Entry(0, "", 0, 0, 0, 0);
        long fanClubRoom = 0;
        while (reader.TryRead(out var field, out var type))
        {
            switch (field)
            {
                case 1: entry = entry with { Uid = reader.Int64(type) }; break;
                case 2: entry = entry with { Nickname = reader.Text(type) }; break;
                case 5: entry = entry with { MessageType = reader.Int64(type) }; break;
                case 9:
                    var medal = new ProtobufReader(reader.Bytes(type));
                    while (medal.TryRead(out var medalField, out var medalType))
                        switch (medalField)
                        {
                            case 2: entry = entry with { FanClubLevel = medal.Int64(medalType) }; break;
                            case 12: fanClubRoom = medal.Int64(medalType); break;
                            default: medal.Skip(medalField, medalType); break;
                        }
                    break;
                case 16: entry = entry with { Guard = reader.Int64(type) }; break;
                case 22: entry = entry with { WealthLevel = ReadWealth(reader.Bytes(type)) }; break;
                default: reader.Skip(field, type); break;
            }
        }
        return (entry, fanClubRoom);
    }

    private static long ReadWealth(ReadOnlyMemory<byte> bytes)
    {
        long level = 0;
        var user = new ProtobufReader(bytes);
        while (user.TryRead(out var field, out var type))
        {
            if (field != 4) { user.Skip(field, type); continue; }
            var wealth = new ProtobufReader(user.Bytes(type));
            while (wealth.TryRead(out var wealthField, out var wealthType))
                if (wealthField == 1) level = wealth.Int64(wealthType);
                else wealth.Skip(wealthField, wealthType);
        }
        return level;
    }
}
