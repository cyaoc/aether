using System.Collections.Immutable;

namespace Aether.Core;

/// <summary>A room's latest 期 as the 收礼记录 has it: its id, the 期 itself and its options, all null for a room that has
/// never had one; its totals, one per viewer and 礼物键, each with the 收礼记录 row of its first gift; the tids of its gifts;
/// and the last 收礼记录 row, which every gift counted from here on comes after.</summary>
internal sealed record GiftBoardSnapshot(long? PeriodId, GiftBoardPeriod? Period, GiftBoardOptions? Options,
    ImmutableArray<(GiftBoardRow Row, long First)> Totals, ImmutableArray<string> Tids, long LastRecord);

/// <summary>The 礼物榜's 期 (ADR 0014). Each 收礼记录 row names the 期 the board counted it in (ADR 0013), and a room's latest
/// 期 is the one that counts; it runs until it is ended.</summary>
internal sealed class GiftBoardStore(Database database)
{
    private const string LatestPeriodId = "(SELECT MAX(id) FROM gift_board_period WHERE room_id = $room)";

    /// <summary>The room's latest 期, read at one moment. A total's value and count add up exactly, and its nickname, avatar,
    /// gift name and icon are the latest sent.</summary>
    public GiftBoardSnapshot Load(long roomId)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(id), 0) FROM gift_record";
        var last = (long)command.ExecuteScalar()!;
        command.CommandText = $"""
            SELECT id, started_at, ended_at, threshold, threshold_by, mascot, volume FROM gift_board_period
            WHERE id = {LatestPeriodId};
            """;
        command.Parameters.AddWithValue("$room", roomId);
        long id;
        GiftBoardPeriod period;
        GiftBoardOptions options;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return new(null, null, null, [], [], last);
            id = reader.GetInt64(0);
            period = new(reader.GetDateTimeOffset(1), reader.IsDBNull(2) ? null : reader.GetDateTimeOffset(2));
            options = new(reader.GetInt32(3), reader.GetInt32(4), reader.GetBoolean(5), reader.GetInt32(6));
        }
        command.CommandText = """
            WITH totals AS (
                SELECT uid, gift_key, SUM(value) AS value, SUM(num) AS num, MIN(id) AS first, MAX(id) AS last FROM gift_record
                WHERE board_period = $period
                GROUP BY uid, gift_key
            )
            SELECT totals.uid, totals.gift_key, latest.nickname, latest.face_url, latest.gift_name, latest.icon_url,
                latest.guard_level, totals.value, totals.num, totals.first
            FROM totals JOIN gift_record AS latest ON latest.id = totals.last;
            """;
        command.Parameters.AddWithValue("$period", id);
        var totals = ImmutableArray.CreateBuilder<(GiftBoardRow, long)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                totals.Add((new($"{reader.GetInt64(0)}/{reader.GetString(1)}", reader.GetInt64(0), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt64(7),
                    reader.GetInt64(8)), reader.GetInt64(9)));
        command.CommandText = "SELECT tid FROM gift_record WHERE board_period = $period AND tid IS NOT NULL";
        var tids = ImmutableArray.CreateBuilder<string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) tids.Add(reader.GetString(0));
        return new(id, period, options, totals.ToImmutable(), tids.ToImmutable(), last);
    }

    /// <summary>Starts a new 期 for the room with these options and returns its id; the older ones stay as they are. The
    /// room must not have one running.</summary>
    public long Start(long roomId, DateTimeOffset at, GiftBoardOptions options) => (long)Execute("""
        INSERT INTO gift_board_period (room_id, started_at, threshold, threshold_by, mascot, volume)
        VALUES ($room, $at, $threshold, $by, $mascot, $volume)
        RETURNING id;
        """, roomId, at, options)!;

    /// <summary>Ends the room's running 期.</summary>
    public void End(long roomId, DateTimeOffset at) =>
        Execute($"UPDATE gift_board_period SET ended_at = $at WHERE id = {LatestPeriodId} AND ended_at IS NULL;", roomId, at);

    /// <summary>Runs the room's latest 期 again after it was ended.</summary>
    public void Continue(long roomId) => Execute($"UPDATE gift_board_period SET ended_at = NULL WHERE id = {LatestPeriodId};", roomId);

    /// <summary>Gives the room's latest 期 these options.</summary>
    public void SetOptions(long roomId, GiftBoardOptions options) => Execute($"""
        UPDATE gift_board_period SET threshold = $threshold, threshold_by = $by, mascot = $mascot, volume = $volume
        WHERE id = {LatestPeriodId};
        """, roomId, options: options);

    private object? Execute(string statement, long roomId, DateTimeOffset? at = null, GiftBoardOptions? options = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        command.Parameters.AddWithValue("$room", roomId);
        command.Set("$at", at);
        if (options is not null)
        {
            command.Parameters.AddWithValue("$threshold", options.Threshold);
            command.Parameters.AddWithValue("$by", options.ThresholdBy);
            command.Parameters.AddWithValue("$mascot", options.Mascot);
            command.Parameters.AddWithValue("$volume", options.Volume);
        }
        return command.ExecuteScalar();
    }
}
