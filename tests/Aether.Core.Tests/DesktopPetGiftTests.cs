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
        async Task<GiftRoundProgress> SendAsync(string message)
        {
            await h.PushAndSyncAsync(updates, message);
            return await pet.NextAsync<GiftRoundProgress>(DesktopPet.Gift);
        }

        Assert.Equal(Gifts(100, 1, "小花花", 1), await SendAsync(Ordinary(giftId: 31036, giftName: "小花花", price: 100, num: 1, tid: "t1")));
        Assert.Equal(Gifts(500, 5, "小花花", 5), await SendAsync(Ordinary(giftId: 31036, giftName: "小花花", price: 100, num: 4, tid: "t2")));
        Assert.Equal(Gifts(1000, 10, "小花花", 10), await SendAsync(Ordinary(giftId: 31036, giftName: "小花花", price: 100, num: 5, tid: "t3")));
        await h.PushAndSyncAsync(updates, UserToastFixture.Message());
        var guard = await pet.NextAsync(DesktopPet.Guard);
        Assert.Equal((10001L, "测试观众", 3), (guard.GetProperty("uid").GetInt64(), guard.GetProperty("nickname").GetString(),
            guard.GetProperty("guardLevel").GetInt32()));
        // 1000 电池 so far, then 1 万.
        Assert.Equal(Gifts(100000, 11, "告白气球", 1), await SendAsync(Ordinary(giftId: 31164, giftName: "告白气球", price: 99000, tid: "t4")));
        Assert.Equal(Gifts(1000000, 12, "小电视飞船", 1), await SendAsync(Ordinary(giftId: 31170, giftName: "小电视飞船", price: 900000, tid: "t5")));

        h.Time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(1198000, Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates)).Spend);
        Assert.Equal(Gifts(1000000, 12, "小电视飞船", 1) with { Ended = true }, await pet.NextAsync<GiftRoundProgress>(DesktopPet.Gift));
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
    public async Task Without_an_announcement_the_boxes_are_told_as_the_gift_round_ends_and_a_castle_counts_sent_or_opened(string off)
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h, (off, "false"));
        using var _ = pet;
        var castle = With(data => { data["giftId"] = 32132; data["giftName"] = "浪漫城堡"; data["price"] = 2233000; data["total_coin"] = 15000; });
        await h.PushAndSyncAsync(updates, Gift(h.Time.GetUtcNow(), spend: 1500), castle);
        await pet.NextAsync(DesktopPet.Gift);
        Assert.Equal(new GiftRoundProgress(10001, "测试观众", 0, 0, "", 0, 16500, 2, true, false), await pet.NextAsync<GiftRoundProgress>(DesktopPet.Gift));

        h.Time.Advance(TimeSpan.FromSeconds(3));

        Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        Assert.True((await pet.NextAsync<GiftRoundProgress>(DesktopPet.Gift)).Ended);
        var (name, boxes) = await pet.NextAsync();
        Assert.Equal(DesktopPet.BlindBox, name);
        AssertBlindBox(new(10001, "测试观众", new(16500, 2234500), 2, "浪漫城堡", 2233000, true), "Gain", boxes);
        await h.PushAndSyncAsync(updates, Ordinary(uid: 20002, giftId: 32132, giftName: "浪漫城堡", price: 2233000));
        Assert.Equal(new GiftRoundProgress(20002, "测试观众", 2233000, 1, "浪漫城堡", 1, 0, 0, true, false),
            await pet.NextAsync<GiftRoundProgress>(DesktopPet.Gift));
    }

    private static void AssertBlindBox(BlindBoxResult expected, string outcome, JsonElement told)
    {
        Assert.Equal(expected, told.Deserialize<BlindBoxResult>(WebServer.Json));
        Assert.Equal(outcome, told.GetProperty("tally").GetProperty("outcome").GetString());
    }

    private static GiftRoundProgress Gifts(long spend, long count, string topGift, long topCount) =>
        new(10001, "测试观众", spend, count, topGift, topCount, 0, 0, false, false);
}
