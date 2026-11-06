namespace Aether.Core;

/// <summary>Lock files held by the operating system: opened with FileShare.None, a sharing lock on Windows and an flock elsewhere.</summary>
public static class FileLock
{
    // .NET exposes ERROR_SHARING_VIOLATION on Windows, but raw EWOULDBLOCK on Unix (35 on macOS, 11 on Linux).
    private static readonly int HeldHResult = OperatingSystem.IsWindows()
        ? unchecked((int)0x80070020) : OperatingSystem.IsMacOS() ? 35 : 11;

    /// <summary>Whether opening the lock file failed because another process holds it, rather than for any other reason.</summary>
    public static bool IsHeld(IOException error) => error.HResult == HeldHResult;
}
