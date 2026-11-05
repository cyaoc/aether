using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Aether.Core.Tests.Support;

internal static class TestDatabase
{
    public static SqliteConnection Open(string dataDirectory)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "aether.db"), Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>The one saved credential row; fails when there is none or more than one.</summary>
    public static (Dictionary<string, string> Cookies, string RefreshToken, DateTimeOffset SavedAt) SavedCredential(
        string dataDirectory)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cookies, refresh_token, saved_at FROM credential";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        var credential = (JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(0))!,
            reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture));
        Assert.False(reader.Read());
        return credential;
    }

    public static DateTimeOffset? CheckedAt(string dataDirectory)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT checked_at FROM credential";
        return command.ExecuteScalar() is string value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture) : null;
    }

    public sealed record BlindBoxRow(long RoomId, long Uid, string Nickname, long BlindGiftId, string BlindGiftName,
        long BlindGiftPrice, long OpenedGiftId, string OpenedGiftName, long OpenedGiftPrice, long Num, long Spend,
        long OpenedValue, long Timestamp, string? Tid);

    /// <summary>Every 盲盒 the 收礼记录 holds for the 盲盒统计, in insertion order.</summary>
    public static List<BlindBoxRow> BlindBoxRows(string dataDirectory) => ReadBlindBoxes(dataDirectory, """
        SELECT room_id, uid, nickname, blind_gift_id, blind_gift_name, blind_gift_price,
            gift_id, gift_name, value / num, num, spend, value, timestamp, tid
        FROM gift_record WHERE for_blind_box = 1 ORDER BY id;
        """);

    /// <summary>Every row of the blind_box table releases up to v0.3.1 kept, before an upgrade moves them, all columns but id.</summary>
    public static List<BlindBoxRow> LegacyBlindBoxRows(string dataDirectory) => ReadBlindBoxes(dataDirectory, """
        SELECT room_id, uid, nickname, blind_gift_id, blind_gift_name, blind_gift_price,
            opened_gift_id, opened_gift_name, opened_gift_price, num, spend, opened_value, timestamp, tid
        FROM blind_box ORDER BY id;
        """);

    private static List<BlindBoxRow> ReadBlindBoxes(string dataDirectory, string query)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = query;
        using var reader = command.ExecuteReader();
        List<BlindBoxRow> rows = [];
        while (reader.Read()) rows.Add(new(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4),
            reader.GetInt64(5), reader.GetInt64(6), reader.GetString(7), reader.GetInt64(8), reader.GetInt64(9),
            reader.GetInt64(10), reader.GetInt64(11), reader.GetInt64(12), reader.IsDBNull(13) ? null : reader.GetString(13)));
        return rows;
    }

    /// <summary>How many gifts the 收礼记录 holds, in every room.</summary>
    public static long GiftRecords(string dataDirectory) => RowCount(dataDirectory, "gift_record");

    /// <summary>How many 期 the 礼物榜 has begun, in every room.</summary>
    public static long GiftBoardPeriods(string dataDirectory) => RowCount(dataDirectory, "gift_board_period");

    /// <summary>Writes the database an older release left behind: runs a frozen sample from Fixtures on a new file.</summary>
    public static void Restore(string dataDirectory, string fixture)
    {
        Directory.CreateDirectory(dataDirectory);
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        command.ExecuteNonQuery();
    }

    public static long UserVersion(string dataDirectory)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return (long)command.ExecuteScalar()!;
    }

    public static void SetUserVersion(string dataDirectory, long version)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version}";
        command.ExecuteNonQuery();
    }

    public static List<string> Columns(string dataDirectory, string table)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($table)";
        command.Parameters.AddWithValue("$table", table);
        using var reader = command.ExecuteReader();
        List<string> names = [];
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    public static List<long> ViewerGuardLevels(string dataDirectory)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT viewer_guard_level FROM gift_record ORDER BY id";
        using var reader = command.ExecuteReader();
        List<long> levels = [];
        while (reader.Read()) levels.Add(reader.GetInt64(0));
        return levels;
    }

    public static long RowCount(string dataDirectory, string table)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    public static long CredentialCount(string dataDirectory) => RowCount(dataDirectory, "credential");
}
