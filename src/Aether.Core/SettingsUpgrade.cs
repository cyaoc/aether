using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using Tokens = YamlDotNet.Core.Tokens;

namespace Aether.Core;

internal static class SettingsUpgrade
{
    internal const int CurrentVersion = 1;

    // The 0.2.x spellings, frozen here rather than read from Settings.WelcomeGuards, which may change.
    private static readonly string[] V0Guards = ["关闭", "舰长", "提督", "总督"];

    internal static string Apply(string source, int version, string path)
    {
        while (version < CurrentVersion)
        {
            source = version switch
            {
                0 => UpgradeV0(source, path),
                _ => throw new InvalidOperationException("缺少设置文件升级步骤。"),
            };
            version++;
        }
        return source;
    }

    private static string UpgradeV0(string source, string path)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(source));
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var renames = new[] { ("welcome", "welcome_danmaku"), ("welcome_animation", "entry_animation"),
            ("welcome_animation_videos", "entry_animation_viewers") };
        var groups = renames.Where(rename => !root.Children.Keys.OfType<YamlScalarNode>().Any(key => key.Value == rename.Item2))
            .SelectMany(rename => root.Children.Where(pair => pair.Key is YamlScalarNode key && key.Value == rename.Item1)
                .Select(pair => (Old: rename.Item1, New: rename.Item2, pair.Key, pair.Value))).ToArray();
        if (groups.Length == 0) return source;
        var tokens = Settings.Scan(source);
        var edits = new List<(int Start, int Length, string Value)>();
        var newline = Settings.NewlineOf(source);
        if (root.Style != MappingStyle.Block || !root.Anchor.IsEmpty) throw CannotEdit(root);
        foreach (var group in groups)
        {
            ReplaceScalar(group.Key, group.New);
            if (group.Value is not YamlMappingNode mapping) continue;
            if (!mapping.Anchor.IsEmpty) throw CannotEdit(mapping);
            if (group.Old == "welcome")
                foreach (var pair in mapping.Children.Where(pair => pair.Key is YamlScalarNode { Value: "guard" }))
                    if (pair.Value is YamlScalarNode { Value: { } guard }
                        && Array.IndexOf(V0Guards, guard) is var tier && tier >= 0)
                        ReplaceScalar(pair.Value, tier.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (group.Old != "welcome_animation") continue;
            foreach (var (key, value) in mapping.Children)
            {
                // Only the spelling Settings reads as on; True or TRUE never worked in 0.2.x either.
                if (key is not YamlScalarNode { Value: "enabled" } || value is not YamlScalarNode { Value: "true" }) continue;
                if (mapping.Style != MappingStyle.Block || key.Start.Line != Settings.EndOf(tokens, value).Line)
                    throw CannotEdit(mapping);
                var at = LineEnd((int)Settings.EndOf(tokens, value).Index);
                var indent = new string(' ', (int)tokens.OfType<Tokens.BlockMappingStart>()
                    .First(token => token.Start.Index >= mapping.Start.Index && token.Start.Index <= key.Start.Index).Start.Column - 1);
                var added = "";
                if (!mapping.Children.Keys.OfType<YamlScalarNode>().Any(item => item.Value == "fan_club_level"))
                    added += indent + "fan_club_level: 0" + newline;
                if (!mapping.Children.Keys.OfType<YamlScalarNode>().Any(item => item.Value == "volume"))
                    added += indent + "volume: 100" + newline;
                edits.Add((at, 0, (at > 0 && !Settings.LineBreaks.Contains(source[at - 1]) ? newline : "") + added));
            }
        }
        var comments = new Dictionary<string, string>
        {
            ["# 大航海身份达到哪一档就欢迎：关闭、舰长、提督、总督；提督表示提督和总督"] = "# 大航海身份门槛：0 关闭，1 舰长及以上，2 提督及以上，3 总督",
            ["# 欢迎动画，下次观看直播间时生效；先开 Aether，再开推流软件"] = "# 进场欢迎：动画。进场动画和欢迎动画共用这个开关；GUI 保存后下一次进场生效，CLI 下次观看时生效",
            ["# true 或 false，映射里有这位观众时，进场在直播画面上播放"] = "",
            ["# 欢迎动画的视频，放在 data/videos/ 里，每行一位观众：UID: 文件名"] = "# 动画名单：一行一位，值是 30、40、50、60，0 表示不播，或视频文件名",
            ["# UID 必须为正整数；文件名只能是 .mp4 或 .webm，不能带路径；空分组表示没有观众"] = "# UID 必须为正整数；视频放在 data/videos/，只能是 .mp4、.webm 文件名，不能带路径",
        };
        // Only scanner-recognized comments may change; a block scalar can contain identical text.
        foreach (var comment in tokens.OfType<Tokens.Comment>())
        {
            var start = LineStart((int)comment.Start.Index);
            var end = (int)comment.End.Index;
            var line = source[start..end];
            if (!comments.TryGetValue(line.TrimStart(' '), out var replacement)) continue;
            edits.Add(replacement.Length == 0 ? (start, LineEnd(end) - start, "")
                : ((int)comment.Start.Index, end - (int)comment.Start.Index, replacement));
        }
        var versionKey = root.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: "format_version" });
        if (versionKey.Key is not null) ReplaceScalar(versionKey.Value, "1");
        else
        {
            var first = tokens.OfType<Tokens.BlockMappingStart>().First();
            var indent = new string(' ', (int)first.Start.Column - 1);
            // Above the comments that lead the first group, as in Template, so they stay with the group they describe.
            var lead = tokens.IndexOf(first);
            while (lead > 0 && tokens[lead - 1] is Tokens.Comment { IsInline: false }) lead--;
            var at = LineStart((int)tokens[lead].Start.Index);
            if (!string.IsNullOrWhiteSpace(source[at..(int)tokens[lead].Start.Index])) throw CannotEdit(root);
            edits.Add((at, 0, indent + "# 设置文件格式版本，由程序维护" + newline + indent + "format_version: 1" + newline + newline));
        }
        // Replacements before inserts at the same offset, so an insert is never removed as the text it precedes.
        foreach (var edit in edits.OrderByDescending(edit => edit.Start).ThenByDescending(edit => edit.Length))
            source = source.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        return source;

        void ReplaceScalar(YamlNode node, string value)
        {
            // Alias nodes point at their anchor definition, which may belong to an unrelated setting.
            if (!node.Anchor.IsEmpty) throw CannotEdit(node);
            // Replace only the scalar token, preserving tags and surrounding whitespace.
            var scalar = tokens.OfType<Tokens.Scalar>().FirstOrDefault(token => token.Start.Index >= node.Start.Index
                && token.End.Index <= node.End.Index);
            if (scalar is null || scalar.Start.Line != scalar.End.Line) throw CannotEdit(node);
            edits.Add(((int)scalar.Start.Index, (int)(scalar.End.Index - scalar.Start.Index), value));
        }
        int LineStart(int at)
        {
            while (at > 0 && !Settings.LineBreaks.Contains(source[at - 1])) at--;
            return at;
        }
        int LineEnd(int at)
        {
            while (at < source.Length && !Settings.LineBreaks.Contains(source[at])) at++;
            return at == source.Length ? at : at + (source.AsSpan(at).StartsWith("\r\n") ? 2 : 1);
        }
        SettingsException CannotEdit(YamlNode node) => new(path, node.Start.Line, "升级",
            "不能安全地逐行升级，请将相关分组改成每项一行的块状写法，并移除锚点后重试。");
    }
}
