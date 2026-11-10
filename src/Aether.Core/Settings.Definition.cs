using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Aether.Core;

public sealed partial class Settings
{
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

        /// <summary>A percentage from 0 to 100: a volume, where 0 is silent, or a chance, where 0 is never.</summary>
        public static Definition<int> Percent(string name, int initial) =>
            Number(name, initial, "必须是 0–100。", value => value is >= 0 and <= 100);

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

    /// <summary>Settings whose keys come from the file, each <c>name.key</c> on a line of its own: the animation viewers by UID, the
    /// 桌宠's changed lines by character and kind. Aliases of a key, such as 007 and 7, name the same item, and null deletes one.</summary>
    internal abstract class KeyedDefinition<TKey, TValue>(string name, string keyRule, string duplicateRule) : Definition(name)
        where TKey : notnull
    {
        private sealed class GroupValues
        {
            public bool CanEdit { get; set; } = true;
            public Dictionary<TKey, Setting<TValue>> Items { get; } = [];
        }

        private static readonly GroupValues Unread = new();

        // Reads never add the group, so a Settings that has not loaded it stays unchanged.
        private GroupValues Read(Settings settings) => settings.values.TryGetValue(Name, out var value) ? (GroupValues)value : Unread;

        private GroupValues Write(Settings settings)
        {
            if (!settings.values.TryGetValue(Name, out var value)) settings.values[Name] = value = new GroupValues();
            return (GroupValues)value;
        }

        /// <summary>The key <paramref name="text"/> spells, if it spells one.</summary>
        protected abstract (bool Valid, TKey Key) ParseKey(string text);
        protected abstract string KeyText(TKey key);
        /// <summary>How the item under <paramref name="key"/>, named <paramref name="itemName"/>, is read, checked and written.</summary>
        protected abstract Definition<TValue> Item(TKey key, string itemName);

        public IReadOnlyDictionary<TKey, Setting<TValue>> Items(Settings settings) => Read(settings).Items;
        public string ItemName(TKey key) => Name + "." + KeyText(key);
        public override bool Contains(string name) => name.StartsWith(Name + ".", StringComparison.Ordinal);
        private (bool Valid, TKey Key) ParseName(string name) => Contains(name) ? ParseKey(name[(Name.Length + 1)..]) : default;
        public override string? Resolve(string name) => ParseName(name) is (true, var key) ? ItemName(key) : null;
        private Definition<TValue> Item(string name)
        {
            var key = ParseName(name).Key;
            return Item(key, ItemName(key));
        }
        public override string? Validate(string name, string? text) => text is null ? null : Item(name).Validate(name, text);
        public override string Format(string name, string text) => Item(name).Format(name, text);
        public override string ValueText(Settings settings, string name) => settings.values.ContainsKey(Resolve(name)!) ? Item(name).ValueText(settings, name) : "";
        public override bool CanEdit(Settings settings, string? name = null) =>
            Read(settings).CanEdit && (name is null || Item(name).CanEdit(settings));
        public override void MarkEditable(Settings settings, bool canEdit) => Write(settings).CanEdit = canEdit;

        public override string? ReadKey(Settings settings, string name, YamlNode key, YamlNode node,
            bool canEdit, bool singleLine, HashSet<string> seenNames)
        {
            if (ParseName(name) is not (true, var parsed)) return keyRule;
            var item = Item(parsed, ItemName(parsed));
            var reason = seenNames.Add(item.Name) ? item.Read(settings, node, canEdit && key.Anchor.IsEmpty, singleLine) : duplicateRule;
            Write(settings).Items[parsed] = settings.Get(item);
            return reason;
        }
    }

    /// <summary>The animation viewers: a group whose keys are viewer UIDs, each on one line mapped to its tier, 0 or a video file name.</summary>
    internal sealed class ViewerGroupDefinition(string name)
        : KeyedDefinition<long, AnimationChoice>(name, "UID 必须为 int64 范围内的正整数。", "UID 重复，请只保留一项。")
    {
        private const string ChoiceRule = "必须是 0、30、40、50、60 或纯 .mp4、.webm 文件名，不能带路径。";

        protected override (bool Valid, long Key) ParseKey(string text) => Viewer.TryParseUid(text, out var uid) ? (true, uid) : default;
        protected override string KeyText(long key) => key.ToString(CultureInfo.InvariantCulture);
        protected override Definition<AnimationChoice> Item(long key, string itemName) => new(itemName, AnimationChoice.Off, ChoiceRule,
            (node, singleLine) => singleLine && node is YamlScalarNode { Value: { } text } && AnimationChoice.Parse(text) is { } choice ? (true, choice) : default,
            text => AnimationChoice.Parse(text) is { } choice ? (true, choice) : default,
            choice => Emit([choice.ToString()])[1..^1]);
    }

    /// <summary>The 桌宠's lines the streamer changed: <c>lines.character.kind</c> keys in the desktop_pet group, a character's
    /// number and a kind of the 演出表, each a one-line list like the welcome texts holding only that kind's placeholders.</summary>
    internal sealed class LineDefinition(string name) : KeyedDefinition<(string Character, string Kind), IReadOnlyList<string>>(name,
        "必须是 lines.角色编号.台词类，例如 lines.1.bigGift；台词类见演出表。", "这一类台词重复，请只保留一行。")
    {
        protected override (bool Valid, (string Character, string Kind) Key) ParseKey(string text) =>
            text.Split('.') is [var number, var kind] && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var character)
                && character > 0 && ShowTable.Current.Lines.Any(line => line.Id == kind)
                ? (true, (character.ToString(CultureInfo.InvariantCulture), kind)) : default;

        protected override string KeyText((string Character, string Kind) key) => key.Character + "." + key.Kind;

        protected override Definition<IReadOnlyList<string>> Item((string Character, string Kind) key, string itemName) =>
            TextList(itemName, [], ShowTable.Current.Line(key.Kind).Check);
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
