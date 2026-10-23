using System.Diagnostics;

namespace Aether.Cli.Tests;

public sealed class RoomArgumentTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("+123")]
    [InlineData("9223372036854775808")]
    [InlineData("１２３")]
    public async Task Invalid_room_number_fails_during_argument_parsing(string room)
    {
        var directory = Directory.CreateTempSubdirectory("aether-cli-room-");
        try
        {
            var assembly = typeof(WatchCommand).Assembly.Location;
            // Debug locates data from the executable, so a parse regression must not reach the repository's data.
            foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(assembly)!))
                File.Copy(file, Path.Combine(directory.FullName, Path.GetFileName(file)));
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(Path.Combine(directory.FullName, Path.GetFileName(assembly)));
            start.ArgumentList.Add("watch");
            start.ArgumentList.Add(room);
            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.NotEqual(0, process.ExitCode);
                Assert.Contains("请输入有效的房间号（正整数）。", await error);
                Assert.Equal("", await output);
                Assert.False(Directory.Exists(Path.Combine(directory.FullName, "data")));
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
        }
        finally { directory.Delete(recursive: true); }
    }
}
