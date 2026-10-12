using System.Text;
using Aether.Core.Entries;

namespace Aether.Core;

internal sealed class WelcomeText
{
    private const string PlaceholderError = "可用占位符为 {@}（只能放开头）、{昵称}、{大航海}；花括号本身用 {{ 和 }}。";
    private readonly List<(string Kind, string Text)> parts = [];
    public bool Mention { get; }

    public WelcomeText(string text)
    {
        var literal = new StringBuilder();
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (c is not ('{' or '}')) { literal.Append(c); i++; continue; }
            if (i + 1 < text.Length && text[i + 1] == c) { literal.Append(c); i += 2; continue; }
            var end = text.IndexOf('}', i + 1);
            if (c != '{' || end < 0) throw new FormatException(PlaceholderError);
            var kind = text[(i + 1)..end];
            if (kind is not ("@" or "昵称" or "大航海") || kind == "@" && i != 0)
                throw new FormatException(PlaceholderError);
            parts.Add(("", literal.ToString()));
            literal.Clear();
            if (kind == "@") Mention = true;
            else parts.Add((kind, ""));
            i = end + 1;
        }
        parts.Add(("", literal.ToString()));
        if (parts.All(part => part.Kind == "" && string.IsNullOrWhiteSpace(part.Text)))
            throw new FormatException("欢迎语不能只有 {@}，还要写上发出去的文字。");
    }

    public static string? Validate(string text, int limit)
    {
        try
        {
            // Every guard name counts 4, so any one stands in for {大航海}.
            var length = new WelcomeText(text).FixedLength("舰长");
            return length > limit ? $"欢迎语固定部分计数为 {length}，超过上限 {limit}。" : null;
        }
        catch (FormatException error) { return error.Message; }
    }

    public string Render(string nickname, long guard, int limit)
    {
        var guardName = Entry.GuardName(guard);
        var fixedLength = FixedLength(guardName);
        var occurrences = parts.Count(part => part.Kind == "昵称");
        var budget = occurrences == 0 ? 0 : (limit - fixedLength) / occurrences;
        var end = 0;
        // ponytail: truncates code points, which may split a ZWJ emoji; use grapheme clusters if that matters in practice.
        foreach (var rune in nickname.EnumerateRunes())
        {
            budget -= Weight(rune);
            if (budget < 0) break;
            end += rune.Utf16SequenceLength;
        }
        nickname = nickname[..end];
        return string.Concat(parts.Select(part => part.Kind switch
        {
            "昵称" => nickname, "大航海" => guardName, _ => part.Text
        }));
    }

    /// <summary>Everything but the nicknames, which share what is left; {@} counts nothing.</summary>
    private int FixedLength(string guardName) => parts.Sum(part => Length(part.Kind == "大航海" ? guardName : part.Text));

    private static int Length(string text) => text.EnumerateRunes().Sum(Weight);

    private static int Weight(Rune rune) => rune.Value is >= 0x20 and <= 0x7e ? 1 : 2;
}
