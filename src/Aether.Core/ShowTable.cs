using System.Text.RegularExpressions;

namespace Aether.Core;

/// <summary>The 演出表 (#343): every 演出 of the 桌宠, her 日常小动作 and 心情, each with its 演出档, the kinds of lines it says and
/// what a 试演 of it can choose; the kinds of lines with the placeholders each may hold; and the groups both are listed in. One
/// file, Pages/desktop-pet/shows.json, which Core, the GUI and the 桌宠页 all read; a new 演出 is a few more rows there
/// (desktop-pet/README.md).</summary>
public sealed record ShowTable(IReadOnlyList<ShowGroup> Groups, IReadOnlyList<Show> Shows, IReadOnlyList<LineKind> Lines,
    IReadOnlyDictionary<string, string> Variants)
{
    /// <summary>The table in the program's Pages.</summary>
    public static ShowTable Current { get; } = Character.Read<ShowTable>(Path.Combine(WebServer.PagesDirectory, "desktop-pet"), "shows.json");

    public LineKind Line(string id) => Lines.Single(kind => kind.Id == id);
}

/// <summary>A group the GUI lists 演出 and kinds of lines under, in the table's order; only a group of 演出 has a note.</summary>
public sealed record ShowGroup(string Id, string Label, string? Note = null);

/// <summary>A row of the 演出表: its id, which settings and the 桌宠页 know it by; its group; its label; its 演出档; the kinds of
/// lines it says; its 试演, each a variant of <see cref="ShowTable.Variants"/> or null for the row itself; and, for a 演出 the
/// GUI lists by group, a note on when it plays ({mid}, {big} and {big-1} stand for the 中礼物 and 大礼物 thresholds in 电池).</summary>
public sealed record Show(string Id, string Group, string Label, string Tier, IReadOnlyList<string> Lines, IReadOnlyList<string?> Tests,
    string? Note = null)
{
    /// <summary>A 心情 comes of how quiet the room has been, never of a switch.</summary>
    public bool CanTurnOff => Group != "mood";
}

/// <summary>A kind of line: when she says it and the placeholders it may hold, by their names in the braces.</summary>
public sealed partial record LineKind(string Id, string Group, string Label, string Note, IReadOnlyList<string> Placeholders)
{
    /// <summary>Why <paramref name="lines"/> cannot be this kind's lines; null when they can. A brace that names nothing is plain
    /// text.</summary>
    public string? Check(IReadOnlyList<string> lines) =>
        lines.Count == 0 ? "台词列表不能为空。"
        : lines.Any(string.IsNullOrWhiteSpace) ? "每一句都不能为空。"
        : lines.SelectMany(line => Placeholder().Matches(line)).Select(match => match.Groups[1].Value)
            .FirstOrDefault(name => !Placeholders.Contains(name)) is { } wrong
            ? $"这一类台词不能用 {{{wrong}}}，" + (Placeholders.Count == 0 ? "没有可用的占位。" : $"只能用 {string.Join("、", Placeholders.Select(name => $"{{{name}}}"))}。")
        : null;

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex Placeholder();
}
