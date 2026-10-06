namespace Aether.Core;

public static class DataDirectory
{
    /// <summary>ADR 0002: Release uses the executable's directory; Debug prefers the directory holding Aether.slnx.</summary>
    public static string Locate()
    {
        var root = AppContext.BaseDirectory;
#if DEBUG
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Aether.slnx"))) continue;
            root = directory.FullName;
            break;
        }
#endif
        return Path.Combine(root, "data");
    }
}
