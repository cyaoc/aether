namespace Aether.Core;

public static class Viewer
{
    public static bool TryParseUid(string? text, out long uid) => Ids.TryParsePositive(text, out uid);
}
