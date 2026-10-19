using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Aether.Core;

internal sealed partial class LotteryStore
{
    public ImmutableArray<LotteryHistory> History(long roomId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        return ReadHistory(connection, transaction, roomId);
    }

    private static ImmutableArray<LotteryHistory> ReadHistory(SqliteConnection connection, SqliteTransaction transaction,
        long roomId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        const string filter = " WHERE l.room_id = $room";
        command.Parameters.AddWithValue("$room", roomId);
        command.CommandText = """
            SELECT l.id, l.mode, l.started_at, l.ended_at, l.keyword, l.stop_word,
                (SELECT count(*) FROM lottery_entry WHERE lottery_id = l.id),
                (SELECT count(*) FROM lottery_draw WHERE lottery_id = l.id)
            FROM lottery l
            """ + filter + " ORDER BY julianday(l.started_at) DESC, l.rowid DESC";
        var items = new Dictionary<string, LotteryHistory>();
        var order = new List<string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var key = reader.GetString(0);
                order.Add(key);
                items.Add(key, new(key, roomId, reader.GetString(1) == "entry" ? LotteryMode.Entry : LotteryMode.Pool,
                    DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                    reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                    reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7), [], []));
            }
        command.CommandText = "SELECT a.lottery_id, a.name, a.prize, a.capacity, a.grade FROM lottery_award a JOIN lottery l ON l.id = a.lottery_id"
            + filter + " ORDER BY a.position";
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var item = items[reader.GetString(0)];
                items[item.Id] = item with { Awards = item.Awards.Add(new(reader.GetString(1), reader.GetString(2), reader.GetInt32(3), (Grade)reader.GetInt32(4))) };
            }
        command.CommandText = "SELECT p.lottery_id, p.name, p.weight, p.grade FROM lottery_prize p JOIN lottery l ON l.id = p.lottery_id"
            + filter + " ORDER BY p.position";
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var item = items[reader.GetString(0)];
                items[item.Id] = item with { Prizes = item.Prizes.Add(new(reader.GetString(1), reader.GetInt32(2), (Grade)reader.GetInt32(3))) };
            }
        return [.. order.Select(key => items[key])];
    }
}
