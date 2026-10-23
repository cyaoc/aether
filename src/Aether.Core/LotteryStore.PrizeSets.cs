using System.Collections.Immutable;
using System.Text.Json;

namespace Aether.Core;

internal sealed partial class LotteryStore
{
    public ImmutableArray<LotteryPrizeSet> PrizeSets(long roomId, LotteryMode mode)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, saved_at, content FROM lottery_prize_set WHERE room_id = $room AND mode = $mode ORDER BY julianday(saved_at) DESC, rowid DESC";
        command.Parameters.AddWithValue("$room", roomId);
        command.Parameters.AddWithValue("$mode", mode.ToString());
        using var reader = command.ExecuteReader();
        var sets = ImmutableArray.CreateBuilder<LotteryPrizeSet>();
        while (reader.Read())
            sets.Add(new(mode, reader.GetString(0), reader.GetDateTimeOffset(1),
                mode == LotteryMode.Pool ? JsonSerializer.Deserialize<ImmutableArray<PoolPrize>>(reader.GetString(2)) : [],
                mode == LotteryMode.Entry ? JsonSerializer.Deserialize<ImmutableArray<LotteryAward>>(reader.GetString(2)) : []));
        return sets.ToImmutable();
    }

    public void SavePrizeSet(long roomId, LotteryPrizeSet set)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO lottery_prize_set(room_id, mode, name, saved_at, content) VALUES ($room, $mode, $name, $saved, $content)";
        command.Parameters.AddWithValue("$room", roomId);
        command.Parameters.AddWithValue("$mode", set.Mode.ToString());
        command.Parameters.AddWithValue("$name", set.Name);
        command.Parameters.AddWithValue("$saved", set.SavedAt.ToString("O"));
        command.Parameters.AddWithValue("$content", set.Mode == LotteryMode.Pool ? JsonSerializer.Serialize(set.Prizes) : JsonSerializer.Serialize(set.Awards));
        command.ExecuteNonQuery();
    }

    public void DeletePrizeSet(long roomId, LotteryMode mode, string name)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM lottery_prize_set WHERE room_id = $room AND mode = $mode AND name = $name";
        command.Parameters.AddWithValue("$room", roomId);
        command.Parameters.AddWithValue("$mode", mode.ToString());
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }
}
