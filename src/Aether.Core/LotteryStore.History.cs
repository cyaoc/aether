using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
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

    public LotteryHistoryDetails? HistoryDetails(long roomId, string id)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var lottery = ReadHistory(connection, transaction, roomId, id).SingleOrDefault();
        if (lottery is null) return null;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT result FROM lottery_draw WHERE lottery_id = $id ORDER BY rowid";
        command.Parameters.AddWithValue("$id", id);
        var entries = ImmutableArray.CreateBuilder<EntryDraw>();
        var pool = ImmutableArray.CreateBuilder<PoolDraw>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (lottery.Mode == LotteryMode.Entry) entries.Add(JsonSerializer.Deserialize<EntryDraw>(reader.GetString(0))!);
            else pool.Add(JsonSerializer.Deserialize<PoolDraw>(reader.GetString(0))!);
        return new(lottery, entries.ToImmutable(), pool.ToImmutable());
    }

    private static ImmutableArray<LotteryHistory> ReadHistory(SqliteConnection connection, SqliteTransaction transaction,
        long roomId, string? id = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var filter = " WHERE l.room_id = $room" + (id is null ? "" : " AND l.id = $id");
        command.Parameters.AddWithValue("$room", roomId);
        if (id is not null) command.Parameters.AddWithValue("$id", id);
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
                items[item.Id] = item with { Awards = item.Awards.Add(new(reader.GetString(1), reader.GetString(2), reader.GetInt32(3), (PrizeGrade)reader.GetInt32(4))) };
            }
        command.CommandText = "SELECT p.lottery_id, p.name, p.weight, p.grade FROM lottery_prize p JOIN lottery l ON l.id = p.lottery_id"
            + filter + " ORDER BY p.position";
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var item = items[reader.GetString(0)];
                items[item.Id] = item with { Prizes = item.Prizes.Add(new(reader.GetString(1), reader.GetInt32(2), (PrizeGrade)reader.GetInt32(3))) };
            }
        return [.. order.Select(key => items[key])];
    }
}
