using System.Text.Json;

namespace Aether.Core.Entries;

/// <summary>A viewer's 进场; PrivilegeType is B站's code for the viewer's 大航海身份 in this room.</summary>
internal sealed record Entry(long Uid, string Nickname, long MessageType, long PrivilegeType, long? FanClubLevel, long WealthLevel)
{
    /// <summary>The 大航海身份 as its index in Settings.WelcomeGuards: 0 for none, then 舰长, 提督, 总督.</summary>
    // B站's privilege_type: 1 总督, 2 提督, 3 舰长; 0 is none.
    public int GuardTier => PrivilegeType is >= 1 and <= 3 ? 4 - (int)PrivilegeType : 0;

    /// <summary>What {大航海} stands for; empty without a 大航海身份.</summary>
    public string GuardName => GuardTier > 0 ? Settings.WelcomeGuards[GuardTier] : "";
}

internal static class EntryMessages
{
    private static readonly Dictionary<string, (string Version, Func<JsonElement, (Entry Entry, long FanClubRoom)> Decode)> Decoders = new()
    {
        ["INTERACT_WORD_V2"] = ("V2", InteractWordV2.Decode)
    };

    public static string? Version(string command) => Decoders.TryGetValue(command, out var decoder) ? decoder.Version : null;

    public static Entry? Decode(string command, JsonElement root, long roomId)
    {
        if (!Decoders.TryGetValue(command, out var decoder)) return null;
        var (entry, fanClubRoom) = decoder.Decode(root.GetProperty("data"));
        if (entry.Uid < 0 || entry.MessageType < 0 || entry.PrivilegeType < 0 || entry.FanClubLevel < 0 || entry.WealthLevel < 0)
            throw new FormatException("进场消息的 uid、类型、大航海身份、粉丝团等级或荣耀等级无效。");
        if (entry.MessageType != 1 || entry.Uid == 0) return null;
        return entry with { FanClubLevel = fanClubRoom == roomId && entry.FanClubLevel > 0 ? entry.FanClubLevel : null };
    }
}
