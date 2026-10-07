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

    public static long CredentialCount(string dataDirectory)
    {
        using var connection = Open(dataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM credential";
        return (long)command.ExecuteScalar()!;
    }
}
