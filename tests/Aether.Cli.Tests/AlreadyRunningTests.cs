using Aether.Core;

namespace Aether.Cli.Tests;

public sealed class AlreadyRunningTests
{
    // ADR 0018: another process holds data/ for the whole 5-second wait, so this one says so and writes nothing there.
    [Fact]
    public async Task A_second_cli_on_the_same_data_directory_says_aether_is_running_and_exits_1()
    {
        using var cli = new CliCopy();
        await using var held = await DataDirectory.LockAsync(cli.Data, TimeProvider.System);
        Assert.NotNull(held);
        var (exitCode, output, error) = await cli.RunAsync("logout");
        Assert.Equal(1, exitCode);
        Assert.Equal(DataDirectory.AlreadyRunning, error.Trim());
        Assert.Equal("", output);
        Assert.Equal(["aether.lock"], Directory.EnumerateFileSystemEntries(cli.Data).Select(Path.GetFileName));
    }
}
