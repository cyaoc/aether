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
        if (version > 6) throw new InvalidOperationException("数据库版本高于当前程序支持的版本。");
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
        if (version < 4)
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS lottery (
                    id TEXT PRIMARY KEY, room_id INTEGER NOT NULL, mode TEXT NOT NULL,
                    stop_word TEXT NOT NULL, started_at TEXT NOT NULL, ended_at TEXT,
                    phase INTEGER NOT NULL, uid INTEGER, draw_until TEXT,
                    keyword TEXT NOT NULL DEFAULT '', hidden INTEGER NOT NULL DEFAULT 0
                );
                CREATE UNIQUE INDEX IF NOT EXISTS lottery_active_room ON lottery(room_id) WHERE ended_at IS NULL;
                CREATE TABLE IF NOT EXISTS lottery_prize (
                    lottery_id TEXT NOT NULL REFERENCES lottery(id), position INTEGER NOT NULL,
                    name TEXT NOT NULL, weight INTEGER NOT NULL CHECK(weight > 0), grade INTEGER NOT NULL,
                    PRIMARY KEY(lottery_id, position)
                );
                CREATE TABLE IF NOT EXISTS lottery_draw (
                    id TEXT PRIMARY KEY, lottery_id TEXT NOT NULL REFERENCES lottery(id), result TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS lottery_draw_lottery ON lottery_draw(lottery_id);
                CREATE TABLE IF NOT EXISTS lottery_award (
                    lottery_id TEXT NOT NULL REFERENCES lottery(id), position INTEGER NOT NULL,
                    name TEXT NOT NULL, prize TEXT NOT NULL, capacity INTEGER NOT NULL CHECK(capacity > 0), grade INTEGER NOT NULL,
                    PRIMARY KEY(lottery_id, position)
                );
                CREATE TABLE IF NOT EXISTS lottery_entry (
                    lottery_id TEXT NOT NULL REFERENCES lottery(id), uid INTEGER NOT NULL,
                    nickname TEXT NOT NULL, entered_at TEXT NOT NULL,
                    PRIMARY KEY(lottery_id, uid)
                );
                PRAGMA user_version = 4;
                """;
            command.ExecuteNonQuery();
        }
        if (version < 5)
        {
            command.CommandText = """
                ALTER TABLE lottery_draw ADD COLUMN hidden_from_stream INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE lottery_prize_set (
                    room_id INTEGER NOT NULL, mode TEXT NOT NULL, name TEXT NOT NULL,
                    saved_at TEXT NOT NULL, content TEXT NOT NULL,
                    UNIQUE(room_id, mode, name)
                );
                PRAGMA user_version = 5;
                """;
            command.ExecuteNonQuery();
        }
        if (version < 6)
        {
            // The 收礼记录 the 盲盒统计 and the 礼物榜 share (ADR 0013): one row per paid gift, flagged by which of the two wanted
            // it. value: Price × Num for a gift (a 盲盒's 开出价值), the 投入 for a 大航海; blind_gift_*: the 盲盒 it came out of,
            // null for any other gift; timestamp: B站's send time in Unix seconds, null for a 大航海; gift_key: the 礼物键,
            // gift:<gift id> or guard:<level>, so an opened gift and the same gift sent directly add up. The 盲盒统计 only flags
            // 盲盒. Every blind_box row moves over as a 盲盒 the 盲盒统计 recorded, and raw_message, which nothing ever read, goes
            // with the old table.
            // 期 (ADR 0014): ended_at is null while it runs, and a room runs one at most; after_record is the last 收礼记录 row
            // before it began, so a 期 is the board's rows after its own and up to the next one's; threshold (电池),
            // threshold_by, mascot and volume are its options.
            command.CommandText = """
                CREATE TABLE gift_record (
                    id INTEGER PRIMARY KEY, room_id INTEGER NOT NULL, uid INTEGER NOT NULL, nickname TEXT NOT NULL,
                    face_url TEXT NOT NULL, gift_id INTEGER NOT NULL, gift_name TEXT NOT NULL, icon_url TEXT NOT NULL,
                    guard_level INTEGER NOT NULL, gift_key TEXT NOT NULL, num INTEGER NOT NULL, spend INTEGER NOT NULL, value INTEGER NOT NULL,
                    blind_gift_id INTEGER, blind_gift_name TEXT, blind_gift_price INTEGER, tid TEXT UNIQUE, timestamp INTEGER,
                    for_blind_box INTEGER NOT NULL, for_board INTEGER NOT NULL
                );
                CREATE INDEX gift_record_blind_box ON gift_record (room_id, uid, timestamp) WHERE for_blind_box = 1;
                CREATE INDEX gift_record_board ON gift_record (room_id, for_board);
                INSERT INTO gift_record (room_id, uid, nickname, face_url, gift_id, gift_name, icon_url, guard_level, gift_key, num,
                    spend, value, blind_gift_id, blind_gift_name, blind_gift_price, tid, timestamp, for_blind_box, for_board)
                SELECT room_id, uid, nickname, '', opened_gift_id, opened_gift_name, '', 0, 'gift:' || opened_gift_id, num, spend,
                    opened_value, blind_gift_id, blind_gift_name, blind_gift_price, tid, timestamp, 1, 0
                FROM blind_box ORDER BY id;
                DROP TABLE blind_box;
                CREATE TABLE gift_board_period (
                    id INTEGER PRIMARY KEY, room_id INTEGER NOT NULL, started_at TEXT NOT NULL, ended_at TEXT,
                    after_record INTEGER NOT NULL, threshold INTEGER NOT NULL, threshold_by INTEGER NOT NULL,
                    mascot INTEGER NOT NULL, volume INTEGER NOT NULL
                );
                CREATE INDEX gift_board_period_room ON gift_board_period (room_id);
                CREATE UNIQUE INDEX gift_board_period_running ON gift_board_period (room_id) WHERE ended_at IS NULL;
                PRAGMA user_version = 6;
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
