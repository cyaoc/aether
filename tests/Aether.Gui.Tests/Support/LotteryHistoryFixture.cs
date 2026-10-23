using System.Text.Json;
using Aether.Core;
using Microsoft.Data.Sqlite;

namespace Aether.Gui.Tests.Support;

internal static class LotteryHistoryFixture
{
    internal static readonly DateTimeOffset At = new(2026, 10, 16, 20, 30, 0, TimeSpan.FromHours(8));
    internal static EntryLottery Current => EntryLotteryPanelTests.TestLottery(EntryLotteryPhase.Closed) with
    {
        StartedAt = At,
        Entries = [new(1, "番茄炒蛋", At), new(2, "青柠", At)],
        Draws =
        [
            new("entry-1", 2, EntryLotteryPanelTests.TestLottery().Awards[2], new(1, "番茄炒蛋", At), At, []),
            new("entry-2", 2, EntryLotteryPanelTests.TestLottery().Awards[2], new(2, "青柠", At), At, []),
        ],
    };

    internal static async Task SeedAsync(GuiHarness h)
    {
        await h.SaveCredentialAsync();
        var settings = h.LoadSettings();
        settings.LastLotteryRoomId = 7734200;
        settings.Save();
        using var connection = new SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Execute(string sql, params (string Key, object? Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        var entry = Current;
        Execute("INSERT INTO lottery (id, room_id, mode, stop_word, keyword, started_at, phase) VALUES ('entry', 7734200, 'entry', '', '抽奖', $at, 2)",
            ("$at", At.ToString("O")));
        for (var index = 0; index < entry.Awards.Length; index++)
        {
            var award = entry.Awards[index];
            Execute("INSERT INTO lottery_award VALUES ('entry', $index, $name, $prize, $capacity, $grade)",
                ("$index", index), ("$name", award.Name), ("$prize", award.Prize), ("$capacity", award.Capacity), ("$grade", (int)award.Grade));
        }
        for (var index = 0; index < 18; index++)
            Execute("INSERT INTO lottery_entry VALUES ('entry', $uid, $name, $at)",
                ("$uid", index + 1), ("$name", index < 2 ? entry.Entries[index].Nickname : $"观众{index + 1}"), ("$at", At.ToString("O")));
        foreach (var draw in entry.Draws)
            Execute("INSERT INTO lottery_draw (id, lottery_id, result) VALUES ($id, 'entry', $result)", ("$id", draw.Id), ("$result", JsonSerializer.Serialize(draw)));
        Execute("INSERT INTO lottery (id, room_id, mode, stop_word, started_at, ended_at, phase) VALUES ('pool', 7734200, 'pool', '停', $at, $ended, 0)",
            ("$at", At.AddDays(-2).ToString("O")), ("$ended", At.ToString("O")));
        PoolPrize[] prizes = [new("机械键盘", 1, Grade.Grand), new("周边抱枕", 3, Grade.Second), new("签名照", 4, Grade.Third)];
        for (var index = 0; index < prizes.Length; index++)
            Execute("INSERT INTO lottery_prize VALUES ('pool', $index, $name, $weight, $grade)",
                ("$index", index), ("$name", prizes[index].Name), ("$weight", prizes[index].Weight), ("$grade", (int)prizes[index].Grade));
        string?[] names = ["柚子不酸", "阿白", "小满", null, "晚风", null, null];
        long?[] uids = [42, 7, 8, 9, 9, 88, null];
        for (var index = 0; index < names.Length; index++)
        {
            var prize = prizes[index == 0 ? 0 : index < 3 ? 1 : 2];
            var draw = new PoolDraw($"pool-{index}", prize, uids[index], names[index], true, At.AddDays(-2).AddMinutes(index), []);
            Execute("INSERT INTO lottery_draw (id, lottery_id, result) VALUES ($id, 'pool', $result)", ("$id", draw.Id), ("$result", JsonSerializer.Serialize(draw)));
        }
        // Other rooms must not leak into the current room's count or selected details.
        Execute("INSERT INTO lottery (id, room_id, mode, stop_word, started_at, phase) VALUES ('other', 999, 'pool', '开', $at, 0)",
            ("$at", At.AddDays(1).ToString("O")));
        transaction.Commit();
    }
}
