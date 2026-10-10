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

    /// <summary>Every blind_box row in insertion order, all columns but id.</summary>
    public static List<object[]> BlindBoxRows(string dataDirectory)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT room_id, uid, nickname, blind_gift_id, blind_gift_name, blind_gift_price,
                opened_gift_id, opened_gift_name, opened_gift_price, num, spend, opened_value, timestamp, tid, raw_message
            FROM blind_box ORDER BY id;
            """;
        using var reader = command.ExecuteReader();
        List<object[]> rows = [];
        while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue).ToArray());
        return rows;
    }

    public static long CredentialCount(string dataDirectory)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM credential";
        return (long)command.ExecuteScalar()!;
    }
}
