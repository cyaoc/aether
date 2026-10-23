using Aether.Core;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryPrizeSetTests
{
    [Fact]
    public async Task Prize_sets_are_scoped_ordered_replaceable_and_survive_a_new_client()
    {
        await using var h = new WatchHarness(enableLottery: true);
        var lottery = h.Client.Lottery;
        lottery.SavePrizeSet(7734200, LotteryMode.Pool, " 周末 ", [new("抱枕", 2, Grade.Grand)], []);
        lottery.SavePrizeSet(7734200, LotteryMode.Entry, "周末", [], [new("幸运奖", "贴纸", 3, Grade.Third)]);
        lottery.SavePrizeSet(99, LotteryMode.Pool, "其他房间", [new("玩偶", 1, Grade.Second)], []);
        lottery.SavePrizeSet(7734200, LotteryMode.Pool, "日常", [new("贴纸", 1, Grade.Third)], []);
        Assert.Equal(new[] { "日常", "周末" }, lottery.GetPrizeSets(7734200, LotteryMode.Pool).Select(set => set.Name));
        h.Time.Advance(TimeSpan.FromMinutes(1));
        lottery.SavePrizeSet(7734200, LotteryMode.Pool, "周末", [new("键盘", 4, Grade.Grand), new("鼠标", 7, Grade.Second)], []);
        h.Client.Dispose();
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        var sets = restarted.Lottery.GetPrizeSets(7734200, LotteryMode.Pool);
        Assert.Equal(new[] { "周末", "日常" }, sets.Select(set => set.Name));
        Assert.Equal(new[] { "键盘", "鼠标" }, sets[0].Prizes.Select(prize => prize.Name));
        Assert.Equal(new[] { 4, 7 }, sets[0].Prizes.Select(prize => prize.Weight));
        Assert.Empty(sets[0].Awards);
        Assert.Equal(h.Time.GetUtcNow(), sets[0].SavedAt);
        Assert.Equal("幸运奖", Assert.Single(Assert.Single(restarted.Lottery.GetPrizeSets(7734200, LotteryMode.Entry)).Awards).Name);
        Assert.Equal("其他房间", Assert.Single(restarted.Lottery.GetPrizeSets(99, LotteryMode.Pool)).Name);
        restarted.Lottery.DeletePrizeSet(7734200, LotteryMode.Pool, "周末");
        Assert.Equal("日常", Assert.Single(restarted.Lottery.GetPrizeSets(7734200, LotteryMode.Pool)).Name);
        Assert.Single(restarted.Lottery.GetPrizeSets(7734200, LotteryMode.Entry));
        Assert.Null(restarted.Lottery.State.RoomId);
    }
    [Fact]
    public async Task Prize_set_validation_matches_starting_each_lottery()
    {
        await using var h = new WatchHarness(enableLottery: true);
        var lottery = h.Client.Lottery;
        PoolPrize[][] invalidPrizes = [[], Enumerable.Repeat(new PoolPrize("贴纸", 1, Grade.Third), 11).ToArray(),
            [new("", 1, Grade.Grand)], [new("贴纸", 0, Grade.Third)], [new("贴纸", 1, (Grade)99)]];
        foreach (var prizes in invalidPrizes)
            Assert.Equal(Assert.Throws<ArgumentException>(() => lottery.Start(prizes, "停")).Message,
                Assert.Throws<ArgumentException>(() => lottery.SavePrizeSet(7734200, LotteryMode.Pool, "周末", prizes, [])).Message);
        LotteryAward[][] invalidAwards = [[], Enumerable.Repeat(new LotteryAward("幸运奖", "贴纸", 1, Grade.Third), 6).ToArray(),
            [new("", "贴纸", 1, Grade.Third)], [new("幸运奖", "", 1, Grade.Third)],
            [new("幸运奖", "贴纸", 0, Grade.Third)], [new("幸运奖", "贴纸", 1, (Grade)99)]];
        foreach (var awards in invalidAwards)
            Assert.Equal(Assert.Throws<ArgumentException>(() => lottery.StartEntry(awards, "抽奖")).Message,
                Assert.Throws<ArgumentException>(() => lottery.SavePrizeSet(7734200, LotteryMode.Entry, "周末", [], awards)).Message);
        Assert.Equal("请给方案起个名字。", Assert.Throws<ArgumentException>(() =>
            lottery.SavePrizeSet(7734200, LotteryMode.Pool, "  ", [new("贴纸", 1, Grade.Third)], [])).Message);
        Assert.Empty(lottery.GetPrizeSets(7734200, LotteryMode.Pool));
        Assert.Empty(lottery.GetPrizeSets(7734200, LotteryMode.Entry));
    }
}
