using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using Tokens = YamlDotNet.Core.Tokens;

namespace Aether.Core;

public sealed record Setting<T>(string Name, T Value, bool CanEdit = true);

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

    private static readonly Definition<int> RetentionDays = new("log.retention_days", 30, "必须为正整数。",
        text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days > 0 ? days : null,
        days => days.ToString(CultureInfo.InvariantCulture),
        settings => settings.LogRetentionDays, (settings, setting) => settings.LogRetentionDays = setting);
    private static readonly Definition<LogLevel> Level = new("log.level", Microsoft.Extensions.Logging.LogLevel.Information,
        "必须为 Trace、Debug、Information、Warning、Error、Critical 之一。",
        text => Enum.TryParse<LogLevel>(text, out var level) && level is >= Microsoft.Extensions.Logging.LogLevel.Trace
            and <= Microsoft.Extensions.Logging.LogLevel.Critical && text == level.ToString() ? level : null,
        level => level.ToString(),
        settings => settings.LogLevel, (settings, setting) => settings.LogLevel = setting);
    /// <summary>Every setting this version knows; adding one is a definition here plus its property.</summary>
    private static readonly Definition[] Definitions = [RetentionDays, Level];

    public Setting<int> LogRetentionDays { get; private set; } = RetentionDays.Initial;
    public Setting<LogLevel> LogLevel { get; private set; } = Level.Initial;
    public IReadOnlyList<string> UnknownKeys { get; private set; } = [];
    public SettingsException? Error { get; private set; }
    private string source = "";
    private Encoding encoding = Encoding.UTF8;
    private YamlMappingNode? root;

    public static string FilePath(string dataDirectory) => Path.Combine(dataDirectory, "aether.yml");

    /// <summary>The same validation used when loading and saving; null means the input is valid.</summary>
    public static string? Validate(string name, string? value) =>
        Find(name) is { } definition ? definition.Validate(value) : "未知设置，不能修改。";

    private static Definition? Find(string name) => Definitions.FirstOrDefault(definition => definition.Name == name);

    /// <summary>Reload the file and change only the requested values, then atomically replace it.</summary>
    public static void Save(string dataDirectory, IReadOnlyDictionary<string, string> changes)
    {
        var path = FilePath(dataDirectory);
        foreach (var (name, value) in changes)
            if (Validate(name, value) is { } reason) throw new SettingsException(path, 1, name, reason);
        var current = Load(dataDirectory);
        if (current.Error is { } error) throw error;
        var scanner = new Scanner(new StringReader(current.source));
        var tokens = new List<Tokens.Token>();
        while (scanner.MoveNext()) tokens.Add(scanner.Current!);
        var edits = new List<(int Start, int Length, string Value)>();
        const string lineBreaks = "\r\n\u0085\u2028\u2029"; // YAML also accepts Unicode line breaks.
        var firstBreak = current.source.AsSpan().IndexOfAny(lineBreaks);
        var newline = firstBreak < 0 ? "\n" : current.source.AsSpan(firstBreak).StartsWith("\r\n")
            ? "\r\n" : current.source[firstBreak].ToString();
        var rootIndent = current.root is { Children.Count: > 0 } ? IndentOf(current.root) : 0;
        foreach (var groupChanges in changes.Select(change => (Definition: Find(change.Key)!, change.Value))
            .GroupBy(change => change.Definition.Group))
            EditGroup(groupChanges.Key, groupChanges);
        if (edits.Count == 0) return;
        var text = current.source;
        // Later offsets first so earlier ones stay valid; inserts at one offset keep the order they were added in.
        foreach (var (_, edit) in edits.Index().OrderByDescending(edit => edit.Item.Start).ThenByDescending(edit => edit.Index))
            text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        AtomicFile.Write(path, [.. current.encoding.GetPreamble(), .. current.encoding.GetBytes(text)]);

        void EditGroup(string groupName, IEnumerable<(Definition Definition, string Value)> groupChanges)
        {
            var groupEntry = current.root?.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: var key } && key == groupName);
            var group = groupEntry?.Value as YamlMappingNode;
            var indent = new string(' ', group is { Children.Count: > 0 } ? IndentOf(group) : rootIndent + 2);
            var missing = new StringBuilder();
            foreach (var (definition, value) in groupChanges)
            {
                // Match by name like Load does, so a tagged key such as `!!str level` is still found.
                var node = group?.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: var key } && key == definition.Key).Value;
                if (!definition.CanEdit(current))
                    throw new SettingsException(path, node?.Start.Line ?? groupEntry?.Key?.Start.Line ?? 1, definition.Name,
                        "不能逐行编辑，请在设置文件里修改。");
                var scalar = definition.Format(value);
                if (node is null) missing.Append(indent).Append(definition.Key).Append(": ").Append(scalar).Append(newline);
                else edits.Add(((int)node.Start.Index, (int)(node.End.Index - node.Start.Index), scalar));
            }
            if (missing.Length == 0) return;
            var at = (int)(tokens.OfType<Tokens.DocumentEnd>().FirstOrDefault()?.Start.Index ?? current.source.Length);
            if (group?.Children.LastOrDefault() is { Key: { } lastKey, Value: var lastValue } && lastKey.Start.Line == lastValue.End.Line)
            {
                // Right after the group's last one-line setting, so blank lines and comments before the next group stay with it.
                var end = (int)lastValue.End.Index;
                var lineBreak = current.source.AsSpan(end).IndexOfAny(lineBreaks);
                at = lineBreak < 0 ? current.source.Length
                    : end + lineBreak + (current.source.AsSpan(end + lineBreak).StartsWith("\r\n") ? 2 : 1);
            }
            else if (groupEntry?.Key is { } groupKey
                && current.root!.Children.Keys.FirstOrDefault(key => key.Start.Index > groupKey.Start.Index) is { } next)
            {
                // Insert before the next top-level key, including its indentation.
                at = (int)tokens.OfType<Tokens.Key>().Last(key => key.Start.Index <= next.Start.Index).Start.Index;
                while (at > 0 && !lineBreaks.Contains(current.source[at - 1])) at--;
            }
            var prefix = at > 0 && !lineBreaks.Contains(current.source[at - 1]) ? newline : "";
            if (groupEntry?.Key is null) prefix += new string(' ', rootIndent) + groupName + ":" + newline;
            edits.Add((at, 0, prefix + missing));
        }

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
        var path = FilePath(dataDirectory);
        var settings = new Settings();
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(dataDirectory);
                try { AtomicFile.Write(path, Encoding.UTF8.GetBytes(Template), overwrite: false); }
                catch (IOException) when (File.Exists(path)) { } // Another process created it first.
            }
            var bytes = File.ReadAllBytes(path);
            using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false));
            settings.source = reader.ReadToEnd();
            settings.encoding = reader.CurrentEncoding; // Has a preamble only when the file starts with that BOM.
            // A file that doesn't round-trip (e.g. GBK) still loads; only rewriting it would change bytes.
            var lossless = bytes.AsSpan(settings.encoding.GetPreamble().Length)
                .SequenceEqual(settings.encoding.GetBytes(settings.source));
            MarkEditable(Definitions, lossless);
            var yaml = new YamlStream();
            yaml.Load(new StringReader(settings.source));
            if (yaml.Documents.Count == 0) return settings;
            if (yaml.Documents.Count != 1)
                throw new SettingsException(path, yaml.Documents[1].RootNode.Start.Line, "YAML", "只允许一个设置文档。");
            var root = RequireMapping(yaml.Documents[0].RootNode, "YAML");
            settings.root = root;
            var rootEditable = lossless && root.Style == MappingStyle.Block && root.Anchor.IsEmpty;
            MarkEditable(Definitions, rootEditable);
            var unknown = new List<string>();
            // Load and Save match names without YAML tags, so known names must be unique on that basis.
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (groupKey, groupValue) in root.Children)
            {
                var groupName = KeyName(groupKey);
                var known = Definitions.Where(definition => definition.Group == groupName).ToArray();
                if (known.Length == 0)
                {
                    if (groupValue is YamlMappingNode { Children.Count: > 0 } other)
                        unknown.AddRange(other.Children.Keys.Select(key => groupName + "." + KeyName(key)));
                    else unknown.Add(groupName);
                    continue;
                }
                if (!seenNames.Add(groupName))
                    settings.Error ??= new(path, groupKey.Start.Line, groupName, "分组名重复，请只保留一组。");
                // Every setting under a group commented out leaves a blank value; the settings keep their defaults.
                if (groupValue is YamlScalarNode { Style: ScalarStyle.Plain, Value: "" })
                {
                    MarkEditable(known, rootEditable && groupValue.Anchor.IsEmpty);
                    continue;
                }
                if (groupValue is not YamlMappingNode group)
                {
                    settings.Error ??= new(path, groupValue.Start.Line, groupName, "必须是设置分组。");
                    continue;
                }
                var groupEditable = rootEditable && group.Style == MappingStyle.Block && group.Anchor.IsEmpty;
                MarkEditable(known, groupEditable);
                foreach (var (key, value) in group.Children)
                {
                    var name = groupName + "." + KeyName(key);
                    var text = (value as YamlScalarNode)?.Value?.Trim();
                    var canEdit = groupEditable && value is YamlScalarNode scalar
                        && scalar.Style is not (ScalarStyle.Literal or ScalarStyle.Folded)
                        && scalar.Anchor.IsEmpty && key.Start.Line == key.End.Line
                        && key.Start.Line == value.Start.Line && value.Start.Line == value.End.Line;
                    if (known.FirstOrDefault(definition => definition.Name == name) is not { } definition) unknown.Add(name);
                    else if (!seenNames.Add(name))
                        settings.Error ??= new(path, key.Start.Line, name, "设置名重复，请只保留一项。");
                    else if (definition.Read(settings, text, canEdit) is { } reason)
                        settings.Error ??= new(path, key.Start.Line, name, reason);
                }
            }
            settings.UnknownKeys = unknown.AsReadOnly();
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

        void MarkEditable(IEnumerable<Definition> definitions, bool editable)
        {
            foreach (var definition in definitions) definition.MarkEditable(settings, editable);
        }
    }

    /// <summary>A setting this version knows: its name in the file (group.key) and how its text becomes a value.</summary>
    private abstract class Definition(string name)
    {
        public string Name => name;
        public string Group => name[..name.IndexOf('.')];
        public string Key => name[(name.IndexOf('.') + 1)..];
        /// <summary>Null when the text is valid; otherwise why not.</summary>
        public abstract string? Validate(string? text);
        /// <summary>The scalar written for valid text; every definition yields a plain YAML scalar, so none needs quotes.</summary>
        public abstract string Format(string text);
        public abstract bool CanEdit(Settings settings);
        public abstract void MarkEditable(Settings settings, bool canEdit);
        /// <summary>Records whether the line can be edited, and the value when the text is valid; returns why it is not.</summary>
        public abstract string? Read(Settings settings, string? text, bool canEdit);
    }

    private sealed class Definition<T>(string name, T initial, string rule, Func<string, T?> parse, Func<T, string> format,
        Func<Settings, Setting<T>> get, Action<Settings, Setting<T>> set) : Definition(name) where T : struct
    {
        public Setting<T> Initial => new(Name, initial);
        public override string? Validate(string? text) => Parse(text) is null ? rule : null;
        public override string Format(string text) => format(Parse(text)!.Value);
        public override bool CanEdit(Settings settings) => get(settings).CanEdit;
        public override void MarkEditable(Settings settings, bool canEdit) => set(settings, get(settings) with { CanEdit = canEdit });

        public override string? Read(Settings settings, string? text, bool canEdit)
        {
            var value = Parse(text);
            set(settings, get(settings) with { Value = value ?? get(settings).Value, CanEdit = canEdit });
            return value is null ? rule : null;
        }

        private T? Parse(string? text) => parse(text?.Trim() ?? "");
    }
}
