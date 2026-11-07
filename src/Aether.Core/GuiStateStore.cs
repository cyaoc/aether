using System.Collections.Immutable;
using System.Text.Json;

namespace Aether.Core;

/// <summary>What the GUI keeps between runs (ADR 0016): the 只保留最近 1000 条 switch and the recent room numbers.</summary>
public sealed record GuiState(bool KeepRecentDanmaku, ImmutableArray<string> RecentRooms)
{
    private const int RecentRoomLimit = 5;
    public static GuiState Default { get; } = new(true, []);

    /// <summary>The latest connected first, as typed. Every list goes through here, so one rule holds wherever it came from.</summary>
    public ImmutableArray<string> RecentRooms { get; init => field = Recent(value); } = Recent(RecentRooms);

    private static ImmutableArray<string> Recent(IEnumerable<string> rooms) =>
        [.. rooms.Where(room => Room.TryParseNumber(room, out _)).Distinct().Take(RecentRoomLimit)];
}

/// <summary>The single gui_state row: reads and writes never touch another.</summary>
public sealed class GuiStateStore
{
    private readonly Database database;

    internal GuiStateStore(Database database) => this.database = database;

    public GuiState Load()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT keep_recent_danmaku, recent_rooms FROM gui_state WHERE id = 1";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new(reader.GetBoolean(0), JsonSerializer.Deserialize<ImmutableArray<string>>(reader.GetString(1)))
            : GuiState.Default;
    }

    public void Save(GuiState state)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE gui_state SET keep_recent_danmaku = $keep, recent_rooms = $rooms WHERE id = 1";
        command.Parameters.AddWithValue("$keep", state.KeepRecentDanmaku);
        command.Parameters.AddWithValue("$rooms", JsonSerializer.Serialize(state.RecentRooms));
        command.ExecuteNonQuery();
    }
}
