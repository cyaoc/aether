using Microsoft.Data.Sqlite;

namespace Aether.Core;

/// <summary><c>aether.db</c> in the data directory: WAL journal, schema version in <c>PRAGMA user_version</c>.</summary>
internal sealed class Database
{
    private readonly string connectionString;

    public Database(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "aether.db");
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL";
        command.ExecuteNonQuery();
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        var version = (long)command.ExecuteScalar()!;
        if (version > 3) throw new InvalidOperationException("数据库版本高于当前程序支持的版本。");
        if (version == 0)
        {
            // Schema upgrades are handwritten and committed with user_version in the same transaction.
            command.CommandText = """
                CREATE TABLE credential (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    cookies TEXT NOT NULL,
                    refresh_token TEXT NOT NULL,
                    saved_at TEXT NOT NULL,
                    checked_at TEXT
                );
                PRAGMA user_version = 2;
                """;
            command.ExecuteNonQuery();
        }
        if (version == 1)
        {
            command.CommandText = "ALTER TABLE credential ADD COLUMN checked_at TEXT; PRAGMA user_version = 2;";
            command.ExecuteNonQuery();
        }
        if (version < 3)
        {
            command.CommandText = """
                CREATE TABLE blind_box (
                    id INTEGER PRIMARY KEY,
                    room_id INTEGER NOT NULL,
                    uid INTEGER NOT NULL,
                    nickname TEXT NOT NULL,
                    blind_gift_id INTEGER NOT NULL,
                    blind_gift_name TEXT NOT NULL,
                    blind_gift_price INTEGER NOT NULL,
                    opened_gift_id INTEGER NOT NULL,
                    opened_gift_name TEXT NOT NULL,
                    opened_gift_price INTEGER NOT NULL,
                    num INTEGER NOT NULL,
                    spend INTEGER NOT NULL,
                    opened_value INTEGER NOT NULL,
                    timestamp INTEGER NOT NULL,
                    tid TEXT UNIQUE,
                    raw_message TEXT NOT NULL
                );
                CREATE INDEX blind_box_room_viewer_time ON blind_box (room_id, uid, timestamp);
                PRAGMA user_version = 3;
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        // Owner-only, since credentials are plaintext; SQLite creates later -wal/-shm files with the database's mode.
        // ponytail: Windows relies on the data directory's inherited ACLs; revisit with the keychain move.
        if (!OperatingSystem.IsWindows())
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            // Zero freed pages so a deleted or replaced credential leaves no plaintext behind.
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA secure_delete = ON";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
}
