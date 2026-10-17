using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Aether.Core;

internal sealed class LotteryStore(Database database)
{
    public Lottery? Load(long roomId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, stop_word, started_at, phase, uid, draw_until FROM lottery WHERE room_id = $room AND ended_at IS NULL";
        command.Parameters.AddWithValue("$room", roomId);
        Lottery lottery;
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
            while (reader.Read()) prizes.Add(new(reader.GetString(0), reader.GetInt32(1), (PrizeGrade)reader.GetInt32(2)));
        command.CommandText = "SELECT result FROM lottery_draw WHERE lottery_id = $id ORDER BY rowid";
        var draws = ImmutableArray.CreateBuilder<PoolDraw>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) draws.Add(JsonSerializer.Deserialize<PoolDraw>(reader.GetString(0))!);
        command.CommandText = "SELECT uid, nickname FROM lottery_viewer WHERE lottery_id = $id";
        var viewers = ImmutableDictionary.CreateBuilder<long, string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) viewers.Add(reader.GetInt64(0), reader.GetString(1));
        return lottery with { Prizes = prizes.ToImmutable(), Draws = draws.ToImmutable(), Viewers = viewers.ToImmutable() };
    }

    public void Save(Lottery lottery, DateTimeOffset? endCurrentAt = null, PoolDraw? draw = null)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (endCurrentAt is { } ended)
        {
            command.CommandText = "UPDATE lottery SET ended_at = $ended WHERE room_id = $room AND ended_at IS NULL";
            command.Parameters.AddWithValue("$ended", ended.ToString("O"));
            command.Parameters.AddWithValue("$room", lottery.RoomId);
            command.ExecuteNonQuery();
            command.Parameters.Clear();
        }
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
        if (draw is not null)
        {
            command.Parameters.Clear();
            // Keep the prize and viewer as drawn, even when next time's pool or nickname changes.
            command.CommandText = "INSERT INTO lottery_draw (id, lottery_id, result) VALUES ($id, $lottery, $result)";
            command.Parameters.AddWithValue("$id", draw.Id);
            command.Parameters.AddWithValue("$lottery", lottery.Id);
            command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(draw));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void RememberViewer(string lotteryId, long uid, string nickname)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO lottery_viewer (lottery_id, uid, nickname) VALUES ($id, $uid, $name)
            ON CONFLICT(lottery_id, uid) DO UPDATE SET nickname = $name
            """;
        command.Parameters.AddWithValue("$id", lotteryId);
        command.Parameters.AddWithValue("$uid", uid);
        command.Parameters.AddWithValue("$name", nickname);
        command.ExecuteNonQuery();
    }
}
