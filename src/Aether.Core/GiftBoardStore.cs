using System.Collections.Immutable;
using Microsoft.Data.Sqlite;

namespace Aether.Core;

/// <summary>The 礼物榜's totals, one row per viewer and kind of gift in each 期. A room's latest 期 is the one that counts.</summary>
internal sealed class GiftBoardStore(Database database)
{
    private const string InsertPeriod = "INSERT INTO gift_board_period (room_id, started_at, live_time) SELECT $room, $at, $live";

    // IS NOT, so that a 期 opened off air (live_time null) differs from every show, and a room without a 期 gets its first.
    private const string UnlessLatestIsThisShow = """
         WHERE $live IS NOT (SELECT live_time FROM gift_board_period
            WHERE id = (SELECT MAX(id) FROM gift_board_period WHERE room_id = $room))
        """;

    private const string InsertTotal = """
        INSERT INTO gift_board_total (period_id, uid, gift_key, nickname, face_url, gift_name, icon_url, guard_level,
            price, num, first_at, last_at)
        """;

    // Adding to a row that is already there, whether one gift or a whole 期 merged in. A price is the total spent over the
    // total count, so a 大航海 renewed at another price still adds up to what was paid; the later nickname, avatar and
    // icon win, and the row spans from the earlier first_at to the later last_at.
    private const string AddToRow = """
        ON CONFLICT (period_id, uid, gift_key) DO UPDATE SET
            nickname = excluded.nickname, face_url = excluded.face_url, gift_name = excluded.gift_name,
            icon_url = excluded.icon_url, price = (price * num + excluded.price * excluded.num) / (num + excluded.num),
            num = num + excluded.num, first_at = MIN(first_at, excluded.first_at), last_at = MAX(last_at, excluded.last_at)
        """;

    private const string Upsert = $"""
        {InsertTotal}
        VALUES ($period, $uid, $key, $nickname, $face, $giftName, $icon, $guard, $price, $num, $at, $at)
        {AddToRow};
        """;

    // The 期 merged in is the later one, so its nickname, avatar and icon are the latest.
    private const string MergeLatest = $"""
        {InsertTotal}
        SELECT $previous, uid, gift_key, nickname, face_url, gift_name, icon_url, guard_level, price, num, first_at, last_at
        FROM gift_board_total WHERE period_id = $current
        {AddToRow};
        DELETE FROM gift_board_total WHERE period_id = $current;
        DELETE FROM gift_board_period WHERE id = $current;
        UPDATE gift_board_period SET live_time = COALESCE($live, live_time) WHERE id = $previous;
        """;

