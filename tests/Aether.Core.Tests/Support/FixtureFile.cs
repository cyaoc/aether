namespace Aether.Core.Tests.Support;

/// <summary>A file under Fixtures/, which the build copies next to the tests.</summary>
internal static class FixtureFile
{
    /// <summary>The lines of <paramref name="name"/>, a message each in a .jsonl capture.</summary>
    public static string[] Lines(string name) => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
