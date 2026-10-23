using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Data.Sqlite;

namespace Aether.Core;

internal sealed partial class LotteryStore(Database database)
{
    // Visibility lives in its own column; the original draw JSON stays unchanged.
    private static readonly JsonSerializerOptions DrawJson = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { type =>
            {
                if (type.Properties.FirstOrDefault(property => property.Name == nameof(LotteryDraw.HiddenFromStream)) is { } visibility)
                    visibility.ShouldSerialize = (_, _) => false;
            } },
        },
    };

    public Lottery? Load(long roomId, LotteryMode? mode = null, string? id = null)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, mode, started_at, ended_at, phase, draw_until, stop_word, uid, keyword, hidden FROM lottery WHERE room_id = $room"
            + (mode is null ? "" : " AND mode = $mode") + (id is null ? "" : " AND id = $id") + " ORDER BY rowid DESC LIMIT 1";
        command.Parameters.AddWithValue("$room", roomId);
        if (mode is not null) command.Parameters.AddWithValue("$mode", ModeText(mode.Value));
        if (id is not null) command.Parameters.AddWithValue("$id", id);
        Lottery lottery;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return null;
            var key = reader.GetString(0);
            var startedAt = reader.GetDateTimeOffset(2);
            DateTimeOffset? endedAt = reader.GetNullable<DateTimeOffset>(3);
            var phase = reader.GetInt32(4);
            DateTimeOffset? drawUntil = reader.GetNullable<DateTimeOffset>(5);
            lottery = ReadMode(reader.GetString(1)) == LotteryMode.Entry
                ? new EntryLottery(key, roomId, reader.GetString(8), startedAt, [], [], [], (EntryLotteryPhase)phase,
                    drawUntil, endedAt, reader.GetBoolean(9))
                : new PoolLottery(key, roomId, reader.GetString(6), startedAt, [], [], ImmutableDictionary<long, string>.Empty,
                    (PoolLotteryPhase)phase, reader.GetNullable<long>(7), drawUntil, endedAt);
        }
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", lottery.Id);
        if (lottery is PoolLottery pool)
        {
            command.CommandText = "SELECT name, weight, grade FROM lottery_prize WHERE lottery_id = $id ORDER BY position";
            var prizes = ImmutableArray.CreateBuilder<PoolPrize>();
            using (var reader = command.ExecuteReader())
                while (reader.Read()) prizes.Add(new(reader.GetString(0), reader.GetInt32(1), (Grade)reader.GetInt32(2)));
            return pool with { Prizes = prizes.ToImmutable(), Draws = ReadDraws<PoolDraw>(command, lottery.Id) };
        }
        var entry = (EntryLottery)lottery;
        command.CommandText = "SELECT name, prize, capacity, grade FROM lottery_award WHERE lottery_id = $id ORDER BY position";
        var awards = ImmutableArray.CreateBuilder<LotteryAward>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) awards.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), (Grade)reader.GetInt32(3)));
        command.CommandText = "SELECT uid, nickname, entered_at FROM lottery_entry WHERE lottery_id = $id ORDER BY rowid";
        var entries = ImmutableArray.CreateBuilder<LotteryEntry>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) entries.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetDateTimeOffset(2)));
        return entry with { Awards = awards.ToImmutable(), Entries = entries.ToImmutable(), Draws = ReadDraws<EntryDraw>(command, lottery.Id) };
    }

    public void Save(Lottery lottery, DateTimeOffset? endCurrentAt = null, LotteryDraw? draw = null)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        EndCurrent(command, lottery.RoomId, endCurrentAt);
        var (mode, phase, stopWord, uid, keyword, hidden) = lottery switch
        {
            PoolLottery pool => (LotteryMode.Pool, (int)pool.Phase, pool.StopWord, pool.Uid, "", false),
            EntryLottery entry => (LotteryMode.Entry, (int)entry.Phase, "", (long?)null, entry.Keyword, entry.Hidden),
            _ => throw new ArgumentOutOfRangeException(nameof(lottery)),
        };
        command.CommandText = """
            INSERT INTO lottery (id, room_id, mode, started_at, ended_at, phase, draw_until, stop_word, uid, keyword, hidden)
            VALUES ($id, $room, $mode, $started, $ended, $phase, $until, $word, $uid, $keyword, $hidden)
            ON CONFLICT(id) DO UPDATE SET ended_at = $ended, phase = $phase, draw_until = $until,
                stop_word = $word, uid = $uid, keyword = $keyword, hidden = $hidden;
            DELETE FROM lottery_prize WHERE lottery_id = $id;
            DELETE FROM lottery_award WHERE lottery_id = $id;
            """;
        command.Parameters.AddWithValue("$id", lottery.Id);
        command.Parameters.AddWithValue("$room", lottery.RoomId);
        command.Parameters.AddWithValue("$mode", ModeText(mode));
        command.Set("$started", lottery.StartedAt);
        command.Set("$ended", lottery.EndedAt);
        command.Parameters.AddWithValue("$phase", phase);
        command.Set("$until", lottery.DrawUntil);
        command.Parameters.AddWithValue("$word", stopWord);
        command.Set("$uid", uid);
        command.Parameters.AddWithValue("$keyword", keyword);
        command.Parameters.AddWithValue("$hidden", hidden);
        command.ExecuteNonQuery();
        switch (lottery)
        {
            case PoolLottery pool:
                for (var index = 0; index < pool.Prizes.Length; index++)
                {
                    var prize = pool.Prizes[index];
                    command.Parameters.Clear();
                    command.CommandText = "INSERT INTO lottery_prize (lottery_id, position, name, weight, grade) VALUES ($id, $position, $name, $weight, $grade)";
                    command.Parameters.AddWithValue("$id", lottery.Id);
                    command.Parameters.AddWithValue("$position", index);
                    command.Parameters.AddWithValue("$name", prize.Name);
                    command.Parameters.AddWithValue("$weight", prize.Weight);
                    command.Parameters.AddWithValue("$grade", (int)prize.Grade);
                    command.ExecuteNonQuery();
                }
                break;
            case EntryLottery entry:
                for (var index = 0; index < entry.Awards.Length; index++)
                {
                    var award = entry.Awards[index];
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
                break;
        }
        if (draw is not null) AddDraw(command, lottery.Id, draw.Id, JsonSerializer.Serialize(draw, draw.GetType(), DrawJson));
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
        command.Set("$at", entry.EnteredAt);
        command.ExecuteNonQuery();
    }

    private static void EndCurrent(SqliteCommand command, long roomId, DateTimeOffset? endedAt)
    {
        if (endedAt is not { } ended) return;
        command.CommandText = "UPDATE lottery SET ended_at = $ended WHERE room_id = $room AND ended_at IS NULL";
        command.Set("$ended", ended);
        command.Parameters.AddWithValue("$room", roomId);
        command.ExecuteNonQuery();
        command.Parameters.Clear();
    }

    private static string ModeText(LotteryMode mode) => mode == LotteryMode.Entry ? "entry" : "pool";
    private static LotteryMode ReadMode(string text) => text == "entry" ? LotteryMode.Entry : LotteryMode.Pool;

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

    public void SetDrawHiddenFromStream(string lotteryId, string? drawId, bool hidden)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE lottery_draw SET hidden_from_stream = $hidden WHERE lottery_id = $lottery"
            + (drawId is null ? "" : " AND id = $id");
        command.Parameters.AddWithValue("$lottery", lotteryId);
        command.Parameters.AddWithValue("$hidden", hidden);
        if (drawId is not null) command.Parameters.AddWithValue("$id", drawId);
        if (command.ExecuteNonQuery() == 0 && drawId is not null) throw new ArgumentException("找不到这次开奖。");
    }

    private static ImmutableArray<T> ReadDraws<T>(SqliteCommand command, string lotteryId) where T : LotteryDraw
    {
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", lotteryId);
        command.CommandText = "SELECT result, hidden_from_stream FROM lottery_draw WHERE lottery_id = $id ORDER BY rowid";
        var draws = ImmutableArray.CreateBuilder<T>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) draws.Add(JsonSerializer.Deserialize<T>(reader.GetString(0))! with { HiddenFromStream = reader.GetBoolean(1) });
        return draws.ToImmutable();
    }
}
