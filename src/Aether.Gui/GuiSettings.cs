using Aether.Core;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Aether.Gui;

public sealed class GuiSettings
{
    private static string FilePath => Path.Combine(AetherClient.LocateDataDirectory(), "gui.yml");

    public bool KeepRecentDanmaku { get; set; } = true;

    public static GuiSettings Load(ILogger<GuiSettings> logger)
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return new DeserializerBuilder().Build().Deserialize<GuiSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception error) when (error is YamlException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "读取 GUI 设置失败，使用默认值");
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, new SerializerBuilder().Build().Serialize(this));
    }
}