    /// <summary>The room's latest 期 from the highest 价值 down, equal values by who was sent first.</summary>
    public ImmutableArray<GiftBoardRow> Rows(long roomId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT uid, gift_key, nickname, face_url, gift_name, icon_url, guard_level, price, num FROM gift_board_total
            WHERE period_id = (SELECT MAX(id) FROM gift_board_period WHERE room_id = $room)
            ORDER BY price * num DESC, first_at, rowid;
            """;
        command.Parameters.AddWithValue("$room", roomId);
        var rows = ImmutableArray.CreateBuilder<GiftBoardRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new($"{reader.GetInt64(0)}/{reader.GetString(1)}", reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt64(7), reader.GetInt64(8)));
        return rows.ToImmutable();
    }

    /// <summary>The room's latest 期 and the one before it, if any, newest first.</summary>
    public ImmutableArray<GiftBoardPeriod> Periods(long roomId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        return [.. LatestPeriods(command, roomId).Select(period => period.Period)];
    }

    /// <summary>Adds the gifts to the room's latest 期, opening the first one when the room has none. A 期 opened here
    /// belongs to the 一场直播 that began at <paramref name="liveTime"/>, if any.</summary>
    public void Add(long roomId, IEnumerable<Gift> gifts, DateTimeOffset at, long? liveTime)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var period = LatestPeriod(command, roomId, at, liveTime);
        foreach (var gift in gifts) Accumulate(command, period, gift, at);
        transaction.Commit();
    }

    /// <summary>Opens a new 期 for the room, leaving the older ones as they are. <paramref name="liveTime"/> is when the
    /// 一场直播 under way began, null off air.</summary>
    public void Open(long roomId, DateTimeOffset at, long? liveTime) => Insert(InsertPeriod, roomId, at, liveTime);

    /// <summary>Opens a new 期 unless the room's latest one already belongs to the 一场直播 that began at <paramref name="liveTime"/>.</summary>
    public void OpenForLive(long roomId, DateTimeOffset at, long liveTime) => Insert(InsertPeriod + UnlessLatestIsThisShow, roomId, at, liveTime);

    /// <summary>Merges the room's latest 期 into the one before it, which becomes the latest, in one transaction. Returns false,
    /// changing nothing, when there is no earlier 期. <paramref name="liveTime"/> is the 一场直播 under way, if any: the
    /// restored 期 belongs to it from now on.</summary>
    public bool Restore(long roomId, long? liveTime)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (LatestPeriods(command, roomId) is not [var current, var previous]) return false;
        command.CommandText = MergeLatest;
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$current", current.Id);
        command.Parameters.AddWithValue("$previous", previous.Id);
        command.Set("$live", liveTime);
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    private static ImmutableArray<(long Id, GiftBoardPeriod Period)> LatestPeriods(SqliteCommand command, long roomId)
    {
        command.CommandText = """
            SELECT id, started_at, (SELECT COUNT(DISTINCT uid) FROM gift_board_total WHERE period_id = gift_board_period.id)
            FROM gift_board_period WHERE room_id = $room ORDER BY id DESC LIMIT 2;
            """;
        command.Parameters.AddWithValue("$room", roomId);
        var periods = ImmutableArray.CreateBuilder<(long, GiftBoardPeriod)>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) periods.Add((reader.GetInt64(0), new(reader.GetDateTimeOffset(1), reader.GetInt32(2))));
        return periods.ToImmutable();
    }

    private void Insert(string statement, long roomId, DateTimeOffset at, long? liveTime)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        BindPeriod(command, roomId, at, liveTime);
        command.ExecuteNonQuery();
    }

    private static void BindPeriod(SqliteCommand command, long roomId, DateTimeOffset at, long? liveTime)
    {
        command.Parameters.AddWithValue("$room", roomId);
        command.Set("$at", at);
        command.Set("$live", liveTime);
    }

    private static long LatestPeriod(SqliteCommand command, long roomId, DateTimeOffset at, long? liveTime)
    {
        command.CommandText = "SELECT MAX(id) FROM gift_board_period WHERE room_id = $room";
        command.Parameters.AddWithValue("$room", roomId);
        if (command.ExecuteScalar() is long period) return period;
        command.CommandText = InsertPeriod + " RETURNING id";
        command.Parameters.Clear();
        BindPeriod(command, roomId, at, liveTime);
        return (long)command.ExecuteScalar()!;
    }

    private static void Accumulate(SqliteCommand command, long period, Gift gift, DateTimeOffset at)
    {
        command.CommandText = Upsert;
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$period", period);
        command.Parameters.AddWithValue("$uid", gift.Uid);
        command.Parameters.AddWithValue("$key", gift.GuardLevel > 0 ? $"guard:{gift.GuardLevel}" : $"gift:{gift.GiftId}");
        command.Parameters.AddWithValue("$nickname", gift.Nickname);
        command.Parameters.AddWithValue("$face", gift.FaceUrl);
        command.Parameters.AddWithValue("$giftName", gift.GiftName);
        command.Parameters.AddWithValue("$icon", gift.IconUrl);
        command.Parameters.AddWithValue("$guard", gift.GuardLevel);
        command.Parameters.AddWithValue("$price", gift.Price);
        command.Parameters.AddWithValue("$num", gift.Num);
        command.Set("$at", at);
        command.ExecuteNonQuery();
    }
}
