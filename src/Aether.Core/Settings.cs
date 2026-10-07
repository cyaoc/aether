using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using Tokens = YamlDotNet.Core.Tokens;

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
    private byte[] bytes = [];
    private string source = "";
    private Encoding encoding = Encoding.UTF8;
    private YamlMappingNode? root;

    /// <summary>The same validation used when loading and saving; null means the input is valid.</summary>
    public static string? Validate(string name, string? value) => name switch
    {
        "log.retention_days" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
            && days > 0 ? null : "必须为正整数。",
        "log.level" => Enum.TryParse<LogLevel>(value?.Trim(), out var level)
            && level >= Microsoft.Extensions.Logging.LogLevel.Trace && level <= Microsoft.Extensions.Logging.LogLevel.Critical
            && value?.Trim() == level.ToString() ? null : "必须为 Trace、Debug、Information、Warning、Error、Critical 之一。",
        _ => "未知设置，不能修改。"
    };

    /// <summary>Reload the file and change only the requested values, then atomically replace it.</summary>
    public static void Save(string dataDirectory, IReadOnlyDictionary<string, string> changes)
    {
        var path = Path.Combine(dataDirectory, "aether.yml");
        foreach (var (name, value) in changes)
            if (Validate(name, value) is { } reason) throw new SettingsException(path, 1, name, reason);
        var current = Load(dataDirectory);
        if (current.Error is { } error) throw error;
        var scanner = new Scanner(new StringReader(current.source));
        var tokens = new List<Tokens.Token>();
        while (scanner.MoveNext()) tokens.Add(scanner.Current!);
        var edits = new List<(int Start, int Length, string Value)>();
        var groupEntry = current.root?.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: "log" });
        var group = groupEntry?.Value as YamlMappingNode;
        var missing = new StringBuilder();
        const string lineBreaks = "\r\n\u0085\u2028\u2029"; // YAML also accepts Unicode line breaks.
        var firstBreak = current.source.AsSpan().IndexOfAny(lineBreaks);
        var newline = firstBreak < 0 ? "\n" : current.source.AsSpan(firstBreak).StartsWith("\r\n")
            ? "\r\n" : current.source[firstBreak].ToString();
        var rootIndent = current.root is { Children.Count: > 0 } ? IndentOf(current.root) : 0;
        var indent = new string(' ', group is { Children.Count: > 0 } ? IndentOf(group) : rootIndent + 2);
        foreach (var (name, value) in changes)
        {
            var canEdit = name == "log.retention_days" ? current.LogRetentionDays.CanEdit : current.LogLevel.CanEdit;
            if (!canEdit) throw new SettingsException(path, 1, name, "不能逐行编辑，请在设置文件里修改。");
            var node = group is not null && group.Children.TryGetValue(name[4..], out var valueNode) ? valueNode : null;
            var scalar = name == "log.retention_days"
                ? int.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : value.Trim();
            // Both supported settings validate to plain YAML scalars (an integer or a level name).
            if (node is null) missing.Append(indent).Append(name[4..]).Append(": ").Append(scalar).Append(newline);
            else edits.Add(((int)node.Start.Index, (int)(node.End.Index - node.Start.Index), scalar));
        }
        if (missing.Length > 0)
        {
            var at = (int)(tokens.OfType<Tokens.DocumentEnd>().SingleOrDefault()?.Start.Index ?? current.source.Length);
            if (groupEntry?.Key is { } groupKey)
            {
                var next = current.root!.Children.Keys.FirstOrDefault(key => key.Start.Index > groupKey.Start.Index);
                if (next is not null)
                    at = (int)tokens.OfType<Tokens.Key>().Last(key => key.Start.Index <= next.Start.Index).Start.Index;
            }
            // Insert before the next top-level key or document-end marker, including its indentation.
            if (at < current.source.Length)
                while (at > 0 && !lineBreaks.Contains(current.source[at - 1])) at--;
            var prefix = at > 0 && !lineBreaks.Contains(current.source[at - 1]) ? newline : "";
            if (groupEntry?.Key is null) prefix += new string(' ', rootIndent) + "log:" + newline;
            edits.Add((at, 0, prefix + missing));
        }
        if (edits.Count == 0) return;
        var text = current.source;
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
            text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        var preamble = current.encoding.GetPreamble();
        var temporary = path + "." + Guid.NewGuid() + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                if (current.bytes.AsSpan().StartsWith(preamble)) output.Write(preamble);
                output.Write(current.encoding.GetBytes(text));
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }

        int IndentOf(YamlMappingNode mapping)
        {
            // The block token marks structural indentation even with tags or multiline explicit keys.
            var firstKey = mapping.Children.First().Key.Start.Index;
            var block = tokens.OfType<Tokens.BlockMappingStart>().FirstOrDefault(token =>
                token.Start.Index >= mapping.Start.Index && token.Start.Index <= firstKey)
                ?? throw new SettingsException(path, mapping.Start.Line, "YAML", "不能逐行编辑，请在设置文件里修改。");
            return (int)block.Start.Column - 1;
        }
    }

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
            settings.bytes = File.ReadAllBytes(path);
            using var reader = new StreamReader(new MemoryStream(settings.bytes), new UTF8Encoding(false, true));
            settings.source = reader.ReadToEnd();
            settings.encoding = reader.CurrentEncoding;
            var preamble = settings.encoding.GetPreamble();
            var content = settings.bytes.AsSpan(settings.bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0);
            if (!content.SequenceEqual(settings.encoding.GetBytes(settings.source)))
                throw new SettingsException(path, 1, "文件", "文件编码无效。");
            var yaml = new YamlStream();
            yaml.Load(new StringReader(settings.source));
            if (yaml.Documents.Count == 0) return settings;
            if (yaml.Documents.Count != 1)
                throw new SettingsException(path, yaml.Documents[1].RootNode.Start.Line, "YAML", "只允许一个设置文档。");
            var root = RequireMapping(yaml.Documents[0].RootNode, "YAML");
            settings.root = root;
            var rootEditable = root.Style == MappingStyle.Block && root.Anchor.IsEmpty;
            settings.LogRetentionDays = settings.LogRetentionDays with { CanEdit = rootEditable };
            settings.LogLevel = settings.LogLevel with { CanEdit = rootEditable };
            var unknown = new List<string>();
            foreach (var (groupKey, groupValue) in root.Children)
            {
                var groupName = KeyName(groupKey);
                if (groupName != "log")
                {
                    if (groupValue is YamlMappingNode { Children.Count: > 0 } other)
                        unknown.AddRange(other.Children.Keys.Select(key => groupName + "." + KeyName(key)));
                    else unknown.Add(groupName);
                    continue;
                }
                // Every setting under `log:` commented out leaves a blank value; the settings keep their defaults.
                if (groupValue is YamlScalarNode { Style: ScalarStyle.Plain, Value: "" })
                {
                    var editable = rootEditable && groupValue.Anchor.IsEmpty;
                    settings.LogRetentionDays = settings.LogRetentionDays with { CanEdit = editable };
                    settings.LogLevel = settings.LogLevel with { CanEdit = editable };
                    continue;
                }
                if (groupValue is not YamlMappingNode group)
                {
                    settings.Error ??= new(path, groupValue.Start.Line, "log", "必须是设置分组。");
                    continue;
                }
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
                            if (Validate(name, text) is { } daysError)
                                settings.Error ??= new(path, key.Start.Line, name, daysError);
                            else settings.LogRetentionDays = new(int.Parse(text!, CultureInfo.InvariantCulture), canEdit);
                            break;
                        case "log.level":
                            settings.LogLevel = settings.LogLevel with { CanEdit = canEdit };
                            if (Validate(name, text) is { } levelError)
                                settings.Error ??= new(path, key.Start.Line, name, levelError);
                            else settings.LogLevel = new(Enum.Parse<LogLevel>(text!), canEdit);
                            break;
                        default:
                            unknown.Add(name);
                            break;
                    }
                }
            }
            settings.UnknownKeys = unknown.AsReadOnly();
        }
        catch (SettingsException error) { settings.Error = error; }
        catch (YamlException error) { settings.Error = new(path, error.Start.Line, "YAML", error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException)
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
