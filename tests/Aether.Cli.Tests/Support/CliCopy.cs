using System.Diagnostics;

namespace Aether.Cli.Tests;

/// <summary>The CLI copied into a temporary directory and run as its own process: Debug locates data/ from the executable, so a
/// run never reaches the repository's data/.</summary>
internal sealed class CliCopy : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("aether-cli-");
    private readonly string assembly;

    public CliCopy()
    {
        var built = typeof(WatchCommand).Assembly.Location;
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(built)!))
            File.Copy(file, Path.Combine(directory.FullName, Path.GetFileName(file)));
        assembly = Path.Combine(directory.FullName, Path.GetFileName(built));
    }

    /// <summary>The data/ this copy locates.</summary>
    public string Data => Path.Combine(directory.FullName, "data");

    public async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(assembly);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            return (process.ExitCode, await output, await error);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
    }

    public void Dispose() => directory.Delete(recursive: true);
}
