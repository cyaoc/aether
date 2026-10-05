using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Aether.Core;

internal sealed class CredentialStore
{
    private readonly string connectionString;

    public CredentialStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "aether.db"), Pooling = false
        }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL";
        command.ExecuteNonQuery();
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        var version = (long)command.ExecuteScalar()!;
        if (version > 1) throw new InvalidOperationException("数据库版本高于当前程序支持的版本。");
        if (version == 0)
        {
            // Schema upgrades are handwritten and committed with user_version in the same transaction.
            command.CommandText = """
                CREATE TABLE credential (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    cookies TEXT NOT NULL,
                    refresh_token TEXT NOT NULL,
                    saved_at TEXT NOT NULL
                );
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void Save(Dictionary<string, string> cookies, string refreshToken, DateTimeOffset savedAt)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // ponytail: 登录凭据暂存明文；要分发给别人用时，改用系统钥匙串。
        command.CommandText = """
            INSERT INTO credential (id, cookies, refresh_token, saved_at) VALUES (1, $cookies, $token, $savedAt)
            ON CONFLICT (id) DO UPDATE SET
                cookies = excluded.cookies, refresh_token = excluded.refresh_token, saved_at = excluded.saved_at;
            """;
        command.Parameters.AddWithValue("$cookies", JsonSerializer.Serialize(cookies));
        command.Parameters.AddWithValue("$token", refreshToken);
        command.Parameters.AddWithValue("$savedAt", savedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Delete()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM credential";
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    internal static string GetDataDirectory()
    {
        var root = AppContext.BaseDirectory;
#if DEBUG
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Aether.slnx"))) continue;
            root = directory.FullName;
            break;
        }
#endif
        return Path.Combine(root, "data");
    }
}
