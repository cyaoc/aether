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
        // A copy, so a parse regression must not reach the repository's data.
        using var cli = new CliCopy();
        var (exitCode, output, error) = await cli.RunAsync("watch", room);
        Assert.NotEqual(0, exitCode);
        Assert.Contains("请输入有效的房间号（正整数）。", error);
        Assert.Equal("", output);
        Assert.False(Directory.Exists(cli.Data));
    }
}
