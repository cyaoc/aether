using System.Globalization;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Aether.Core;

public sealed record Setting<T>(T Value, bool CanEdit = true);

public sealed class SettingsException(string filePath, long lineNumber, string settingName, string reason)
    : Exception($"{filePath} 第 {lineNumber} 行（{settingName}）：{reason}")
{
    public string FilePath { get; } = filePath;
    public long LineNumber { get; } = lineNumber;
    public string SettingName { get; } = settingName;
}

/// <summary>A settings snapshot. Values must not be used when Error is present; CanEdit describes safe line editing.</summary>
public sealed class Settings
{
    private const string Template = """
        # 日志设置，修改后重启程序生效
        log:
          # 日志保留天数，必须为正整数
          retention_days: 30
          # 日志级别：Trace、Debug、Information、Warning、Error、Critical
          level: Information

        """;

    public Setting<int> LogRetentionDays { get; private set; } = new(30);
    public Setting<LogLevel> LogLevel { get; private set; } = new(Microsoft.Extensions.Logging.LogLevel.Information);
    public IReadOnlyList<string> UnknownKeys { get; private set; } = [];
    public SettingsException? Error { get; private set; }

    public static Settings Load(string dataDirectory, ILogger? logger = null)
    {
        var path = Path.Combine(dataDirectory, "aether.yml");
        var settings = new Settings();
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(dataDirectory);
                var temporary = path + "." + Guid.NewGuid() + ".tmp";
                try
                {
                    File.WriteAllText(temporary, Template);
                    try { File.Move(temporary, path); }
                    catch (IOException) when (File.Exists(path)) { } // Another process created it first.
                }
                finally { File.Delete(temporary); }
            }
            var yaml = new YamlStream();
            yaml.Load(new StringReader(File.ReadAllText(path)));
            if (yaml.Documents.Count == 0) return settings;
            if (yaml.Documents.Count != 1)
                throw new SettingsException(path, yaml.Documents[1].RootNode.Start.Line, "YAML", "只允许一个设置文档。");
            var root = RequireMapping(yaml.Documents[0].RootNode, "YAML");
            var rootEditable = root.Style == MappingStyle.Block && root.Anchor.IsEmpty;
            settings.LogRetentionDays = settings.LogRetentionDays with { CanEdit = rootEditable };
            settings.LogLevel = settings.LogLevel with { CanEdit = rootEditable };
            var unknown = new List<string>();
            foreach (var (groupKey, groupValue) in root.Children)
            {
                var groupName = KeyName(groupKey);
                if (groupValue is YamlMappingNode { Children.Count: > 0 } group)
                    foreach (var key in group.Children.Keys)
                    {
                        var name = groupName + "." + KeyName(key);
                        if (name is not ("log.retention_days" or "log.level")) unknown.Add(name);
                    }
                else if (groupName != "log") unknown.Add(groupName);
            }
            settings.UnknownKeys = unknown.AsReadOnly();
            if (root.Children.TryGetValue(new YamlScalarNode("log"), out var log))
            {
                var group = RequireMapping(log, "log");
                var groupEditable = rootEditable && group.Style == MappingStyle.Block && group.Anchor.IsEmpty;
                settings.LogRetentionDays = settings.LogRetentionDays with { CanEdit = groupEditable };
                settings.LogLevel = settings.LogLevel with { CanEdit = groupEditable };
                foreach (var (key, value) in group.Children)
                {
                    var name = "log." + KeyName(key);
                    var text = (value as YamlScalarNode)?.Value?.Trim();
                    var canEdit = groupEditable && value is YamlScalarNode scalar
                        && scalar.Style is not (ScalarStyle.Literal or ScalarStyle.Folded)
                        && scalar.Anchor.IsEmpty && key.Start.Line == key.End.Line
                        && key.Start.Line == value.Start.Line && value.Start.Line == value.End.Line;
                    switch (name)
                    {
                        case "log.retention_days":
                            settings.LogRetentionDays = settings.LogRetentionDays with { CanEdit = canEdit };
                            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days <= 0)
                                settings.Error ??= new(path, key.Start.Line, name, "必须为正整数。");
                            else settings.LogRetentionDays = new(days, canEdit);
                            break;
                        case "log.level":
                            settings.LogLevel = settings.LogLevel with { CanEdit = canEdit };
                            if (!Enum.TryParse<LogLevel>(text, out var level) || level < Microsoft.Extensions.Logging.LogLevel.Trace
                                || level > Microsoft.Extensions.Logging.LogLevel.Critical || text != level.ToString())
                                settings.Error ??= new(path, key.Start.Line, name,
                                    "必须为 Trace、Debug、Information、Warning、Error、Critical 之一。");
                            else settings.LogLevel = new(level, canEdit);
                            break;
                    }
                }
            }
        }
        catch (SettingsException error) { settings.Error = error; }
        catch (YamlException error) { settings.Error = new(path, error.Start.Line, "YAML", error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            settings.Error = new(path, 1, "文件", error.Message);
        }
        if (settings.UnknownKeys.Count > 0)
            logger?.LogWarning("未知设置键：{Keys}", string.Join("、", settings.UnknownKeys));
        return settings;

        YamlMappingNode RequireMapping(YamlNode node, string name) => node as YamlMappingNode
            ?? throw new SettingsException(path, node.Start.Line, name, "必须是设置分组。");

        string KeyName(YamlNode key) => key is YamlScalarNode { Value: { } name } ? name
            : throw new SettingsException(path, key.Start.Line, "YAML", "设置键必须是文字。");
    }
}
