using System.Text.Json;

namespace Aether.Core.Entries;

internal sealed record Entry(long Uid, string Nickname, long MessageType, long Guard, long? FanClubLevel, long WealthLevel)
{
    // B站's privilege_type: 1 总督, 2 提督, 3 舰长; 0 is none.
    private static readonly string[] GuardNames = ["总督", "提督", "舰长"];

    public static string GuardName(long guard) => guard is >= 1 and <= 3 ? GuardNames[guard - 1] : "";

    /// <summary>The privilege_type a 大航海身份 name stands for; 0 for anything else, such as 关闭.</summary>
    public static long GuardLevel(string name) => Array.IndexOf(GuardNames, name) + 1;
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
        if (entry.Uid < 0 || entry.MessageType < 0 || entry.Guard < 0 || entry.FanClubLevel < 0 || entry.WealthLevel < 0)
            throw new FormatException("进场消息的 uid、类型、大航海身份、粉丝团等级或荣耀等级无效。");
        if (entry.MessageType != 1 || entry.Uid == 0) return null;
        return entry with { FanClubLevel = fanClubRoom == roomId && entry.FanClubLevel > 0 ? entry.FanClubLevel : null };
    }
}
