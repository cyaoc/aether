using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>Opens the frozen samples of what releases left behind: v0.2.1, and v0.2.2 to v0.3.1 (database version 5).</summary>
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

        Assert.Equal(6L, TestDatabase.UserVersion(h.DataDirectory));
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

        Assert.Equal(["legacy-entry", "legacy-pool"], lottery.GetHistory(Room).Select(history => history.Id));
        var entry = Assert.IsType<EntryLottery>(lottery.GetHistoryDetails(Room, "legacy-entry"));
        Assert.Equal([false, true], entry.Draws.Select(draw => draw.HiddenFromStream));
        Assert.Equal(["键盘", "鼠标"], Assert.Single(lottery.GetPrizeSets(Room, LotteryMode.Pool)).Prizes.Select(prize => prize.Name));
        Assert.Equal(["一等奖", "参与奖"], Assert.Single(lottery.GetPrizeSets(Room, LotteryMode.Entry)).Awards.Select(award => award.Name));
        Assert.Equal("legacy-token", TestDatabase.SavedCredential(h.DataDirectory).RefreshToken);
    }

    [Fact]
    public async Task Opening_the_upgraded_database_again_does_not_upgrade_it_again()
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, Sample);
        Assert.NotEmpty(h.Client.Lottery.GetHistory(Room));

        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);

        Assert.NotEmpty(restarted.Lottery.GetHistory(Room));
        Assert.Equal(6L, TestDatabase.UserVersion(h.DataDirectory));
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
