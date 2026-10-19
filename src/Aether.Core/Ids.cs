namespace Aether.Core;

internal static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N");
}
