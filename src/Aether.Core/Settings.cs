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
          # 今日查询关键字，非空文字
          daily_keyword: 今日盲盒
          # 本月查询关键字，非空文字；和今日关键字相同时只回复今日
          monthly_keyword: 本月盲盒
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
          # 欢迎语列表，可以写多条，每次随机发一条；列表和每一条都不能为空
          # 可用 {@}（@ 这位观众，只能放开头，不算字数）、{昵称}（太长时自动截短）、
          # {大航海}（舰长/提督/总督，不是大航海身份时为空）；要写花括号本身，用 {{ 和 }}
          # 用 ["欢迎{大航海}{昵称}", "{@}欢迎回家"] 的单行写法；每条加双引号，双引号和反斜杠写成 \" 和 \\
          # 英文、数字、半角符号算 1，其他字符算 2。上限自动读取账号的
          texts: ["欢迎{大航海}{昵称}"]
          # 指定观众的 UID，例如 [12345, 67890]；[] 表示不按 UID 欢迎
          uids: []
          # 本直播间粉丝团等级达到多少就欢迎，0 表示不按粉丝团等级欢迎
          fan_club_level: 0
          # 大航海身份达到哪一档就欢迎：关闭、舰长、提督、总督；提督表示提督和总督
          guard: 舰长
          # 荣耀等级达到多少就欢迎，0 表示不按荣耀等级欢迎
          wealth_level: 0

        # 欢迎动画，下次观看直播间时生效；先开 Aether，再开推流软件
        welcome_animation:
          # true 或 false，映射里有这位观众时，进场在直播画面上播放
          enabled: false

        # 网页服务，视频页 /video、抽奖页 /lottery 都从这里打开；先开 Aether 再开推流软件
        web_service:
          # 端口 1–65535；GUI 保存后立即生效，所有浏览器素材的地址都要跟着改
          port: 17890

        # 欢迎动画的视频，放在 data/videos/ 里，每行一位观众：UID: 文件名
        # UID 必须为正整数；文件名只能是 .mp4 或 .webm，不能带路径；空分组表示没有观众
        welcome_animation_videos:
          # 12345: 小明进场.mp4

        """;

    private const string LineBreaks = "\r\n\u0085\u2028\u2029"; // YAML also accepts Unicode line breaks.

    // Static fields initialise in text order: this list must stay above every Register(...) below.
    private static readonly List<Definition> Definitions = [];
    /// <summary>Every setting this version knows; only tests read it.</summary>
    internal static IReadOnlyList<Definition> AllDefinitions => Definitions;
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
    private static readonly Definition<string> DailyKeyword = Register(Definition.RequiredText("blind_box.daily_keyword", "今日盲盒"));
    private static readonly Definition<string> MonthlyKeyword = Register(Definition.RequiredText("blind_box.monthly_keyword", "本月盲盒"));
    private static readonly Definition<bool> Announce = Register(Definition.Boolean("blind_box.announce", true));
    private static readonly Definition<int> SendInterval = Register(Definition.Number("send.interval_seconds", 1, "必须为正整数。", seconds => seconds > 0));
    private static readonly Definition<bool> WelcomeOn = Register(Definition.Boolean("welcome.enabled", false));
    private static readonly Definition<IReadOnlyList<string>> WelcomeTextList = Register(Definition.TextList("welcome.texts", ["欢迎{大航海}{昵称}"],
        texts => texts.Count == 0 ? "欢迎语列表不能为空。" : texts.Select(Core.WelcomeText.Validate).FirstOrDefault(error => error is not null)));
    private static readonly Definition<IReadOnlyList<long>> WelcomeUidList = Register(Definition.NumberList("welcome.uids", []));
    public const int WelcomeLevelOff = 0;
    private static readonly Definition<int> WelcomeFanClub = Register(Definition.Number("welcome.fan_club_level", WelcomeLevelOff, "必须为非负整数。", level => level >= 0));
    /// <summary>The values welcome.guard accepts: 关闭, then 大航海身份 from low to high, so each one's index is its tier.</summary>
    public static IReadOnlyList<string> WelcomeGuards { get; } = ["关闭", "舰长", "提督", "总督"];
    private static readonly Definition<string> WelcomeGuardTier = Register(Definition.Choice("welcome.guard", "舰长", WelcomeGuards));
    private static readonly Definition<int> WelcomeWealth = Register(Definition.Number("welcome.wealth_level", WelcomeLevelOff, "必须为非负整数。", level => level >= 0));
    private static readonly Definition<bool> AnimationOn = Register(Definition.Boolean("welcome_animation.enabled", false));
    internal static readonly Definition<int> WebPort = Register(Definition.Number("web_service.port", 17890, "必须是 1–65535 的端口。", port => port is >= 1 and <= 65535));
    private static readonly VideoGroupDefinition AnimationVideos = Register(new VideoGroupDefinition("welcome_animation_videos"));
    /// <summary>Adding a setting means registering its definition here, adding its property and a line in Template,
    /// then a Bind call in its GUI settings page (composite settings need their row code instead).
    /// The GUI coverage test catches settings omitted from the pages.</summary>
    private static T Register<T>(T definition) where T : Definition
    {
        Definitions.Add(definition);
        return definition;
    }

    public Setting<int> LogRetentionDays => Get(RetentionDays);
    public Setting<LogLevel> LogLevel => Get(Level);
    public Setting<bool> BlindBoxEnabled => Get(Enabled);
    public Setting<string> BlindBoxDailyKeyword => Get(DailyKeyword);
    public Setting<string> BlindBoxMonthlyKeyword => Get(MonthlyKeyword);
    public Setting<bool> BlindBoxAnnounce => Get(Announce);
    public Setting<int> SendIntervalSeconds => Get(SendInterval);
    public Setting<bool> WelcomeEnabled => Get(WelcomeOn);
    public Setting<IReadOnlyList<string>> WelcomeTexts => Get(WelcomeTextList);
    public Setting<IReadOnlyList<long>> WelcomeUids => Get(WelcomeUidList);
    public Setting<int> WelcomeFanClubLevel => Get(WelcomeFanClub);
    public Setting<string> WelcomeGuard => Get(WelcomeGuardTier);
    public Setting<int> WelcomeWealthLevel => Get(WelcomeWealth);
    public Setting<bool> WelcomeAnimationEnabled => Get(AnimationOn);
    public Setting<int> WebServicePort => Get(WebPort);
    public IReadOnlyDictionary<long, Setting<string>> WelcomeAnimationVideos => AnimationVideos.Items(this);
    public bool CanEditWelcomeAnimationVideos => AnimationVideos.CanEdit(this);
    public IReadOnlyList<string> UnknownKeys { get; private set; } = [];
    public SettingsException? Error { get; private set; }
    private readonly Dictionary<string, object> values = [];
    private string source = "";
    private Encoding encoding = Encoding.UTF8;
    private YamlMappingNode? root;

    public static string FilePath(string dataDirectory) => Path.Combine(dataDirectory, "aether.yml");

    /// <summary>Where shared videos live; the mapping holds bare file names inside it.</summary>
    public static string VideoDirectory(string dataDirectory) => Path.Combine(dataDirectory, "videos");

    internal static string VideoFile(string dataDirectory, string fileName) =>
        Path.Combine(VideoDirectory(dataDirectory), fileName);

    /// <summary>How a switch is written in the file, for forms that hold it as a bool.</summary>
    public static string BooleanText(bool value) => value ? "true" : "false";

    /// <summary>How a number list is written in the file, which is also the text forms take for it.</summary>
    public static string NumberListText(IEnumerable<long> numbers) =>
        Definition.Emit(numbers.Select(number => number.ToString(CultureInfo.InvariantCulture)));

    /// <summary>A single-line text list, quoting every item and escaping quotes and backslashes.</summary>
    public static string TextListText(IEnumerable<string> texts) => Definition.Emit(texts, ScalarStyle.DoubleQuoted);

    /// <summary>The same validation used when loading and saving; null means the input is valid.</summary>
    public static string? Validate(string name, string? value) => Validate(Definitions, name, value);

    private static string? Validate(IReadOnlyList<Definition> definitions, string name, string? value) =>
        Find(definitions, name) is { } definition ? definition.Validate(name, value) : "未知设置，不能修改。";

    private static Definition? Find(IReadOnlyList<Definition> definitions, string name) =>
        definitions.FirstOrDefault(definition => definition.Resolve(name) is not null);

    public static string? ValidateVideoFileName(string name) =>
        name is "." or ".." || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0
            || name.Any(char.IsControl) || !new[] { ".mp4", ".webm" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            ? "必须是纯文件名，扩展名为 .mp4 或 .webm，不能带路径。" : null;

    internal Setting<T> Get<T>(Definition<T> definition) =>
        values.TryGetValue(definition.Name, out var setting) ? (Setting<T>)setting : definition.Initial;

    /// <summary>Reload the file and change only the requested values, then atomically replace it.</summary>
    /// <param name="snapshot">The settings a form was filled from. A value it already held was not changed by the user,
    /// so it is left alone even if the file has since been edited elsewhere.</param>
    public static void Save(string dataDirectory, IReadOnlyDictionary<string, string> changes, Settings? snapshot = null) =>
        Save(dataDirectory, Definitions, changes, snapshot);

    /// <summary>Commits the animation form in one write; mappings added outside the form are left alone.</summary>
    public static void SaveWelcomeAnimations(string dataDirectory, IReadOnlyDictionary<string, string> changes,
        IReadOnlyDictionary<long, string> videos, Settings snapshot)
    {
        var edits = changes.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        foreach (var uid in snapshot.WelcomeAnimationVideos.Keys.Except(videos.Keys))
            edits[AnimationVideos.ItemName(uid)] = null;
        foreach (var (uid, fileName) in videos)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uid);
            edits[AnimationVideos.ItemName(uid)] = fileName;
        }
        SaveChanges(dataDirectory, Definitions, edits, snapshot);
    }

    /// <param name="definitions">The settings this file may hold; only tests pass anything but <see cref="Definitions"/>.</param>
    internal static void Save(string dataDirectory, IReadOnlyList<Definition> definitions,
        IReadOnlyDictionary<string, string> changes, Settings? snapshot = null) =>
        SaveChanges(dataDirectory, definitions, changes.ToDictionary(pair => pair.Key, pair => (string?)pair.Value), snapshot);

    private static void SaveChanges(string dataDirectory, IReadOnlyList<Definition> definitions,
        IReadOnlyDictionary<string, string?> changes, Settings? snapshot = null)
    {
        var path = FilePath(dataDirectory);
        foreach (var (name, value) in changes)
            if (Validate(definitions, name, value) is { } reason) throw new SettingsException(path, 1, name, reason);
        var current = Load(dataDirectory, definitions);
        if (current.Error is { } error) throw error;
        // A value the file or the snapshot already holds is no change, and keeps its spelling: '7', 007 and 7 are the same.
        var resolved = changes.Select(change =>
        {
            var definition = Find(definitions, change.Key)!;
            return (Definition: definition, Name: definition.Resolve(change.Key)!, change.Value);
        }).Where(change => change.Value is null
            ? current.values.ContainsKey(change.Name)
            : !change.Definition.Holds(current, change.Name, change.Value)
                && !(snapshot is not null && change.Definition.Holds(snapshot, change.Name, change.Value))).ToArray();
        var tokens = Scan(current.source);
        var edits = new List<(int Start, int Length, string Value)>();
        var newline = NewlineOf(current.source);
        var rootIndent = current.root is { Children.Count: > 0 } ? IndentOf(current.root) : 0;
        foreach (var groupChanges in resolved.GroupBy(change => change.Definition.Group))
            EditGroup(groupChanges.Key, groupChanges);
        if (edits.Count == 0) return;
        var text = current.source;
        // Later offsets first so earlier ones stay valid; inserts at one offset keep the order they were added in.
        foreach (var (_, edit) in edits.Index().OrderByDescending(edit => edit.Item.Start).ThenByDescending(edit => edit.Index))
            text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        AtomicFile.Write(path, [.. current.encoding.GetPreamble(), .. current.encoding.GetBytes(text)]);

        void EditGroup(string groupName, IEnumerable<(Definition Definition, string Name, string? Value)> groupChanges)
        {
            var groupEntry = current.root?.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: var key } && key == groupName);
            var group = groupEntry?.Value as YamlMappingNode;
            var indent = new string(' ', group is { Children.Count: > 0 } ? IndentOf(group) : rootIndent + 2);
            var missing = new StringBuilder();
            foreach (var (definition, name, value) in groupChanges)
            {
                // Match by name like Load does, so a tagged key such as `!!str level` is still found.
                var entry = group?.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: var key }
                    && definition.Resolve(groupName + "." + key) == name);
                var node = entry?.Value;
                if (!definition.CanEdit(current, name))
                    throw new SettingsException(path, node?.Start.Line ?? groupEntry?.Key?.Start.Line ?? 1, name,
                        "不能逐行编辑，请在设置文件里修改。");
                if (value is null)
                {
                    if (node is null) continue;
                    var start = (int)entry!.Value.Key.Start.Index;
                    while (start > 0 && !LineBreaks.Contains(current.source[start - 1])) start--;
                    var end = (int)EndOf(tokens, node).Index;
                    while (end < current.source.Length && !LineBreaks.Contains(current.source[end])) end++;
                    if (end < current.source.Length) end += current.source.AsSpan(end).StartsWith("\r\n") ? 2 : 1;
                    edits.Add((start, end - start, ""));
                    continue;
                }
                var scalar = definition.Format(name, value);
                if (node is null) missing.Append(indent).Append(name[(groupName.Length + 1)..]).Append(": ").Append(scalar).Append(newline);
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
            foreach (var (groupKey, groupValue) in root.Children) LoadGroup(groupKey, groupValue);
            settings.UnknownKeys = unknown.AsReadOnly();

            void LoadGroup(YamlNode groupKey, YamlNode groupValue)
            {
                var groupName = KeyName(groupKey);
                var known = definitions.Where(definition => definition.Group == groupName).ToArray();
                if (known.Length == 0)
                {
                    if (groupValue is YamlMappingNode { Children.Count: > 0 } other)
                        unknown.AddRange(other.Children.Keys.Select(key => groupName + "." + KeyName(key)));
                    else unknown.Add(groupName);
                    return;
                }
                if (!seenNames.Add(groupName))
                    settings.Error ??= new(path, groupKey.Start.Line, groupName, "分组名重复，请只保留一组。");
                // Every setting under a group commented out leaves a blank value; the settings keep their defaults.
                if (groupValue is YamlScalarNode { Style: ScalarStyle.Plain, Value: "" })
                {
                    MarkEditable(known, rootEditable && groupValue.Anchor.IsEmpty);
                    return;
                }
                if (groupValue is not YamlMappingNode group)
                {
                    settings.Error ??= new(path, groupValue.Start.Line, groupName, "必须是设置分组。");
                    return;
                }
                var groupEditable = rootEditable && group.Style == MappingStyle.Block && group.Anchor.IsEmpty;
                MarkEditable(known, groupEditable);
                foreach (var (key, value) in group.Children) LoadKey(groupName, known, groupEditable, key, value);
            }

            void LoadKey(string groupName, Definition[] known, bool groupEditable, YamlNode key, YamlNode value)
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
                if (known.FirstOrDefault(definition => definition.Contains(name)) is not { } definition) unknown.Add(name);
                else if (definition.ReadKey(settings, name, key, value, canEdit, singleLine, seenNames) is { } reason)
                    settings.Error ??= new(path, key.Start.Line, name, reason);
            }
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
        if (settings.Error is null)
            foreach (var (uid, video) in settings.WelcomeAnimationVideos)
                if (!File.Exists(VideoFile(dataDirectory, video.Value)))
                    logger?.LogWarning("观众 {Uid} 的欢迎动画文件不存在：{FileName}，这位观众不播", uid, video.Value);
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

    /// <summary>A fixed setting (group.key) or a group whose keys come from the file,
    /// including how each value is read and written back on one line.</summary>
    internal abstract class Definition(string name)
    {
        public string Name => name;
        public string Group => name.Split('.')[0];
        public virtual bool Contains(string name) => Name == name;
        public virtual string? Resolve(string name) => Contains(name) ? name : null;
        /// <summary>Null when the text is valid; otherwise why not.</summary>
        public abstract string? Validate(string name, string? text);
        /// <summary>The one-line YAML written for valid text, quoted wherever YAML needs it.</summary>
        public abstract string Format(string name, string text);
        /// <summary>The value <paramref name="settings"/> holds, written as its one-line YAML.</summary>
        public abstract string ValueText(Settings settings, string name);
        public abstract bool CanEdit(Settings settings, string? name = null);
        /// <summary>Whether <paramref name="settings"/> already holds the value of this valid text.</summary>
        public bool Holds(Settings settings, string name, string text) => Format(name, text) == ValueText(settings, name);
        public abstract void MarkEditable(Settings settings, bool canEdit);
        /// <summary>Recognizes a key, checks duplicates and records its value and whether its line can be edited.</summary>
        public abstract string? ReadKey(Settings settings, string name, YamlNode key, YamlNode node,
            bool canEdit, bool singleLine, HashSet<string> seenNames);

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

        /// <param name="check">Why a non-empty line is still invalid; null when it is valid.</param>
        public static Definition<string> RequiredText(string name, string initial, Func<string, string?>? check = null) =>
            ScalarSetting(name, initial, "必须是非空的一行文字。",
                text => OneLine(text) && !string.IsNullOrWhiteSpace(text) ? (true, text) : default, text => text, check);

        /// <summary>One-line texts, written as <c>[a, b]</c>; the input text uses the same form.</summary>
        public static Definition<IReadOnlyList<string>> TextList(string name, IReadOnlyList<string> initial, Func<IReadOnlyList<string>, string?>? check = null)
        {
            const string rule = "必须是 [a, b] 形式的一行文字列表。";
            return new(name, initial, rule, (node, _) => ReadList(node), ParseList, TextListText, check);

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
                        || !Viewer.TryParseUid(text, out var number)) return default;
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

        private static Definition<T> ScalarSetting<T>(string name, T initial, string rule, Func<string, (bool, T)> parse,
            Func<T, string> toText, Func<T, string?>? check = null) =>
            new(name, initial, rule, (node, _) => node is YamlScalarNode { Value: var value } ? parse(value ?? "") : default, parse,
                value => Emit([toText(value)])[1..^1], check);

        private static bool OneLine(string text) => text.AsSpan().IndexOfAny(LineBreaks) < 0;

        /// <summary>A one-line flow list; by default YamlDotNet decides which items need quotes.</summary>
        internal static string Emit(IEnumerable<string> items, ScalarStyle style = ScalarStyle.Any)
        {
            using var writer = new StringWriter();
            var emitter = new Emitter(writer, new EmitterSettings().WithBestWidth(int.MaxValue));
            emitter.Emit(new StreamStart());
            emitter.Emit(new DocumentStart(null, null, true));
            emitter.Emit(new SequenceStart(null, null, true, SequenceStyle.Flow));
            foreach (var item in items) emitter.Emit(new Scalar(null, null, item, style, true, true));
            emitter.Emit(new SequenceEnd());
            emitter.Emit(new DocumentEnd(true));
            emitter.Emit(new StreamEnd());
            return writer.ToString().TrimEnd(LineBreaks.ToCharArray());
        }
    }

    /// <summary>The welcome animation videos: a group whose keys are viewer UIDs read from the file, each mapped to a video file name.
    /// Numeric aliases such as 007 and 7 name the same item, and null deletes one.</summary>
    internal sealed class VideoGroupDefinition(string name) : Definition(name)
    {
        private sealed class GroupValues
        {
            public bool CanEdit { get; set; } = true;
            public Dictionary<long, Setting<string>> Items { get; } = [];
        }

        private static readonly GroupValues Unread = new();

        // Reads never add the group, so a Settings that has not loaded it stays unchanged.
        private GroupValues Read(Settings settings) => settings.values.TryGetValue(Name, out var value) ? (GroupValues)value : Unread;

        private GroupValues Write(Settings settings)
        {
            if (!settings.values.TryGetValue(Name, out var value)) settings.values[Name] = value = new GroupValues();
            return (GroupValues)value;
        }

        public IReadOnlyDictionary<long, Setting<string>> Items(Settings settings) => Read(settings).Items;
        public string ItemName(long uid) => Name + "." + uid.ToString(CultureInfo.InvariantCulture);
        public override bool Contains(string name) => name.StartsWith(Name + ".", StringComparison.Ordinal);
        private bool TryParseUid(string name, out long uid)
        {
            uid = 0;
            return Contains(name) && Viewer.TryParseUid(name[(Name.Length + 1)..], out uid);
        }
        public override string? Resolve(string name) => TryParseUid(name, out var uid) ? ItemName(uid) : null;
        private Definition<string> Item(string name) => RequiredText(Resolve(name)!, "", ValidateVideoFileName);
        public override string? Validate(string name, string? text) => text is null ? null : Item(name).Validate(name, text);
        public override string Format(string name, string text) => Item(name).Format(name, text);
        public override string ValueText(Settings settings, string name) => Item(name).ValueText(settings, name);
        public override bool CanEdit(Settings settings, string? name = null) =>
            Read(settings).CanEdit && (name is null || Item(name).CanEdit(settings));
        public override void MarkEditable(Settings settings, bool canEdit) => Write(settings).CanEdit = canEdit;

        public override string? ReadKey(Settings settings, string name, YamlNode key, YamlNode node,
            bool canEdit, bool singleLine, HashSet<string> seenNames)
        {
            if (!TryParseUid(name, out var uid)) return "UID 必须为 int64 范围内的正整数。";
            var item = Item(name);
            string? reason = null;
            if (!seenNames.Add(item.Name)) reason = "UID 重复，请只保留一项。";
            else if (!singleLine || item.Read(settings, node, canEdit && key.Anchor.IsEmpty, singleLine) is { })
                reason = "必须是单行的纯文件名，扩展名为 .mp4 或 .webm，不能带路径。";
            Write(settings).Items[uid] = settings.Get(item);
            return reason;
        }
    }

    /// <param name="check">Why a value that reads and parses is still invalid, such as a welcome text misusing placeholders.</param>
    internal sealed class Definition<T>(string name, T initial, string rule, Func<YamlNode, bool, (bool Valid, T Value)> read,
        Func<string, (bool Valid, T Value)> parse, Func<T, string> format, Func<T, string?>? check = null) : Definition(name)
    {
        public Setting<T> Initial => new(Name, initial);
        public override string? Validate(string name, string? text) => parse(text ?? "") is (true, var value) ? check?.Invoke(value) : rule;
        /// <summary>The value of valid text.</summary>
        public T Parse(string text) => parse(text).Value;
        public override string Format(string name, string text) => format(Parse(text));
        public override string ValueText(Settings settings, string name) => format(settings.Get(this).Value);
        public override bool CanEdit(Settings settings, string? name = null) => settings.Get(this).CanEdit;
        public override void MarkEditable(Settings settings, bool canEdit) => settings.values[Name] = settings.Get(this) with { CanEdit = canEdit };

        public override string? ReadKey(Settings settings, string name, YamlNode key, YamlNode node,
            bool canEdit, bool singleLine, HashSet<string> seenNames) =>
            !seenNames.Add(Name) ? "设置名重复，请只保留一项。" : Read(settings, node, canEdit, singleLine);

        public string? Read(Settings settings, YamlNode node, bool canEdit, bool singleLine)
        {
            var (valid, value) = read(node, singleLine);
            // A value only check rejects is still kept, so the GUI shows what the file holds.
            settings.values[Name] = new Setting<T>(Name, valid ? value : settings.Get(this).Value, canEdit);
            return valid ? check?.Invoke(value) : rule;
        }
    }
}
