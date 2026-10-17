using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Aether.Core;

internal sealed partial class LotteryStore
{
    public EntryLottery? LoadEntry(long roomId, bool latest = false)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        // Keep an ended entry lottery visible until the host hides it or creates the next lottery.
        command.CommandText = "SELECT id, keyword, started_at, phase, draw_until, ended_at, hidden FROM lottery WHERE room_id = $room AND mode = 'entry'"
            + (latest ? " ORDER BY rowid DESC LIMIT 1" : " AND rowid = (SELECT max(rowid) FROM lottery WHERE room_id = $room)");
        command.Parameters.AddWithValue("$room", roomId);
        EntryLottery lottery;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return null;
            lottery = new(reader.GetString(0), roomId, reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                [], [], [], (EntryLotteryPhase)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture), reader.GetBoolean(6));
        }
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", lottery.Id);
        command.CommandText = "SELECT name, prize, capacity, grade FROM lottery_award WHERE lottery_id = $id ORDER BY position";
        var awards = ImmutableArray.CreateBuilder<LotteryAward>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) awards.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), (PrizeGrade)reader.GetInt32(3)));
        command.CommandText = "SELECT uid, nickname, entered_at FROM lottery_entry WHERE lottery_id = $id ORDER BY rowid";
        var entries = ImmutableArray.CreateBuilder<LotteryEntry>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) entries.Add(new(reader.GetInt64(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        command.CommandText = "SELECT result FROM lottery_draw WHERE lottery_id = $id ORDER BY rowid";
        var draws = ImmutableArray.CreateBuilder<EntryDraw>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) draws.Add(JsonSerializer.Deserialize<EntryDraw>(reader.GetString(0))!);
        return lottery with { Awards = awards.ToImmutable(), Entries = entries.ToImmutable(), Draws = draws.ToImmutable() };
    }

    public void Save(EntryLottery lottery, DateTimeOffset? endCurrentAt = null, EntryDraw? draw = null)
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
            INSERT INTO lottery (id, room_id, mode, stop_word, keyword, started_at, ended_at, phase, draw_until, hidden)
            VALUES ($id, $room, 'entry', '', $keyword, $started, $ended, $phase, $until, $hidden)
            ON CONFLICT(id) DO UPDATE SET keyword = $keyword, ended_at = $ended, phase = $phase, draw_until = $until, hidden = $hidden;
            DELETE FROM lottery_award WHERE lottery_id = $id;
            """;
        command.Parameters.AddWithValue("$id", lottery.Id);
        command.Parameters.AddWithValue("$room", lottery.RoomId);
        command.Parameters.AddWithValue("$keyword", lottery.Keyword);
        command.Parameters.AddWithValue("$started", lottery.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$ended", (object?)lottery.EndedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$phase", (int)lottery.Phase);
        command.Parameters.AddWithValue("$until", (object?)lottery.DrawUntil?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$hidden", lottery.Hidden);
        command.ExecuteNonQuery();
        for (var index = 0; index < lottery.Awards.Length; index++)
        {
            var award = lottery.Awards[index];
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO lottery_award (lottery_id, position, name, prize, capacity, grade) VALUES ($id, $position, $name, $prize, $capacity, $grade)";
            command.Parameters.AddWithValue("$id", lottery.Id);
            command.Parameters.AddWithValue("$position", index);
            command.Parameters.AddWithValue("$name", award.Name);
            command.Parameters.AddWithValue("$prize", award.Prize);
            command.Parameters.AddWithValue("$capacity", award.Capacity);
            command.Parameters.AddWithValue("$grade", (int)award.Grade);
            command.ExecuteNonQuery();
        }
        if (draw is not null)
        {
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO lottery_draw (id, lottery_id, result) VALUES ($id, $lottery, $result)";
            command.Parameters.AddWithValue("$id", draw.Id);
            command.Parameters.AddWithValue("$lottery", lottery.Id);
            command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(draw));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void AddEntry(string lotteryId, LotteryEntry entry)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO lottery_entry (lottery_id, uid, nickname, entered_at) VALUES ($id, $uid, $name, $at)";
        command.Parameters.AddWithValue("$id", lotteryId);
        command.Parameters.AddWithValue("$uid", entry.Uid);
        command.Parameters.AddWithValue("$name", entry.Nickname);
        command.Parameters.AddWithValue("$at", entry.EnteredAt.ToString("O"));
        command.ExecuteNonQuery();
    }
}
