using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>Opens the frozen sample of what releases v0.2.2 to v0.3.1 left behind (database version 5).</summary>
public sealed class DatabaseUpgradeTests
{
    private const string Sample = "database-v0.3.1.sql";
    private const long Room = 7734200;

    [Fact]
    public async Task Version_five_database_loses_blind_box_raw_messages_and_keeps_their_values_and_tallies()
    {
        await using var h = new WatchHarness();
        TestDatabase.Restore(h.DataDirectory, Sample);
        var before = TestDatabase.BlindBoxRows(h.DataDirectory);

        var blindBoxes = new BlindBoxStore(new Database(h.DataDirectory), h.Logger);

        Assert.Equal(6L, TestDatabase.UserVersion(h.DataDirectory));
        Assert.DoesNotContain("raw_message", TestDatabase.Columns(h.DataDirectory, "blind_box"));
        Assert.Equal(5, before.Count);
        Assert.Equal(before, TestDatabase.BlindBoxRows(h.DataDirectory));
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
}
