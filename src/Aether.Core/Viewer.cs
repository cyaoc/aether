using System.Globalization;

namespace Aether.Core;

public static class Viewer
{
    public static bool TryParseUid(string? text, out long uid) =>
        long.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uid) && uid > 0;
}
