using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Aether.Core;

internal sealed partial class LotteryStore(Database database)
{
    public PoolLottery? LoadPool(long roomId, bool latest = false)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, stop_word, started_at, phase, uid, draw_until FROM lottery WHERE room_id = $room AND mode = 'pool'"
            + (latest ? " ORDER BY rowid DESC LIMIT 1" : " AND ended_at IS NULL");
        command.Parameters.AddWithValue("$room", roomId);
        PoolLottery lottery;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return null;
            lottery = new(reader.GetString(0), roomId, reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                [], [], ImmutableDictionary<long, string>.Empty, (PoolLotteryPhase)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture));
        }
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", lottery.Id);
        command.CommandText = "SELECT name, weight, grade FROM lottery_prize WHERE lottery_id = $id ORDER BY position";
        var prizes = ImmutableArray.CreateBuilder<PoolPrize>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) prizes.Add(new(reader.GetString(0), reader.GetInt32(1), (Grade)reader.GetInt32(2)));
        return lottery with { Prizes = prizes.ToImmutable(), Draws = ReadDraws<PoolDraw>(command, lottery.Id) };
    }

    public void Save(PoolLottery lottery, DateTimeOffset? endCurrentAt = null, PoolDraw? draw = null)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        EndCurrent(command, lottery.RoomId, endCurrentAt);
        command.CommandText = """
            INSERT INTO lottery (id, room_id, mode, stop_word, started_at, phase, uid, draw_until)
            VALUES ($id, $room, 'pool', $word, $started, $phase, $uid, $until)
            ON CONFLICT(id) DO UPDATE SET stop_word = $word, phase = $phase, uid = $uid, draw_until = $until;
            DELETE FROM lottery_prize WHERE lottery_id = $id;
            """;
        command.Parameters.AddWithValue("$id", lottery.Id);
        command.Parameters.AddWithValue("$room", lottery.RoomId);
        command.Parameters.AddWithValue("$word", lottery.StopWord);
        command.Parameters.AddWithValue("$started", lottery.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$phase", (int)lottery.Phase);
        command.Parameters.AddWithValue("$uid", (object?)lottery.Uid ?? DBNull.Value);
        command.Parameters.AddWithValue("$until", (object?)lottery.DrawUntil?.ToString("O") ?? DBNull.Value);
        command.ExecuteNonQuery();
        for (var index = 0; index < lottery.Prizes.Length; index++)
        {
            var prize = lottery.Prizes[index];
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO lottery_prize (lottery_id, position, name, weight, grade) VALUES ($id, $position, $name, $weight, $grade)";
            command.Parameters.AddWithValue("$id", lottery.Id);
            command.Parameters.AddWithValue("$position", index);
            command.Parameters.AddWithValue("$name", prize.Name);
            command.Parameters.AddWithValue("$weight", prize.Weight);
            command.Parameters.AddWithValue("$grade", (int)prize.Grade);
            command.ExecuteNonQuery();
        }
        if (draw is not null) AddDraw(command, lottery.Id, draw.Id, JsonSerializer.Serialize(draw));
        transaction.Commit();
    }

    private static void EndCurrent(SqliteCommand command, long roomId, DateTimeOffset? endedAt)
    {
        if (endedAt is not { } ended) return;
        command.CommandText = "UPDATE lottery SET ended_at = $ended WHERE room_id = $room AND ended_at IS NULL";
        command.Parameters.AddWithValue("$ended", ended.ToString("O"));
        command.Parameters.AddWithValue("$room", roomId);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
    }

    /// <summary>Keeps the prize and viewer as drawn, even when next time's pool or nickname changes.</summary>
    private static void AddDraw(SqliteCommand command, string lotteryId, string drawId, string result)
    {
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO lottery_draw (id, lottery_id, result) VALUES ($id, $lottery, $result)";
        command.Parameters.AddWithValue("$id", drawId);
        command.Parameters.AddWithValue("$lottery", lotteryId);
        command.Parameters.AddWithValue("$result", result);
        command.ExecuteNonQuery();
    }

    private static ImmutableArray<T> ReadDraws<T>(SqliteCommand command, string lotteryId)
    {
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", lotteryId);
        command.CommandText = "SELECT result FROM lottery_draw WHERE lottery_id = $id ORDER BY rowid";
        var draws = ImmutableArray.CreateBuilder<T>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) draws.Add(JsonSerializer.Deserialize<T>(reader.GetString(0))!);
        return draws.ToImmutable();
    }
}
