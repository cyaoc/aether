using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using Tokens = YamlDotNet.Core.Tokens;

namespace Aether.Core;

public sealed partial class Settings
{
    /// <summary>Reload the file and change only the requested values, then atomically replace it.</summary>
    /// <param name="snapshot">The settings a form was filled from. A value it already held was not changed by the user,
    /// so it is left alone even if the file has since been edited elsewhere.</param>
    public static void Save(string dataDirectory, IReadOnlyDictionary<string, string> changes, Settings? snapshot = null) =>
        Save(dataDirectory, Definitions, changes, snapshot);

    /// <summary>Commits the animation form in one write; viewers added outside the form are left alone.</summary>
    public static void SaveAnimationViewers(string dataDirectory, IReadOnlyDictionary<string, string> changes,
        IReadOnlyDictionary<long, string> choices, Settings snapshot)
    {
        var edits = changes.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        foreach (var uid in snapshot.EntryAnimationViewers.Keys.Except(choices.Keys)) edits[AnimationViewers.ItemName(uid)] = null;
        foreach (var (uid, choice) in choices)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uid);
            edits[AnimationViewers.ItemName(uid)] = choice;
        }
        SaveChanges(dataDirectory, Definitions, edits, snapshot);
    }

    /// <summary>Commits the 桌宠 form in one write. <paramref name="lines"/> are the current character's kinds of lines the streamer
    /// edited; a kind edited back to hers is deleted, so the file keeps only kinds that differ.</summary>
    public static void SaveDesktopPet(string dataDirectory, IReadOnlyDictionary<string, string> changes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> lines, Settings snapshot)
    {
        var edits = changes.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        foreach (var (kind, list) in lines)
            edits[PetLines.ItemName((Character.CurrentNumber, kind))] = list.SequenceEqual(Character.Current.Lines[kind]) ? null : TextListText(list);
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
        var document = new YamlStream();
        document.Load(new StringReader(text));
        if (ValidateAcross((YamlMappingNode)document.Documents[0].RootNode, path) is { } acrossError) throw acrossError;
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

    internal static List<Tokens.Token> Scan(string source)
    {
        var scanner = new Scanner(new StringReader(source), skipComments: false);
        var tokens = new List<Tokens.Token>();
        while (scanner.MoveNext()) tokens.Add(scanner.Current!);
        return tokens;
    }

    /// <summary>Where a node's text ends. YamlDotNet ends a collection node at its opening bracket,
    /// so a flow list or map ends just past the bracket that closes it.</summary>
    internal static Mark EndOf(List<Tokens.Token> tokens, YamlNode node)
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

    /// <summary>Reads the settings file, first upgrading an older format in place (<see cref="SettingsUpgrade"/>),
    /// then appending the template's section for each group a valid file lacks entirely,
    /// so file editors see settings added by an upgrade. Failing to append leaves the file and the defaults as they are.</summary>
    public static Settings Load(string dataDirectory, ILogger? logger = null)
    {
        var settings = Read(dataDirectory, Definitions, logger, upgrade: true);
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
        // A blank line before the appended groups, as between the template's groups, unless the file already ends with one.
        if (settings.source.Length > 0)
        {
            var breaks = settings.source[settings.source.TrimEnd().Length..].Replace("\r\n", "\n").Count(LineBreaks.Contains);
            appended = string.Concat(Enumerable.Repeat(newline, Math.Max(0, 2 - breaks))) + appended;
        }
        try
        {
            AtomicFile.Write(FilePath(dataDirectory), [.. settings.encoding.GetPreamble(), .. settings.encoding.GetBytes(settings.source + appended)]);
            // Refresh node offsets as well as values, so Save can edit the appended defaults in place.
            var refreshed = Load(dataDirectory, Definitions);
            refreshed.UpgradedFromVersion = settings.UpgradedFromVersion;
            return refreshed;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The defaults already apply, so a read-only file or directory must not stop watching or file logging.
            logger?.LogWarning("设置文件缺少的分组未能补写，按默认值运行：{Error}", error.Message);
            return settings;
        }
    }

    /// <summary>How the numbers of one group follow one another, each positive: thresholds each above the one before (rise 1),
    /// the 日常小动作's least and most seconds, which may be equal (rise 0).</summary>
    public sealed record NumberOrder(int Rise)
    {
        public static readonly NumberOrder Rising = new(1), NotFalling = new(0);

        /// <summary>Whether <paramref name="value"/> may follow <paramref name="before"/>, or come first when that is null.</summary>
        public bool Follows(int? before, int value) => value >= (before is { } previous ? (long)previous + Rise : 1);

        /// <summary>The index of the first number out of order, or null when all are in order.</summary>
        public int? Invalid(IReadOnlyList<int> values)
        {
            for (var index = 0; index < values.Count; index++)
                if (!Follows(index == 0 ? null : values[index - 1], values[index])) return index;
            return null;
        }
    }

    public const string GiftThresholdRule = "四档投入门槛必须为正整数且逐档递增。";

    // A property: static fields of another part of this class may not have been initialised before this one.
    /// <summary>Numbers of one group that must follow one another in order, and why.</summary>
    private static (Definition<int>[] Definitions, NumberOrder Order, string Rule)[] Ordered =>
        [(GiftThresholds, NumberOrder.Rising, GiftThresholdRule), ([PetMidGift, PetBigGift], NumberOrder.Rising, "中礼物门槛要小于大礼物门槛。"),
            ([PetDailyMin, PetDailyMax], NumberOrder.NotFalling, "日常小动作的最短间隔不能比最长间隔长。")];

    internal const string PranksterKeywordIsBlindBox = "和盲盒查询的关键字一样，观众查盲盒时就站到它那边了；换一个";
    internal const string BlindBoxKeywordIsPrankster = "和桌宠宝箱小游戏里帮它的关键字一样，观众查盲盒时就站到它那边了；换一个";

    /// <summary>Whether a danmaku saying <paramref name="said"/> is <paramref name="keyword"/>: exactly it, the spaces around either
    /// aside, as a 报名抽奖 and the 帮它的关键字 take one. Two keywords that are the same take the same danmaku.</summary>
    internal static bool SameKeyword(string said, string keyword) => said.Trim() == keyword.Trim();

    /// <summary>Why the 帮它的关键字 cannot be <paramref name="keyword"/> as typed: empty, the next 报名抽奖's
    /// <paramref name="entryKeyword"/>, or a 盲盒查询 keyword this file holds; null when it can.</summary>
    public string? ValidatePranksterKeyword(string keyword, string entryKeyword) =>
        string.IsNullOrWhiteSpace(keyword) ? "关键字不能空着"
        : SameKeyword(keyword, entryKeyword) ? "和抽奖报名的关键字一样，观众分不清是在报名还是帮它；换一个"
        : BlindBoxKeywords.Any(query => SameKeyword(Get(query).Value, keyword)) ? PranksterKeywordIsBlindBox : null;

    /// <summary>Why a 盲盒查询 keyword cannot be <paramref name="keyword"/> as typed, beside the 帮它的关键字 this file holds; null
    /// when it can.</summary>
    public string? ValidateBlindBoxKeyword(string keyword) => SameKeyword(keyword, DesktopPetPranksterKeyword.Value) ? BlindBoxKeywordIsPrankster : null;

    // A property, as Ordered is.
    private static Definition<string>[] BlindBoxKeywords => [DailyKeyword, MonthlyKeyword, YearlyKeyword];

    /// <summary>What must hold between settings, which no one of them can check: numbers in order, and keywords apart.</summary>
    private static SettingsException? ValidateAcross(YamlMappingNode root, string path) =>
        Ordered.Select(numbers => ValidateOrder(root, path, numbers)).FirstOrDefault(error => error is not null) ?? ValidateKeywords(root, path);

    /// <summary>A 盲盒查询 keyword that is the 帮它的关键字 too, which a viewer asking for a 盲盒查询 would join the 捣蛋鬼 with. It is
    /// reported on the line of the 帮它的关键字, or of the 盲盒查询 keyword when the file leaves the other out.</summary>
    private static SettingsException? ValidateKeywords(YamlMappingNode root, string path)
    {
        var mine = NodeOf(root, PetPranksterKeyword);
        foreach (var query in BlindBoxKeywords)
        {
            var theirs = NodeOf(root, query);
            if (!SameKeyword(TextOf(theirs, query), TextOf(mine, PetPranksterKeyword))) continue;
            var (node, name, rule) = mine is null ? (theirs, query.Name, BlindBoxKeywordIsPrankster) : (mine, PetPranksterKeyword.Name, PranksterKeywordIsBlindBox);
            return new(path, node?.Start.Line ?? 1, name, rule);
        }
        return null;

        static string TextOf(YamlNode? node, Definition<string> definition) => node is YamlScalarNode { Value: { } text } ? text : definition.Initial.Value;
    }

    /// <summary>The node <paramref name="definition"/> is read from, if <paramref name="root"/> has it.</summary>
    private static YamlNode? NodeOf(YamlMappingNode root, Definition definition) =>
        ChildOf(ChildOf(root, definition.Group), definition.Name.Split('.')[1]);

    /// <summary>The value of <paramref name="node"/>'s first key named <paramref name="key"/>, matched by name as Read matches it, so a
    /// tagged key such as `!!str blind_box` is found too; null when <paramref name="node"/> is no mapping or has no such key.</summary>
    internal static YamlNode? ChildOf(YamlNode? node, string key) =>
        (node as YamlMappingNode)?.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: var name } && name == key).Value;

    private static SettingsException? ValidateOrder(YamlMappingNode root, string path, (Definition<int>[] Definitions, NumberOrder Order, string Rule) numbers)
    {
        var definitions = numbers.Definitions;
        var fields = definitions.Select(definition => NodeOf(root, definition)).ToArray();
        var values = fields.Select((field, index) => field is YamlScalarNode { Value: { } text } && int.TryParse(text, out var value)
            ? value : definitions[index].Initial.Value).ToArray();
        // The defaults are in order, so one the file leaves out is out of order only after one it holds, in the group told of.
        return numbers.Order.Invalid(values) is { } invalid
            ? new(path, (fields[invalid] ?? ChildOf(root, definitions[0].Group))?.Start.Line ?? 1, definitions[invalid].Name, numbers.Rule) : null;
    }

    internal static string NewlineOf(string source)
    {
        var firstBreak = source.AsSpan().IndexOfAny(LineBreaks);
        return firstBreak < 0 ? "\n" : source.AsSpan(firstBreak).StartsWith("\r\n") ? "\r\n" : source[firstBreak].ToString();
    }

    /// <param name="definitions">The settings this file may hold; only tests pass anything but <see cref="Definitions"/>.</param>
    internal static Settings Load(string dataDirectory, IReadOnlyList<Definition> definitions, ILogger? logger = null) =>
        Read(dataDirectory, definitions, logger, upgrade: false);

    private static Settings Read(string dataDirectory, IReadOnlyList<Definition> definitions, ILogger? logger, bool upgrade)
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
            if (yaml.Documents.Count == 0) return settings;
            if (yaml.Documents.Count != 1)
                throw new SettingsException(path, yaml.Documents[1].RootNode.Start.Line, "YAML", "只允许一个设置文档。");
            var root = RequireMapping(yaml.Documents[0].RootNode, "YAML");
            var versionEntry = root.Children.Where(pair => pair.Key is YamlScalarNode { Value: "format_version" }).ToArray();
            var version = 0;
            if (versionEntry.Length > 0)
            {
                var value = versionEntry[0].Value;
                if (versionEntry.Length != 1 || value is not YamlScalarNode { Value: { } number }
                    || !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out version))
                    throw new SettingsException(path, value.Start.Line, "format_version", "必须为非负整数，请修正设置文件版本。");
                if (version > SettingsUpgrade.CurrentVersion)
                    throw new SettingsException(path, value.Start.Line, "format_version", "文件版本高于当前程序，请使用更新版本的 Aether。");
            }
            var original = settings.source;
            if (upgrade && version < SettingsUpgrade.CurrentVersion)
            {
                settings.source = SettingsUpgrade.Apply(original, version, path);
                if (settings.source != original)
                {
                    if (!lossless)
                        throw new SettingsException(path, 1, "升级", "文件编码无法无损改写，请先将设置文件转换为 UTF-8，再重试。");
                    yaml.Load(new StringReader(settings.source));
                    root = RequireMapping(yaml.Documents[0].RootNode, "YAML");
                }
            }
            var tokens = Scan(settings.source);
            settings.root = root;
            var rootEditable = lossless && root.Style == MappingStyle.Block && root.Anchor.IsEmpty;
            MarkEditable(definitions, rootEditable);
            var unknown = new List<string>();
            // Load and Save match names without YAML tags, so known names must be unique on that basis.
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (groupKey, groupValue) in root.Children) LoadGroup(groupKey, groupValue);
            settings.UnknownKeys = unknown.AsReadOnly();
            settings.Error ??= ValidateAcross(root, path);
            // Written even when a value is invalid: the upgrade never causes one, and its line must match the file on disk.
            if (settings.source != original)
            {
                try { AtomicFile.Write(path, [.. settings.encoding.GetPreamble(), .. settings.encoding.GetBytes(settings.source)]); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    throw new SettingsException(path, 1, "升级", $"无法写入升级后的设置：{error.Message}。请检查文件和目录的写入权限后重试。");
                }
                settings.UpgradedFromVersion = version;
                logger?.LogInformation("设置文件已从格式版本 {FromVersion} 升级到 {ToVersion}", version, SettingsUpgrade.CurrentVersion);
            }

            void LoadGroup(YamlNode groupKey, YamlNode groupValue)
            {
                var groupName = KeyName(groupKey);
                if (groupName == "format_version") return;
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
}
