using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class GuiStateTests
{
    [Fact]
    public async Task Saved_state_is_read_back_by_a_new_client()
    {
        await using var h = new WatchHarness();
        h.Client.GuiState.Save(new(false, ["006", "42"]));
        h.Client.Dispose();

        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        var state = restarted.GuiState.Load();

        Assert.False(state.KeepRecentDanmaku);
        Assert.Equal(["006", "42"], state.RecentRooms);
        Assert.Equal(1L, TestDatabase.RowCount(h.DataDirectory, "gui_state"));
    }

    [Theory]
    [InlineData(new[] { "+123", "42", "１２３" }, new[] { "42" })]
    [InlineData(new[] { "6", "006", "invalid", "0", "-1", "6", "42", "123", "456", "789" }, new[] { "6", "006", "42", "123", "456" })]
    public void Recent_rooms_keep_valid_numbers_as_typed_without_repeats_and_at_most_five(string[] rooms, string[] expected)
    {
        Assert.Equal(expected, new GuiState(true, [.. rooms]).RecentRooms);
        Assert.Equal(expected, (GuiState.Default with { RecentRooms = [.. rooms] }).RecentRooms);
    }
}
