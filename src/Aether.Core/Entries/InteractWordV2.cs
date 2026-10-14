using System.Text.Json;

namespace Aether.Core.Entries;

internal static class InteractWordV2
{
    // Field numbers: xuserreward/v1.proto and component/common_model/user_dagw.proto at
    // d487dc3f1a61bc810e8ac4e40e4e9e55b175f959 in https://github.com/pskdje/bilibili_live_message_stream (#73, #117).
    public static (Entry Entry, long FanClubRoom) Decode(JsonElement data)
    {
        var reader = new ProtobufReader(Convert.FromBase64String(data.GetProperty("pb").GetString()
            ?? throw new JsonException("缺少 pb。")));
        // proto3 omits zero and empty values, including a missing medal or wealth message.
        var entry = new Entry(0, "", 0, 0, 0, 0);
        long fanClubLevel = 0, fanClubRoom = 0, wealthLevel = 0;
        long? nestedGuard = null;
        while (reader.TryRead(out var field, out var type))
        {
            switch (field)
            {
                case 1: entry = entry with { Uid = reader.Int64(type) }; break;
                case 2: entry = entry with { Nickname = reader.Text(type) }; break;
                case 5: entry = entry with { MessageType = reader.Int64(type) }; break;
                case 9: (fanClubLevel, fanClubRoom) = ReadFanClub(reader.Bytes(type), fanClubLevel, fanClubRoom); break;
                case 16: entry = entry with { PrivilegeType = reader.Int64(type) }; break;
                case 22: (wealthLevel, nestedGuard) = ReadUser(reader.Bytes(type), wealthLevel, nestedGuard); break;
                default: reader.Skip(field, type); break;
            }
        }
        // A present uinfo.guard wins even at level 0 (#117); a negative privilege_type is kept so validation rejects it.
        return (entry with
        {
            FanClubLevel = fanClubLevel, WealthLevel = wealthLevel,
            PrivilegeType = entry.PrivilegeType < 0 ? entry.PrivilegeType : nestedGuard ?? entry.PrivilegeType
        }, fanClubRoom);
    }

    /// <summary>The worn fan club's level and its room, merged over earlier fans_medal.</summary>
    private static (long Level, long Room) ReadFanClub(ReadOnlyMemory<byte> bytes, long level, long room)
    {
        var medal = new ProtobufReader(bytes);
        while (medal.TryRead(out var field, out var type))
            switch (field)
            {
                case 2: level = medal.Int64(type); break;
                case 12: room = medal.Int64(type); break;
                default: medal.Skip(field, type); break;
            }
        return (level, room);
    }

    /// <summary>uinfo's wealth and guard levels merged over earlier uinfo; Guard stays null without a guard message.</summary>
    private static (long WealthLevel, long? Guard) ReadUser(ReadOnlyMemory<byte> bytes, long wealthLevel, long? guard)
    {
        var user = new ProtobufReader(bytes);
        while (user.TryRead(out var field, out var type))
            switch (field)
            {
                case 4: wealthLevel = ReadLevel(user.Bytes(type), wealthLevel); break;
                case 6: guard = ReadLevel(user.Bytes(type), guard ?? 0); break;
                default: user.Skip(field, type); break;
            }
        return (wealthLevel, guard);
    }

    /// <summary>Field 1 of a wealth or guard message, merged over <paramref name="level"/>.</summary>
    private static long ReadLevel(ReadOnlyMemory<byte> bytes, long level)
    {
        var reader = new ProtobufReader(bytes);
        while (reader.TryRead(out var field, out var type))
            if (field == 1) level = reader.Int64(type);
            else reader.Skip(field, type);
        return level;
    }
}
