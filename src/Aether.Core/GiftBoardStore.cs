using System.Collections.Immutable;
using Microsoft.Data.Sqlite;

namespace Aether.Core;

/// <summary>The 礼物榜's totals, one row per viewer and kind of gift in each 期. A room's latest 期 is the one that counts.</summary>
internal sealed class GiftBoardStore(Database database)
{
    private const string OpenPeriod = "INSERT INTO gift_board_period (room_id, started_at, live_time) SELECT $room, $at, $live";

    // A price is the total spent over the total count, so a 大航海 renewed at another price still adds up to what was paid.
    private const string Upsert = """
        INSERT INTO gift_board_total (period_id, uid, gift_key, nickname, face_url, gift_name, icon_url, guard_level,
            price, num, first_at, last_at)
        VALUES ($period, $uid, $key, $nickname, $face, $giftName, $icon, $guard, $price, $num, $at, $at)
        ON CONFLICT (period_id, uid, gift_key) DO UPDATE SET
            nickname = excluded.nickname, face_url = excluded.face_url, gift_name = excluded.gift_name,
            icon_url = excluded.icon_url, price = (price * num + excluded.price * excluded.num) / (num + excluded.num),
            num = num + excluded.num, last_at = excluded.last_at;
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

    /// <summary>Adds the gifts to the room's latest 期, opening the first one when the room has none.</summary>
    public void Add(long roomId, IEnumerable<Gift> gifts, DateTimeOffset at)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var period = LatestPeriod(command, roomId, at);
        foreach (var gift in gifts) Accumulate(command, period, gift, at);
        transaction.Commit();
    }

    /// <summary>Opens a new 期 for the room, leaving the older ones as they are. <paramref name="liveTime"/> is when the
    /// 一场直播 under way began, null off air.</summary>
    public void Open(long roomId, DateTimeOffset at, long? liveTime)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = OpenPeriod;
        BindPeriod(command, roomId, at, liveTime);
        command.ExecuteNonQuery();
    }

    /// <summary>Opens a new 期 unless the room's latest one already belongs to the 一场直播 that began at <paramref name="liveTime"/>.</summary>
    public void OpenForLive(long roomId, DateTimeOffset at, long liveTime)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        // IS NOT, so that a 期 opened off air (null) differs from every live_time, and a room without a 期 gets its first.
        command.CommandText = OpenPeriod + """
             WHERE $live IS NOT (SELECT live_time FROM gift_board_period
                WHERE id = (SELECT MAX(id) FROM gift_board_period WHERE room_id = $room))
            """;
        BindPeriod(command, roomId, at, liveTime);
        command.ExecuteNonQuery();
    }

    private static void BindPeriod(SqliteCommand command, long roomId, DateTimeOffset at, long? liveTime)
    {
        command.Parameters.AddWithValue("$room", roomId);
        command.Set("$at", at);
        command.Set("$live", liveTime);
    }

    private static long LatestPeriod(SqliteCommand command, long roomId, DateTimeOffset at)
    {
        command.CommandText = "SELECT MAX(id) FROM gift_board_period WHERE room_id = $room";
        command.Parameters.AddWithValue("$room", roomId);
        if (command.ExecuteScalar() is long period) return period;
        command.CommandText = "INSERT INTO gift_board_period (room_id, started_at) VALUES ($room, $at) RETURNING id";
        command.Set("$at", at);
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
