using System.Text.Json;

namespace Aether.Core.Entries;

internal sealed record Entry(long Uid, string Nickname, long MessageType, long Guard, long? FanClubLevel, long WealthLevel);

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
            throw new FormatException("进场消息的 uid、类型、身份或等级无效。");
        if (entry.MessageType != 1 || entry.Uid == 0) return null;
        return entry with { FanClubLevel = fanClubRoom == roomId && entry.FanClubLevel > 0 ? entry.FanClubLevel : null };
    }
}
