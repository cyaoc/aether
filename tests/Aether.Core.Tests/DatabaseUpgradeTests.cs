using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>Opens the frozen samples of what releases left behind: v0.2.1, v0.2.2 to v0.3.1 (database version 5), v0.4.0
/// (version 6) and v0.5.1 (version 7).</summary>
public sealed class DatabaseUpgradeTests
{
    private const string Sample = "database-v0.3.1.sql";
    private const long Room = 7734200;

    [Theory]
    [InlineData("database-v0.2.1.sql", 1)]
    [InlineData(Sample, 5)]
    public async Task An_older_database_moves_every_blind_box_into_the_gift_record_and_drops_the_old_table(string sample, int boxes)
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, sample);
        var before = TestDatabase.LegacyBlindBoxRows(h.DataDirectory);

        _ = new Database(h.DataDirectory);

        Assert.Equal(Database.CurrentVersion, TestDatabase.UserVersion(h.DataDirectory));
        Assert.Equal(boxes, before.Count);
        Assert.Equal(before, TestDatabase.BlindBoxRows(h.DataDirectory));
        // Every row is one of them: none is on the board, and the board has no 期 yet.
        Assert.Equal(boxes, TestDatabase.RowCount(h.DataDirectory, "gift_record"));
        Assert.Equal(0L, TestDatabase.RowCount(h.DataDirectory, "gift_board_period"));
        Assert.Empty(TestDatabase.Columns(h.DataDirectory, "blind_box"));
    }

    [Fact]
    public async Task Version_five_database_keeps_its_blind_box_tallies()
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, Sample);

        var blindBoxes = new BlindBoxStore(new Database(h.DataDirectory));

        // Viewer 42 sent one box on 31 October, one on 1 November and two on 2 November, Beijing time.
        var at = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.FromHours(8));
        Assert.Equal(new BlindBoxTally(3000, 4500), blindBoxes.Tally(Room, 42, at, BlindBoxPeriod.Daily));
        Assert.Equal(new BlindBoxTally(6000, 6000), blindBoxes.Tally(Room, 42, at, BlindBoxPeriod.Monthly));
    }

    [Fact]
    public async Task Version_five_database_keeps_lottery_history_prize_sets_and_credential()
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, Sample);
        var lottery = h.Client.Lottery;

        Assert.Equal(["legacy-entry", "legacy-pool"], (await lottery.GetHistoryAsync(Room)).Select(history => history.Id));
        var entry = Assert.IsType<EntryLottery>(await lottery.GetHistoryDetailsAsync(Room, "legacy-entry"));
        Assert.Equal([false, true], entry.Draws.Select(draw => draw.HiddenFromStream));
        Assert.Equal(["键盘", "鼠标"], Assert.Single(await lottery.PrizeSetsAsync(LotteryMode.Pool)).Prizes.Select(prize => prize.Name));
        Assert.Equal(["一等奖", "参与奖"], Assert.Single(await lottery.PrizeSetsAsync(LotteryMode.Entry)).Awards.Select(award => award.Name));
        Assert.Equal("legacy-token", TestDatabase.SavedCredential(h.DataDirectory).RefreshToken);
    }

    [Fact]
    public async Task Opening_the_upgraded_database_again_does_not_upgrade_it_again()
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, Sample);
        Assert.NotEmpty(await h.Client.Lottery.GetHistoryAsync(Room));

        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);

        Assert.NotEmpty(await restarted.Lottery.GetHistoryAsync(Room));
        Assert.Equal(Database.CurrentVersion, TestDatabase.UserVersion(h.DataDirectory));
    }

    [Fact]
    public async Task Version_six_database_gives_a_guard_purchase_the_level_bought_as_its_viewers_guard_and_any_other_gift_none()
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, "database-v0.4.0.sql");

        _ = new Database(h.DataDirectory);

        Assert.Equal(Database.CurrentVersion, TestDatabase.UserVersion(h.DataDirectory));
        Assert.Equal([0L, 3L, 0L], TestDatabase.ViewerGuardLevels(h.DataDirectory));
    }

    [Fact]
    public async Task Version_seven_database_keeps_every_prize_set_for_all_rooms_naming_the_room_of_an_older_namesake()
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, "database-v0.5.1.sql");
        var lottery = h.Client.Lottery;

        var pool = await lottery.PrizeSetsAsync(LotteryMode.Pool);

        Assert.Equal(["周末", "周末（直播间 7734200）", "日常"], pool.Select(set => set.Name));
        Assert.Equal(["键盘", "抱枕", "贴纸"], pool.Select(set => set.Prizes.Single().Name));
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero), pool[1].SavedAt);
        Assert.Equal("日常", Assert.Single(await lottery.PrizeSetsAsync(LotteryMode.Entry)).Name);
        Assert.Empty(TestDatabase.Columns(h.DataDirectory, "lottery_prize_set").Intersect(["room_id"]));
        Assert.Equal(Database.CurrentVersion, TestDatabase.UserVersion(h.DataDirectory));
        // Everything else stays as it was.
        Assert.Equal(["legacy-pool"], (await lottery.GetHistoryAsync(Room)).Select(history => history.Id));
        Assert.Equal(3L, TestDatabase.GiftRecords(h.DataDirectory));
        Assert.Equal([0L, 3L, 0L], TestDatabase.ViewerGuardLevels(h.DataDirectory));
        Assert.Equal("legacy-token", TestDatabase.SavedCredential(h.DataDirectory).RefreshToken);
    }

    [Theory]
    [InlineData(1, "周末（直播间 7734200，2）")]
    [InlineData(2, "周末（直播间 7734200，3）")]
    public async Task A_renamed_set_takes_the_next_free_name_and_sets_already_named_so_keep_theirs(int taken, string renamed)
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, "database-v0.5.1.sql");
        string[] names = ["周末（直播间 7734200）", "周末（直播间 7734200，2）"];
        using (var connection = TestDatabase.Open(h.DataDirectory))
            foreach (var (name, index) in names.Take(taken).Select((name, index) => (name, index)))
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO lottery_prize_set VALUES (42, 'pool', $name, $saved, '[{"Name":"徽章","Weight":1,"Grade":2}]');
                    """;
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$saved", $"2026-10-0{index + 1}T12:00:00.0000000+00:00");
                command.ExecuteNonQuery();
            }

        var pool = await h.Client.Lottery.PrizeSetsAsync(LotteryMode.Pool);

        Assert.Equal(["周末", renamed, "日常", .. names.Take(taken).Reverse()], pool.Select(set => set.Name));
        Assert.Equal(["键盘", "抱枕", "贴纸", .. Enumerable.Repeat("徽章", taken)], pool.Select(set => set.Prizes.Single().Name));
    }

    [Fact]
    public async Task Version_seven_database_starts_the_gui_state_at_its_defaults()
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, "database-v0.5.1.sql");

        await using var updates = h.Client.GuiState.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        var state = await h.NextAsync(updates);

        Assert.True(state.KeepRecentDanmaku);
        Assert.Empty(state.RecentRooms);
    }

    [Fact]
    public async Task A_new_database_opens_again_at_the_version_it_was_made_with()
    {
        await using var h = new WatchHarness();
        _ = new Database(h.DataDirectory);
        var made = TestDatabase.UserVersion(h.DataDirectory);

        _ = new Database(h.DataDirectory);

        Assert.Equal(made, TestDatabase.UserVersion(h.DataDirectory));
    }

    [Fact]
    public async Task A_database_from_a_newer_release_is_refused()
    {
        await using var h = new WatchHarness();
        _ = new Database(h.DataDirectory);
        TestDatabase.SetUserVersion(h.DataDirectory, Database.CurrentVersion + 1);

        var error = Assert.Throws<InvalidOperationException>(() => new Database(h.DataDirectory));

        Assert.Equal("数据库版本高于当前程序支持的版本。", error.Message);
    }
}
