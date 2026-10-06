using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Aether.Gui;

public sealed class GuiSettings
{
    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder().IncludeNonPublicProperties().IgnoreUnmatchedProperties().Build();
    private static readonly ISerializer Serializer = new SerializerBuilder().IncludeNonPublicProperties().Build();
    private string path = "";

    [YamlIgnore] public bool KeepRecentDanmaku { get; set; } = true;

    // A blank or `~` value keeps the default; deserializing straight into bool would turn it into false.
    [YamlMember(Alias = nameof(KeepRecentDanmaku))]
    private bool? KeepRecentDanmakuYaml
    {
        get => KeepRecentDanmaku;
        set { if (value is { } keep) KeepRecentDanmaku = keep; }
    }

    public static GuiSettings Load(string dataDirectory, ILogger<GuiSettings> logger)
    {
        var path = Path.Combine(dataDirectory, "gui.yml");
        var settings = new GuiSettings();
        try
        {
            if (File.Exists(path)) settings = Deserializer.Deserialize<GuiSettings?>(File.ReadAllText(path)) ?? settings;
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
        File.WriteAllText(path, Serializer.Serialize(this));
    }
}
