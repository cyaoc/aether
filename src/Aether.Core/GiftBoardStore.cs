using System.Collections.Immutable;

namespace Aether.Core;

/// <summary>A room's latest 期 as the 收礼记录 has it: the 期 itself and its options, null for a room that has never had one, and
/// its totals, one per viewer and 礼物键, each with the 收礼记录 row of its first gift. Through is the last 收礼记录 row read:
/// anything up to it is in the totals or belongs to an earlier 期.</summary>
internal sealed record GiftBoardSnapshot(GiftBoardPeriod? Period, GiftBoardOptions? Options,
    ImmutableArray<(GiftBoardRow Row, long First)> Totals, long Through);

/// <summary>The 礼物榜's 期 (ADR 0014), each a boundary in the 收礼记录 (ADR 0013): the board's rows after it and up to the next
/// one. A room's latest 期 is the one that counts; it runs until it is ended.</summary>
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
        command.CommandText = $"""
            SELECT started_at, ended_at, after_record, threshold, threshold_by, mascot, volume FROM gift_board_period
            WHERE id = {LatestPeriodId};
            """;
        command.Parameters.AddWithValue("$room", roomId);
        GiftBoardPeriod? period = null;
        GiftBoardOptions? options = null;
        long after = 0;
        using (var reader = command.ExecuteReader())
            if (reader.Read())
            {
                period = new(reader.GetDateTimeOffset(0), reader.IsDBNull(1) ? null : reader.GetDateTimeOffset(1));
                after = reader.GetInt64(2);
                options = new(reader.GetInt32(3), reader.GetInt32(4), reader.GetBoolean(5), reader.GetInt32(6));
            }
        command.CommandText = "SELECT COALESCE(MAX(id), 0) FROM gift_record";
        var through = (long)command.ExecuteScalar()!;
        if (period is null) return new(null, null, [], through);
        command.CommandText = """
            WITH totals AS (
                SELECT uid, gift_key, SUM(value) AS value, SUM(num) AS num, MIN(id) AS first, MAX(id) AS last FROM gift_record
                WHERE room_id = $room AND for_board = 1 AND id > $after AND id <= $through
                GROUP BY uid, gift_key
            )
            SELECT totals.uid, totals.gift_key, latest.nickname, latest.face_url, latest.gift_name, latest.icon_url,
                latest.guard_level, totals.value, totals.num, totals.first
            FROM totals JOIN gift_record AS latest ON latest.id = totals.last;
            """;
        command.Parameters.AddWithValue("$after", after);
        command.Parameters.AddWithValue("$through", through);
        var totals = ImmutableArray.CreateBuilder<(GiftBoardRow, long)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                totals.Add((new($"{reader.GetInt64(0)}/{reader.GetString(1)}", reader.GetInt64(0), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt64(7),
                    reader.GetInt64(8)), reader.GetInt64(9)));
        return new(period, options, totals.ToImmutable(), through);
    }

    /// <summary>Starts a new 期 for the room with these options; the older ones stay as they are. The room must not have one running.</summary>
    public void Start(long roomId, DateTimeOffset at, GiftBoardOptions options) => Execute("""
        INSERT INTO gift_board_period (room_id, started_at, after_record, threshold, threshold_by, mascot, volume)
        SELECT $room, $at, COALESCE((SELECT MAX(id) FROM gift_record), 0), $threshold, $by, $mascot, $volume;
        """, roomId, at, options);

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

    private void Execute(string statement, long roomId, DateTimeOffset? at = null, GiftBoardOptions? options = null)
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
        command.ExecuteNonQuery();
    }
}
