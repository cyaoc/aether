using System.Collections.Immutable;
using System.Text.Json;

namespace Aether.Core;

internal sealed partial class LotteryStore
{
    public ImmutableArray<LotteryPrizeSet> PrizeSets(LotteryMode mode)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, saved_at, content FROM lottery_prize_set WHERE mode = $mode ORDER BY julianday(saved_at) DESC, rowid DESC";
        command.Parameters.AddWithValue("$mode", ModeText(mode));
        using var reader = command.ExecuteReader();
        var sets = ImmutableArray.CreateBuilder<LotteryPrizeSet>();
        while (reader.Read())
            sets.Add(new(mode, reader.GetString(0), reader.GetDateTimeOffset(1),
                mode == LotteryMode.Pool ? JsonSerializer.Deserialize<ImmutableArray<PoolPrize>>(reader.GetString(2)) : [],
                mode == LotteryMode.Entry ? JsonSerializer.Deserialize<ImmutableArray<LotteryAward>>(reader.GetString(2)) : []));
        return sets.ToImmutable();
    }

    public void SavePrizeSet(LotteryPrizeSet set)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO lottery_prize_set(mode, name, saved_at, content) VALUES ($mode, $name, $saved, $content)";
        command.Parameters.AddWithValue("$mode", ModeText(set.Mode));
        command.Parameters.AddWithValue("$name", set.Name);
        command.Set("$saved", set.SavedAt);
        command.Parameters.AddWithValue("$content", set.Mode == LotteryMode.Pool ? JsonSerializer.Serialize(set.Prizes) : JsonSerializer.Serialize(set.Awards));
        command.ExecuteNonQuery();
    }

    public void DeletePrizeSet(LotteryMode mode, string name)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM lottery_prize_set WHERE mode = $mode AND name = $name";
        command.Parameters.AddWithValue("$mode", ModeText(mode));
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }
}
