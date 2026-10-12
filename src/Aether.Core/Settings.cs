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

        # 盲盒统计，下次观看直播间时生效
        blind_box:
          # true 或 false，false 时既不记录，也不回复和播报
          enabled: true
          # 查询关键字，非空文字
          keyword: 今日盲盒
          # true 或 false，送完一轮盲盒后主动 @ 播报这一轮的投入和盈亏
          announce: true

        # 发送队列，下次观看直播间时生效
        send:
          # 两次发送之间的秒数，必须为正整数
          interval_seconds: 1

        # 进场欢迎，下次观看直播间时生效
        welcome:
          # true 或 false，满足下面任意一个欢迎条件的观众进场时，发一条欢迎弹幕
          enabled: false
          # 欢迎语，非空文字。可用 {@}（@ 这位观众，只能放开头，不算字数）、{昵称}（太长时自动截短）、
          # {大航海}（舰长/提督/总督，不是大航海身份时为空）；要写花括号本身，用 {{ 和 }}
          # 以 { 开头时要加引号，例如："{@}欢迎回家"、"欢迎{大航海}{昵称}"、"欢迎{昵称}光临"
          text: "欢迎{大航海}{昵称}"
          # 弹幕字数上限：英文、数字、半角符号算 1，其他字符算 2，必须为正整数
          # B站 默认 20（约 10 个汉字）；大航海、荣耀等级高的账号更多。填多了，超出的欢迎会被 B站 拒绝
          max_length: 20
          # 指定观众的 UID，例如 [12345, 67890]；[] 表示不按 UID 欢迎
          uids: []
          # 本直播间粉丝团等级达到多少就欢迎，0 表示不按粉丝团等级欢迎
          fan_club_level: 0
          # 大航海身份达到哪一档就欢迎：关闭、舰长、提督、总督；提督表示提督和总督
          guard: 舰长
          # 荣耀等级达到多少就欢迎，0 表示不按荣耀等级欢迎
          wealth_level: 0

        """;

    private const string LineBreaks = "\r\n\u0085\u2028\u2029"; // YAML also accepts Unicode line breaks.

    // Static fields initialise in text order: this list must stay above every Register(...) below.
    private static readonly List<Definition> Definitions = [];
    private static readonly Definition<int> RetentionDays = Register(Definition.Number("log.retention_days", 30, "必须为正整数。", days => days > 0));
    /// <summary>The values log.level accepts, in order.</summary>
    public static IReadOnlyList<LogLevel> LogLevels { get; } =
    [
        Microsoft.Extensions.Logging.LogLevel.Trace, Microsoft.Extensions.Logging.LogLevel.Debug,
        Microsoft.Extensions.Logging.LogLevel.Information, Microsoft.Extensions.Logging.LogLevel.Warning,
        Microsoft.Extensions.Logging.LogLevel.Error, Microsoft.Extensions.Logging.LogLevel.Critical,
    ];
    private static readonly Definition<LogLevel> Level =
        Register(Definition.Choice("log.level", Microsoft.Extensions.Logging.LogLevel.Information, LogLevels));
    private static readonly Definition<bool> Enabled = Register(Definition.Boolean("blind_box.enabled", true));
    private static readonly Definition<string> Keyword = Register(Definition.RequiredText("blind_box.keyword", "今日盲盒"));
    private static readonly Definition<bool> Announce = Register(Definition.Boolean("blind_box.announce", true));
    private static readonly Definition<int> SendInterval = Register(Definition.Number("send.interval_seconds", 1, "必须为正整数。", seconds => seconds > 0));
    private static readonly Definition<bool> WelcomeOn = Register(Definition.Boolean("welcome.enabled", false));
    private static readonly Definition<string> WelcomeTemplate = Register(Definition.RequiredText("welcome.text", "欢迎{大航海}{昵称}"));
    private static readonly Definition<int> WelcomeLimit = Register(Definition.Number("welcome.max_length", 20, "必须为正整数。", length => length > 0));
    private static readonly Definition<IReadOnlyList<long>> WelcomeUidList = Register(Definition.NumberList("welcome.uids", []));
    private static readonly Definition<int> WelcomeFanClub = Register(Definition.Number("welcome.fan_club_level", 0, "必须为非负整数。", level => level >= 0));
    /// <summary>The values welcome.guard accepts, in order.</summary>
    public static IReadOnlyList<string> WelcomeGuards { get; } = ["关闭", "舰长", "提督", "总督"];
    private static readonly Definition<string> WelcomeGuardTier = Register(Definition.Choice("welcome.guard", "舰长", WelcomeGuards));
    private static readonly Definition<int> WelcomeWealth = Register(Definition.Number("welcome.wealth_level", 0, "必须为非负整数。", level => level >= 0));
    /// <summary>Adding a setting is a registered definition here, its property and its line in Template
    /// (plus, for the GUI, its controls in SettingsWindow.axaml, one Show call and one Fields entry).</summary>
    private static Definition<T> Register<T>(Definition<T> definition)
    {
        Definitions.Add(definition);
        return definition;
    }

    public Setting<int> LogRetentionDays => Get(RetentionDays);
    public Setting<LogLevel> LogLevel => Get(Level);
    public Setting<bool> BlindBoxEnabled => Get(Enabled);
    public Setting<string> BlindBoxKeyword => Get(Keyword);
    public Setting<bool> BlindBoxAnnounce => Get(Announce);
    public Setting<int> SendIntervalSeconds => Get(SendInterval);
    public Setting<bool> WelcomeEnabled => Get(WelcomeOn);
    public Setting<string> WelcomeText => Get(WelcomeTemplate);
    public Setting<int> WelcomeMaxLength => Get(WelcomeLimit);
    public Setting<IReadOnlyList<long>> WelcomeUids => Get(WelcomeUidList);
    public Setting<int> WelcomeFanClubLevel => Get(WelcomeFanClub);
    public Setting<string> WelcomeGuard => Get(WelcomeGuardTier);
    public Setting<int> WelcomeWealthLevel => Get(WelcomeWealth);
    public IReadOnlyList<string> UnknownKeys { get; private set; } = [];
    public SettingsException? Error { get; private set; }
    private readonly Dictionary<Definition, object> values = [];
    private string source = "";
    private Encoding encoding = Encoding.UTF8;
    private YamlMappingNode? root;

    public static string FilePath(string dataDirectory) => Path.Combine(dataDirectory, "aether.yml");

    /// <summary>How a switch is written in the file, for forms that hold it as a bool.</summary>
    public static string BooleanText(bool value) => value ? "true" : "false";

    /// <summary>How a number list is written in the file, which is also the text forms take for it.</summary>
    public static string NumberListText(IEnumerable<long> numbers) =>
        Definition.Emit(numbers.Select(number => number.ToString(CultureInfo.InvariantCulture)));

    /// <summary>The same validation used when loading and saving; null means the input is valid.</summary>
    public static string? Validate(string name, string? value) => Validate(Definitions, name, value);

    /// <summary>Why the welcome text would misuse placeholders or exceed its length limit once <paramref name="changes"/>
    /// apply; null when it would not. Every change must already be valid; settings left out keep this snapshot's values.</summary>
    public string? ValidateWelcomeText(IReadOnlyDictionary<string, string> changes)
    {
        return Core.WelcomeText.Validate(Changed(WelcomeTemplate), Changed(WelcomeLimit));

        T Changed<T>(Definition<T> definition) =>
            changes.TryGetValue(definition.Name, out var input) ? definition.Parse(input) : Get(definition).Value;
    }

    private static string? Validate(IReadOnlyList<Definition> definitions, string name, string? value) =>
        Find(definitions, name) is { } definition ? definition.Validate(value) : "未知设置，不能修改。";

    private static Definition? Find(IReadOnlyList<Definition> definitions, string name) =>
        definitions.FirstOrDefault(definition => definition.Name == name);

    internal Setting<T> Get<T>(Definition<T> definition) =>
        values.TryGetValue(definition, out var setting) ? (Setting<T>)setting : definition.Initial;

    /// <summary>Reload the file and change only the requested values, then atomically replace it.</summary>
    /// <param name="snapshot">The settings a form was filled from. A value it already held was not changed by the user,
    /// so it is left alone even if the file has since been edited elsewhere.</param>
    public static void Save(string dataDirectory, IReadOnlyDictionary<string, string> changes, Settings? snapshot = null) =>
        Save(dataDirectory, Definitions, changes, snapshot);

    /// <param name="definitions">The settings this file may hold; only tests pass anything but <see cref="Definitions"/>.</param>
    internal static void Save(string dataDirectory, IReadOnlyList<Definition> definitions,
        IReadOnlyDictionary<string, string> changes, Settings? snapshot = null)
    {
        var path = FilePath(dataDirectory);
        foreach (var (name, value) in changes)
            if (Validate(definitions, name, value) is { } reason) throw new SettingsException(path, 1, name, reason);
        var current = Load(dataDirectory, definitions);
        if (current.Error is { } error) throw error;
        // A value the file or the snapshot already holds is no change, and keeps its spelling: '7', 007 and 7 are the same.
        changes = changes.Where(change =>
        {
            var definition = Find(definitions, change.Key)!;
            return !definition.Holds(current, change.Value) && !(snapshot is not null && definition.Holds(snapshot, change.Value));
        }).ToDictionary();
        if (definitions.Contains(WelcomeTemplate) && current.WelcomeError(path, changes) is { } invalid) throw invalid;
        var tokens = Scan(current.source);
        var edits = new List<(int Start, int Length, string Value)>();
        var newline = NewlineOf(current.source);
        var rootIndent = current.root is { Children.Count: > 0 } ? IndentOf(current.root) : 0;
        foreach (var groupChanges in changes.Select(change => (Definition: Find(definitions, change.Key)!, change.Value))
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
                else edits.Add(((int)node.Start.Index, (int)(EndOf(tokens, node).Index - node.Start.Index), scalar));
            }
            if (missing.Length == 0) return;
            var at = (int)(tokens.OfType<Tokens.DocumentEnd>().FirstOrDefault()?.Start.Index ?? current.source.Length);
            if (group?.Children.LastOrDefault() is { Key: { } lastKey, Value: var lastValue }
                && lastKey.Start.Line == EndOf(tokens, lastValue).Line)
            {
                // Right after the group's last one-line setting, so blank lines and comments before the next group stay with it.
                var end = (int)EndOf(tokens, lastValue).Index;
                var lineBreak = current.source.AsSpan(end).IndexOfAny(LineBreaks);
                at = lineBreak < 0 ? current.source.Length
                    : end + lineBreak + (current.source.AsSpan(end + lineBreak).StartsWith("\r\n") ? 2 : 1);
            }
            else if (groupEntry?.Key is { } groupKey
                && current.root!.Children.Keys.FirstOrDefault(key => key.Start.Index > groupKey.Start.Index) is { } next)
            {
                // Insert before the next top-level key, including its indentation.
                at = (int)tokens.OfType<Tokens.Key>().Last(key => key.Start.Index <= next.Start.Index).Start.Index;
                while (at > 0 && !LineBreaks.Contains(current.source[at - 1])) at--;
            }
            var prefix = at > 0 && !LineBreaks.Contains(current.source[at - 1]) ? newline : "";
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

    /// <summary>YamlDotNet rejects some malformed input with InvalidOperationException instead of YamlException;
    /// either way the text is not YAML we can read, and never a reason to crash.</summary>
    private static bool IsYamlFailure(Exception error) => error is YamlException
        || error is InvalidOperationException && error.TargetSite?.DeclaringType?.Namespace?.StartsWith("YamlDotNet") == true;

    private static List<Tokens.Token> Scan(string source)
    {
        var scanner = new Scanner(new StringReader(source));
        var tokens = new List<Tokens.Token>();
        while (scanner.MoveNext()) tokens.Add(scanner.Current!);
        return tokens;
    }

    /// <summary>Where a node's text ends. YamlDotNet ends a collection node at its opening bracket,
    /// so a flow list or map ends just past the bracket that closes it.</summary>
    private static Mark EndOf(List<Tokens.Token> tokens, YamlNode node)
    {
        if (node is not (YamlSequenceNode { Style: SequenceStyle.Flow } or YamlMappingNode { Style: MappingStyle.Flow })) return node.End;
        var depth = 0;
        foreach (var token in tokens.SkipWhile(token => token.Start.Index < node.Start.Index))
        {
            if (token is Tokens.FlowSequenceStart or Tokens.FlowMappingStart) depth++;
            else if (token is Tokens.FlowSequenceEnd or Tokens.FlowMappingEnd && --depth == 0)
                return new Mark(token.Start.Index + 1, token.Start.Line, token.Start.Column + 1);
        }
        return node.End;
    }

    /// <summary>Reads the settings file, appending the template's section for each group a valid file lacks entirely,
    /// so file editors see settings added by an upgrade. Failing to append leaves the file and the defaults as they are.</summary>
    public static Settings Load(string dataDirectory, ILogger? logger = null)
    {
        var settings = Load(dataDirectory, Definitions, logger);
        if (settings.Error is not null) return settings;
        var missing = Definitions.Where(definition => definition.CanEdit(settings)
            && settings.root?.Children.Keys.Any(key => key is YamlScalarNode { Value: var name } && name == definition.Group) != true)
            .Select(definition => definition.Group).ToHashSet();
        if (missing.Count == 0) return settings;
        var tokens = Scan(settings.source);
        // Appending after an explicit document end would create a second document.
        if (tokens.OfType<Tokens.DocumentEnd>().Any()) return settings;
        var sections = Template.ReplaceLineEndings("\n").Split("\n\n")
            .Where(section => missing.Any(group => section.Contains("\n" + group + ":\n")));
        var indent = new string(' ', (int)(tokens.OfType<Tokens.BlockMappingStart>().FirstOrDefault()?.Start.Column ?? 1) - 1);
        var newline = NewlineOf(settings.source);
        var appended = string.Join("\n\n", sections).TrimEnd('\n');
        appended = string.Join(newline, appended.Split('\n').Select(line => indent + line)) + newline;
        // A blank line before the appended groups, as between the template's groups.
        if (settings.source.Length > 0)
            appended = (LineBreaks.Contains(settings.source[^1]) ? newline : newline + newline) + appended;
        try
        {
            AtomicFile.Write(FilePath(dataDirectory), [.. settings.encoding.GetPreamble(), .. settings.encoding.GetBytes(settings.source + appended)]);
            // Refresh node offsets as well as values, so Save can edit the appended defaults in place.
            return Load(dataDirectory, Definitions);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The defaults already apply, so a read-only file or directory must not stop watching or file logging.
            logger?.LogWarning("设置文件缺少的分组未能补写，按默认值运行：{Error}", error.Message);
            return settings;
        }
    }

    private static string NewlineOf(string source)
    {
        var firstBreak = source.AsSpan().IndexOfAny(LineBreaks);
        return firstBreak < 0 ? "\n" : source.AsSpan(firstBreak).StartsWith("\r\n") ? "\r\n" : source[firstBreak].ToString();
    }

    /// <param name="definitions">The settings this file may hold; only tests pass anything but <see cref="Definitions"/>.</param>
    internal static Settings Load(string dataDirectory, IReadOnlyList<Definition> definitions, ILogger? logger = null)
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
            MarkEditable(definitions, lossless);
            var yaml = new YamlStream();
            yaml.Load(new StringReader(settings.source));
            var tokens = Scan(settings.source);
            if (yaml.Documents.Count == 0) return settings;
            if (yaml.Documents.Count != 1)
                throw new SettingsException(path, yaml.Documents[1].RootNode.Start.Line, "YAML", "只允许一个设置文档。");
            var root = RequireMapping(yaml.Documents[0].RootNode, "YAML");
            settings.root = root;
            var rootEditable = lossless && root.Style == MappingStyle.Block && root.Anchor.IsEmpty;
            MarkEditable(definitions, rootEditable);
            var unknown = new List<string>();
            // Load and Save match names without YAML tags, so known names must be unique on that basis.
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (groupKey, groupValue) in root.Children)
            {
                var groupName = KeyName(groupKey);
                var known = definitions.Where(definition => definition.Group == groupName).ToArray();
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
                    // One line holding a scalar or a flow list of scalars, as ADR 0004 allows.
                    var singleLine = key.Start.Line == key.End.Line && key.Start.Line == value.Start.Line
                        && value.Start.Line == EndOf(tokens, value).Line;
                    var canEdit = groupEditable && value.Anchor.IsEmpty && singleLine && value switch
                        {
                            YamlScalarNode scalar => scalar.Style is not (ScalarStyle.Literal or ScalarStyle.Folded),
                            YamlSequenceNode { Style: SequenceStyle.Flow } list =>
                                list.Children.All(item => item is YamlScalarNode { Anchor.IsEmpty: true }),
                            _ => false,
                        };
                    if (known.FirstOrDefault(definition => definition.Name == name) is not { } definition) unknown.Add(name);
                    else if (!seenNames.Add(name))
                        settings.Error ??= new(path, key.Start.Line, name, "设置名重复，请只保留一项。");
                    else if (definition.Read(settings, value, canEdit, singleLine) is { } reason)
                        settings.Error ??= new(path, key.Start.Line, name, reason);
                }
            }
            settings.UnknownKeys = unknown.AsReadOnly();
            if (definitions.Contains(WelcomeTemplate))
                settings.Error ??= settings.WelcomeError(path, new Dictionary<string, string>());
        }
        catch (SettingsException error) { settings.Error = error; }
        catch (Exception error) when (IsYamlFailure(error))
        {
            settings.Error = new(path, (error as YamlException)?.Start.Line ?? 1, "YAML", error.Message);
        }
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

    /// <summary>Checks the welcome text once these changes apply, reported at the text's line, else the welcome group's.</summary>
    private SettingsException? WelcomeError(string path, IReadOnlyDictionary<string, string> changes)
    {
        if (ValidateWelcomeText(changes) is not { } reason) return null;
        var group = root?.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: "welcome" }) ?? default;
        var key = (group.Value as YamlMappingNode)?.Children.Keys.FirstOrDefault(key => key is YamlScalarNode { Value: "text" });
        return new(path, key?.Start.Line ?? group.Key?.Start.Line ?? 1, "welcome.text", reason);
    }

    /// <summary>A setting this version knows: its name in the file (group.key), how its text becomes a value,
    /// and how a value is written back on one line.</summary>
    internal abstract class Definition(string name)
    {
        public string Name => name;
        public string Group => name[..name.IndexOf('.')];
        public string Key => name[(name.IndexOf('.') + 1)..];
        /// <summary>Null when the text is valid; otherwise why not.</summary>
        public abstract string? Validate(string? text);
        /// <summary>The one-line YAML written for valid text, quoted wherever YAML needs it.</summary>
        public abstract string Format(string text);
        public abstract bool CanEdit(Settings settings);
        /// <summary>Whether <paramref name="settings"/> already holds the value of this valid text.</summary>
        public abstract bool Holds(Settings settings, string text);
        public abstract void MarkEditable(Settings settings, bool canEdit);
        /// <summary>Records whether the line can be edited, and the value when the node is valid; returns why it is not.</summary>
        public abstract string? Read(Settings settings, YamlNode node, bool canEdit, bool singleLine);

        public static Definition<int> Number(string name, int initial, string rule, Func<int, bool> valid) => ScalarSetting(name, initial, rule,
            text => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && valid(number)
                ? (true, number) : default,
            number => number.ToString(CultureInfo.InvariantCulture));

        public static Definition<bool> Boolean(string name, bool initial) => ScalarSetting(name, initial, "必须为 true 或 false。",
            text => text.Trim() switch { "true" => (true, true), "false" => (true, false), _ => default },
            BooleanText);

        /// <summary>One of <paramref name="choices"/>, written exactly as its name.</summary>
        public static Definition<T> Choice<T>(string name, T initial, IReadOnlyList<T> choices) where T : notnull =>
            ScalarSetting(name, initial, $"必须为 {string.Join("、", choices)} 之一。",
                text => choices.Where(choice => choice.ToString() == text.Trim()).Select(choice => (true, choice)).FirstOrDefault(),
                choice => choice.ToString()!);

        /// <summary>One line of text, kept exactly as typed.</summary>
        public static Definition<string> Text(string name, string initial) => ScalarSetting(name, initial, "必须是一行文字。",
            text => OneLine(text) ? (true, text) : default, text => text);

        public static Definition<string> RequiredText(string name, string initial) => ScalarSetting(name, initial, "必须是非空的一行文字。",
            text => OneLine(text) && !string.IsNullOrWhiteSpace(text) ? (true, text) : default, text => text);

        /// <summary>One-line texts, written as <c>[a, b]</c>; the input text uses the same form.</summary>
        public static Definition<IReadOnlyList<string>> TextList(string name, IReadOnlyList<string> initial)
        {
            const string rule = "必须是 [a, b] 形式的一行文字列表。";
            return new(name, initial, rule, (node, _) => ReadList(node), ParseList, Emit);

            static (bool, IReadOnlyList<string>) ReadList(YamlNode node) =>
                node is YamlSequenceNode list && list.Children.All(item => item is YamlScalarNode { Value: { } text } && OneLine(text))
                    ? (true, list.Children.Select(item => ((YamlScalarNode)item).Value!).ToArray()) : default;

            static (bool, IReadOnlyList<string>) ParseList(string text) => ParseNode(text) is { } node ? ReadList(node) : default;
        }

        /// <summary>Positive int64 values, written and entered as a single-line <c>[12345, 67890]</c> list.</summary>
        public static Definition<IReadOnlyList<long>> NumberList(string name, IReadOnlyList<long> initial)
        {
            const string rule = "必须是 [12345, 67890] 形式的单行数字列表，每项为 int64 范围内的正整数；[] 表示空列表。";
            return new(name, initial, rule, ReadList, ParseList, NumberListText);

            static (bool, IReadOnlyList<long>) ReadList(YamlNode node, bool singleLine)
            {
                if (!singleLine || node is not YamlSequenceNode { Style: SequenceStyle.Flow } list) return default;
                var numbers = new List<long>();
                foreach (var item in list.Children)
                {
                    if (item is not YamlScalarNode { Value: { } text }
                        || !long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                        || number <= 0) return default;
                    numbers.Add(number);
                }
                return (true, numbers.AsReadOnly());
            }

            static (bool, IReadOnlyList<long>) ParseList(string text) =>
                OneLine(text) && ParseNode(text) is { } node ? ReadList(node, true) : default;
        }

        /// <summary>The root node of text holding exactly one YAML document; null otherwise.</summary>
        private static YamlNode? ParseNode(string text)
        {
            var yaml = new YamlStream();
            try { yaml.Load(new StringReader(text)); }
            catch (Exception error) when (IsYamlFailure(error)) { return null; }
            return yaml.Documents.Count == 1 ? yaml.Documents[0].RootNode : null;
        }

        private static Definition<T> ScalarSetting<T>(string name, T initial, string rule, Func<string, (bool, T)> parse, Func<T, string> toText) =>
            new(name, initial, rule, (node, _) => node is YamlScalarNode { Value: var value } ? parse(value ?? "") : default, parse,
                value => Emit([toText(value)])[1..^1]);

        private static bool OneLine(string text) => text.AsSpan().IndexOfAny(LineBreaks) < 0;

        /// <summary>A one-line flow list, letting YamlDotNet decide which items need quotes.</summary>
        internal static string Emit(IEnumerable<string> items)
        {
            using var writer = new StringWriter();
            var emitter = new Emitter(writer, new EmitterSettings().WithBestWidth(int.MaxValue));
            emitter.Emit(new StreamStart());
            emitter.Emit(new DocumentStart(null, null, true));
            emitter.Emit(new SequenceStart(null, null, true, SequenceStyle.Flow));
            foreach (var item in items) emitter.Emit(new Scalar(null, null, item, ScalarStyle.Any, true, true));
            emitter.Emit(new SequenceEnd());
            emitter.Emit(new DocumentEnd(true));
            emitter.Emit(new StreamEnd());
            return writer.ToString().TrimEnd(LineBreaks.ToCharArray());
        }
    }

    internal sealed class Definition<T>(string name, T initial, string rule,
        Func<YamlNode, bool, (bool Valid, T Value)> read, Func<string, (bool Valid, T Value)> parse, Func<T, string> format) : Definition(name)
    {
        public Setting<T> Initial => new(Name, initial);
        public override string? Validate(string? text) => parse(text ?? "").Valid ? null : rule;
        /// <summary>The value of valid text.</summary>
        public T Parse(string text) => parse(text).Value;
        public override string Format(string text) => format(Parse(text));
        public override bool CanEdit(Settings settings) => settings.Get(this).CanEdit;
        public override bool Holds(Settings settings, string text) => Format(text) == format(settings.Get(this).Value);
        public override void MarkEditable(Settings settings, bool canEdit) => settings.values[this] = settings.Get(this) with { CanEdit = canEdit };

        public override string? Read(Settings settings, YamlNode node, bool canEdit, bool singleLine)
        {
            var (valid, value) = read(node, singleLine);
            settings.values[this] = new Setting<T>(Name, valid ? value : settings.Get(this).Value, canEdit);
            return valid ? null : rule;
        }
    }
}
