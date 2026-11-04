using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>The thresholds are in 电池 (1 电池 = 100 金瓜子), so 10 电池 is 1000 and the prices below are in 金瓜子.</summary>
public sealed class GiftBoardRankingTests
{
    [Fact]
    public async Task By_unit_price_a_gift_must_be_worth_the_threshold_on_its_own()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync(threshold: 10);

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(giftId: 1, giftName: "刚好", price: 1000, tid: "a"),
            BlindGiftFixture.Ordinary(giftId: 2, giftName: "差一点", price: 999, num: 50, tid: "b"));

        Assert.Equal(["刚好"], (await h.BoardRowsAsync()).Select(row => row.GiftName));
    }

    [Fact]
    public async Task By_total_price_gifts_of_one_kind_add_up_to_the_threshold()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync(threshold: 10, thresholdBy: Settings.GiftBoardByTotalPrice);

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(giftId: 1, giftName: "三个", price: 300, num: 3, tid: "a"),
            BlindGiftFixture.Ordinary(giftId: 2, giftName: "刚好", price: 500, num: 2, tid: "b"));
        Assert.Equal(["刚好"], (await h.BoardRowsAsync()).Select(row => row.GiftName));

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(giftId: 1, giftName: "三个", price: 300, tid: "c"));
        Assert.Equal(["三个", "刚好"], (await h.BoardRowsAsync()).Select(row => row.GiftName));
    }

    [Fact]
    public async Task Changing_the_threshold_or_what_it_compares_takes_effect_at_once()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync(threshold: 10);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(price: 300, num: 4));
        Assert.Empty(await h.BoardRowsAsync());

        await h.ApplySettingsAsync(("gift_board.threshold_by", Settings.GiftBoardByTotalPrice.ToString()));
        Assert.Single(await h.BoardRowsAsync());

        await h.ApplySettingsAsync(("gift_board.threshold", "13"));
        Assert.Empty(await h.BoardRowsAsync());
    }

    [Fact]
    public async Task Rows_rank_by_value_and_equal_values_by_who_was_sent_first()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        // Viewers 1 to 4 all end at 2000; viewer 1 is the first sent but reaches it after viewer 2 does.
        foreach (var (uid, price) in new[] { (1, 1000), (2, 2000), (1, 1000), (3, 1000), (3, 1000), (4, 2000) })
        {
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: uid, price: price, tid: null));
            h.Time.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Equal(["1/gift:32698", "2/gift:32698", "3/gift:32698", "4/gift:32698"], (await h.BoardRowsAsync()).Select(row => row.Id));

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 5, giftId: 5, price: 5000, tid: null));
        Assert.Equal("5/gift:5", (await h.BoardRowsAsync())[0].Id);
    }
}
