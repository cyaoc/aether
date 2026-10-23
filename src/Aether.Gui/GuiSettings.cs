using System.Text;
using Aether.Core;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Aether.Gui;

public sealed class GuiSettings
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();
    private string path = "";

    private const int RecentRoomLimit = 5;

    public bool KeepRecentDanmaku { get; set; } = true;
    public long? LastLotteryRoomId { get; set; }

    // Most recent first. Every write goes through here, so a hand-edited gui.yml and a new connection follow one rule.
    public string[] RecentRooms
    {
        get;
        set => field = [.. value.Where(room => Room.TryParseNumber(room, out _)).Distinct().Take(RecentRoomLimit)];
    } = [];

    public static GuiSettings Load(string dataDirectory, ILogger<GuiSettings> logger)
    {
        var path = Path.Combine(dataDirectory, "gui.yml");
        var settings = new GuiSettings();
        try
        {
            // Read key by key: one hand-broken value falls back alone instead of resetting the others,
            // which the next save would then overwrite. Blank, `~` and wrongly shaped values keep the default.
            var values = File.Exists(path)
                ? Deserializer.Deserialize<Dictionary<string, object?>?>(File.ReadAllText(path)) ?? []
                : [];
            if (values.GetValueOrDefault(nameof(KeepRecentDanmaku)) is string keep && bool.TryParse(keep, out var value))
                settings.KeepRecentDanmaku = value;
            if (values.GetValueOrDefault(nameof(LastLotteryRoomId)) is string lastRoom && Room.TryParseNumber(lastRoom, out var roomId))
                settings.LastLotteryRoomId = roomId;
            settings.RecentRooms = values.GetValueOrDefault(nameof(RecentRooms)) switch
            {
                List<object?> rooms => [.. rooms.OfType<string>()],
                string room => [room],
                _ => []
            };
        }
        catch (Exception error) when (error is YamlException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "读取 GUI 设置失败，使用默认值");
        }
        settings.path = path;
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, Encoding.UTF8.GetBytes(Serializer.Serialize(this)));
    }
}
