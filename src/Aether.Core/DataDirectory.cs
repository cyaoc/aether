namespace Aether.Core;

public static class DataDirectory
{
    /// <summary>What a shell says when another process still holds data/ after the wait (ADR 0018).</summary>
    public const string AlreadyRunning = "Aether 已经在运行";

    // Long enough for the old version to let go when an update restarts the GUI.
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(100);

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

    /// <summary>ADR 0018: locks data/aether.lock until the returned stream is disposed or the process ends, waiting up to
    /// 5 seconds for another process to let go of it; null when one still holds it.</summary>
    // ponytail: On Unix this is a best-effort flock: .NET silently skips it when DOTNET_SYSTEM_IO_DISABLEFILELOCKING is set
    // or the filesystem rejects flock (some network mounts). Fine while data/ sits on a local disk, as the README asks.
    // For OpenOrCreate + FileShare.None + DeleteOnClose, .NET verifies the inode after locking on Unix
    // and reopens if another owner deleted/replaced the path during handoff.
    public static async Task<FileStream?> LockAsync(string dataDirectory, TimeProvider time)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "aether.lock");
        var deadline = time.GetUtcNow() + LockWait;
        while (true)
        {
            try
            {
                return new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None,
                    Options = FileOptions.DeleteOnClose,
                });
            }
            catch (IOException error) when (FileLock.IsHeld(error))
            {
                if (time.GetUtcNow() >= deadline) return null;
            }
            await Task.Delay(LockRetry, time);
        }
    }
}
