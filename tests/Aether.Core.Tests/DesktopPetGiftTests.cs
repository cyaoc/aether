using System.Text.Json;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.BlindGiftFixture;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class DesktopPetGiftTests
{
    [Fact]
    public async Task Each_gift_message_tells_the_round_so_far_without_its_guard_and_the_gift_animations_round_ends_it()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        async Task SendAsync(string message, GiftRoundProgress round, GiftItem item)
        {
            await h.PushAndSyncAsync(updates, message);
            await AssertRoundAsync(pet, round, item);
        }

        await SendAsync(Ordinary(giftId: 31036, giftName: "小花花", price: 100, num: 1, tid: "t1"), Gifts(100, 1, "小花花", 1), Item(100, 1));
        await SendAsync(Ordinary(giftId: 31036, giftName: "小花花", price: 100, num: 4, tid: "t2"), Gifts(500, 5, "小花花", 5), Item(400, 4));
        await SendAsync(Ordinary(giftId: 31036, giftName: "小花花", price: 100, num: 5, tid: "t3"), Gifts(1000, 10, "小花花", 10), Item(500, 5));
        await h.PushAndSyncAsync(updates, UserToastFixture.Message());
        var guard = await pet.NextAsync(DesktopPet.Guard);
        Assert.Equal((10001L, "测试观众", 3), (guard.GetProperty("uid").GetInt64(), guard.GetProperty("nickname").GetString(),
            guard.GetProperty("guardLevel").GetInt32()));
        // 1000 电池 so far, then 1 万.
        await SendAsync(Ordinary(giftId: 31164, giftName: "告白气球", price: 99000, tid: "t4"), Gifts(100000, 11, "告白气球", 1), Item(99000, 1));
        await SendAsync(Ordinary(giftId: 31170, giftName: "小电视飞船", price: 900000, tid: "t5"), Gifts(1000000, 12, "小电视飞船", 1), Item(900000, 1));

        h.Time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(1198000, Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates)).Spend);
        await AssertRoundAsync(pet, Gifts(1000000, 12, "小电视飞船", 1) with { Ended = true });
    }

    [Fact]
    public async Task A_gift_message_tells_each_of_the_viewers_gifts_in_it_a_blind_box_at_its_price_with_the_icon_of_what_it_opened()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        const string bell = "https://s1.hdslb.com/bfs/live/test-bell.png";

        await h.PushAndSyncAsync(updates, BlindGiftV2Fixture.Message(BlindGiftV2Fixture.Broadcast([
            BlindGiftV2Fixture.Item(35311, "好运柚叶", 1, 2500, 5000, "box-1"),
            BlindGiftV2Fixture.Item(35208, "星光铃铛", 8, 5200, 40000, "box-2", icon: bell),
            BlindGiftV2Fixture.Item(35207, "幸运泡泡", 1, 1500, 5000, "box-3", icon: null)])));

        await AssertRoundAsync(pet, new(10001, "测试观众", 0, 0, "", 0, 50000, 10, false, false),
            new(5000, 1, true, BlindGiftV2Fixture.TestIcon), new(40000, 8, true, bell), new(5000, 1, true, ""));
    }

    [Theory]
    [InlineData(1, 198000)]
    [InlineData(3, 594000)]
    public async Task A_guard_tells_what_it_cost_for_its_months(long months, long spend)
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;

        await h.PushAndSyncAsync(updates, UserToastFixture.Message(num: months));

        Assert.Equal(spend, (await pet.NextAsync(DesktopPet.Guard)).GetProperty("spend").GetInt64());
    }

    [Theory]
    [InlineData(Accepted)]
    [InlineData("{\"code\":1003212,\"message\":\"超长\"}")]
    public async Task With_the_announcement_on_its_result_is_told_once_sent_or_given_up_and_boxes_sent_meanwhile_make_the_next(string response)
    {
        await using var h = new WatchHarness();
        var sent = CaptureSends(h, response);
        var release = HoldSends(h, attempt: 1);
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await h.PushAndSyncAsync(updates, Gift(h.Time.GetUtcNow()), Gift(h.Time.GetUtcNow(), price: 2500));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal("本次投入100电池 亏60电池", (await NextSendAsync(sent))["msg"]);
        Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));

        await h.PushAndSyncAsync(updates, Gift(h.Time.GetUtcNow(), price: 5000));

        // Nothing is told of the round being announced until its send ends.
        Assert.Equal([DesktopPet.Gift, DesktopPet.Gift, DesktopPet.Danmaku, DesktopPet.Gift, DesktopPet.Gift, DesktopPet.Danmaku], await pet.NamesAsync(6));
        release.SetResult();
        AssertBlindBox(new(10001, "测试观众", new(10000, 4000), 2, "小蛋糕", 2500, false), "Loss", await pet.NextAsync(DesktopPet.BlindBox));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertBlindBox(new(10001, "测试观众", new(5000, 5000), 1, "小蛋糕", 5000, false), "Even", await pet.NextAsync(DesktopPet.BlindBox));
    }

    [Theory]
    [InlineData("blind_box.announce")]
    [InlineData("blind_box.enabled")]
    public async Task Without_an_announcement_the_boxes_are_told_as_the_gift_round_ends_and_only_a_castle_opened_is_one(string off)
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h, (off, "false"));
        using var _ = pet;
        var castle = With(data => { data["giftId"] = 32132; data["giftName"] = "浪漫城堡"; data["price"] = 2233000; data["total_coin"] = 15000; });
        await h.PushAndSyncAsync(updates, Gift(h.Time.GetUtcNow(), spend: 1500), castle);
        await pet.NextAsync(DesktopPet.Gift);
        await AssertRoundAsync(pet, new(10001, "测试观众", 0, 0, "", 0, 16500, 2, true, false), new GiftItem(15000, 1, true, TestIcon));

        h.Time.Advance(TimeSpan.FromSeconds(3));

        Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        Assert.True((await pet.NextAsync<GiftRoundProgress>(DesktopPet.Gift)).Ended);
        var (name, boxes) = await pet.NextAsync();
        Assert.Equal(DesktopPet.BlindBox, name);
        AssertBlindBox(new(10001, "测试观众", new(16500, 2234500), 2, "浪漫城堡", 2233000, true), "Gain", boxes);
        // B站 gives a castle only out of a 盲盒: one sent as a gift would be that gift.
        await h.PushAndSyncAsync(updates, Ordinary(uid: 20002, giftId: 32132, giftName: "浪漫城堡", price: 2233000, tid: "castle-2"));
        await AssertRoundAsync(pet, new(20002, "测试观众", 2233000, 1, "浪漫城堡", 1, 0, 0, false, false), Item(2233000, 1));
    }

    [Theory]
    [InlineData(32132, true)]
    [InlineData(34632, true)]
    [InlineData(34633, true)]
    [InlineData(34634, true)]
    [InlineData(31294, false)]
    public void Every_restyle_of_the_romantic_castle_is_a_castle_and_no_other_castle_is(long giftId, bool castle) =>
        Assert.Equal(castle, new Gift(10001, "测试观众", 2233000) { GiftId = giftId }.IsCastle);

    private static void AssertBlindBox(BlindBoxResult expected, string outcome, JsonElement told)
    {
        Assert.Equal(expected, told.Deserialize<BlindBoxResult>(WebServer.Json));
        Assert.Equal(outcome, told.GetProperty("tally").GetProperty("outcome").GetString());
    }

    /// <summary>The next 一轮送礼 told: its totals as <paramref name="round"/>, and the gifts of the message it was told after as
    /// <paramref name="items"/>.</summary>
    private static async Task AssertRoundAsync(DesktopPetEvents pet, GiftRoundProgress round, params GiftItem[] items)
    {
        var told = await pet.NextAsync<GiftRoundProgress>(DesktopPet.Gift);
        Assert.Equal(items, told.Items);
        Assert.Equal(round, told with { Items = round.Items });
    }

    private static GiftRoundProgress Gifts(long spend, long count, string topGift, long topCount) =>
        new(10001, "测试观众", spend, count, topGift, topCount, 0, 0, false, false);

    private static GiftItem Item(long spend, long count) => new(spend, count, false, TestIcon);
}
