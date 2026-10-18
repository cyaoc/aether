using System.Text.Json;

namespace Aether.Core.Entries;

/// <summary>A viewer's 进场; PrivilegeType is B站's code for the viewer's 大航海身份 in this room.</summary>
internal sealed record Entry(long Uid, string Nickname, long MessageType, long PrivilegeType, long? FanClubLevel, long WealthLevel,
    long TriggerTime = 0)
{
    /// <summary>The 大航海身份 as its index in Settings.WelcomeGuards: 0 for none, then 舰长, 提督, 总督.</summary>
    public int GuardTier => Guard.TierFromPrivilegeType(PrivilegeType);

    /// <summary>What {大航海} stands for; empty without a 大航海身份.</summary>
    public string GuardName => Guard.Name(GuardTier);
}

internal static class EntryMessages
{
    // Effects come alongside INTERACT_WORD, so only the INTERACT_WORD family names the room's entry format.
    private static readonly Dictionary<string, (string? Version, Func<JsonElement, long, Entry> Decode)> Decoders = new()
    {
        ["INTERACT_WORD_V2"] = ("V2", DecodeV2),
        ["ENTRY_EFFECT"] = (null, DecodeEffect),
        ["ENTRY_EFFECT_MUST_RECEIVE"] = (null, DecodeEffect)
    };

    public static string? Version(string command) => Decoders.TryGetValue(command, out var decoder) ? decoder.Version : null;

    public static Entry? Decode(string command, JsonElement root, long roomId)
    {
        if (!Decoders.TryGetValue(command, out var decoder)) return null;
        var entry = decoder.Decode(root.GetProperty("data"), roomId);
        if (entry.Uid < 0 || entry.MessageType < 0 || entry.PrivilegeType < 0 || entry.FanClubLevel < 0 || entry.WealthLevel < 0)
            throw new FormatException("进场消息的 uid、类型、大航海身份、粉丝团等级或荣耀等级无效。");
        if (entry.MessageType != 1 || entry.Uid == 0) return null;
        return entry with { FanClubLevel = entry.FanClubLevel > 0 ? entry.FanClubLevel : null };
    }

    private static Entry DecodeV2(JsonElement data, long roomId)
    {
        var (entry, fanClubRoom) = InteractWordV2.Decode(data);
        return entry with { FanClubLevel = fanClubRoom == roomId || entry.FanClubLevel < 0 ? entry.FanClubLevel : null };
    }

    // The medal names its anchor rather than the room, so the effect checks it against target_id itself.
    private static Entry DecodeEffect(JsonElement data, long roomId) => EntryEffect.Decode(data);
}
