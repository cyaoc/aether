namespace Aether.Core;

public static class AtomicFile
{
    /// <summary>Write a temporary file next to path, then move it into place, so a crash never leaves half a file.</summary>
    public static void Write(string path, byte[] content, bool overwrite = true)
    {
        var temporary = path + "." + Guid.NewGuid() + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                output.Write(content);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite);
        }
        finally { File.Delete(temporary); }
    }
}
