using Aether.Core;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using Settings = Aether.Core.Settings;

namespace Aether.Gui;

/// <summary>ADR 0016: moves data/gui.yml, which releases before it wrote, into the settings file and the database, then deletes it.
/// Nothing writes gui.yml any more; until both moves succeed it stays, and the next start tries again.</summary>
internal static class GuiFileUpgrade
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    /// <summary>The auto-update switch gui.yml held, which this start goes by; null once there is no gui.yml.</summary>
    public static bool? Run(string dataDirectory, AetherClient client, ILogger logger)
    {
        var path = Path.Combine(dataDirectory, "gui.yml");
        if (!File.Exists(path)) return null;
        string text;
        try { text = File.ReadAllText(path); }
        // Moved since, as when two windows start together: the settings file already has its switch.
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Its switch is unknown, so this start does not check: it may be someone who turned updates off.
            logger.LogWarning(error, "读取 gui.yml 失败，这次不升级、不检查更新，下次启动再试：{Error}", error.Message);
            return false;
        }
        var (autoUpdate, state) = Parse(text, logger);
        // Load first, so a file without the group gets the template's section with its comment before the value is set.
        var moved = Try(() =>
        {
            Settings.Load(dataDirectory, logger);
            Settings.Save(dataDirectory, new Dictionary<string, string> { ["auto_update.enabled"] = Settings.BooleanText(autoUpdate) });
        }, "自动更新开关") & Try(() => client.GuiState.Save(state), MainWindowViewModel.GuiStateText);
        // File.Delete ignores a file already gone, as when two windows start together.
        if (moved) Try(() => File.Delete(path), "删除");
        return autoUpdate;

        bool Try(Action move, string what)
        {
            try
            {
                move();
                return true;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "升级 gui.yml 失败（{What}），下次启动再试：{Error}", what, error.Message);
                return false;
            }
        }
    }

    // Key by key: one hand-broken value falls back to its default alone. Blank, `~` and wrongly shaped values keep the default.
    private static (bool AutoUpdate, GuiState State) Parse(string text, ILogger logger)
    {
        try
        {
            var values = Deserializer.Deserialize<Dictionary<string, object?>?>(text) ?? [];
            return (On("AutoUpdate"), new(On("KeepRecentDanmaku"), values.GetValueOrDefault("RecentRooms") switch
            {
                List<object?> rooms => [.. rooms.OfType<string>()],
                string room => [room],
                _ => [],
            }));

            // Both switches default to on: only a readable false turns one off.
            bool On(string key) => values.GetValueOrDefault(key) is not string text || !bool.TryParse(text, out var on) || on;
        }
        catch (YamlException error)
        {
            logger.LogWarning(error, "gui.yml 不是能读的 YAML，按默认值升级");
            return (true, GuiState.Default);
        }
    }
}
