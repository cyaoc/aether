using System.Globalization;

namespace Aether.Core;

internal static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N");

    /// <summary>A positive int64 written as plain digits, ignoring surrounding whitespace and the current culture.</summary>
    public static bool TryParsePositive(string? text, out long id) =>
        long.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
