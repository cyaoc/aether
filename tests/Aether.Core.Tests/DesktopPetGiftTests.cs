using System.Text.Json;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.BlindGiftFixture;

namespace Aether.Core.Tests;

public sealed class DesktopPetGiftTests
{
    [Fact]
    public async Task Each_gift_message_tells_the_round_so_far_without_its_guard_and_the_gift_animations_round_ends_it()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        async Task<JsonElement> SendAsync(string message)
        {
            await h.PushAndSyncAsync(updates, message);
            return await pet.NextAsync("gift");
        }

        AssertProgress(await SendAsync(Ordinary(giftId: 31036, giftName: "小花花", price: 100, num: 1, tid: "t1")), 100, 1, "小花花", 1);
        AssertProgress(await SendAsync(Ordinary(giftId: 31036, giftName: "小花花", price: 100, num: 4, tid: "t2")), 500, 5, "小花花", 5);
        AssertProgress(await SendAsync(Ordinary(giftId: 31036, giftName: "小花花", price: 100, num: 5, tid: "t3")), 1000, 10, "小花花", 10);
        await h.PushAndSyncAsync(updates, UserToastFixture.Message());
        var guard = await pet.NextAsync("guard");
        Assert.Equal((10001L, "测试观众", 3), (guard.GetProperty("uid").GetInt64(), guard.GetProperty("nickname").GetString(),
            guard.GetProperty("guardLevel").GetInt32()));
        // 1000 电池 so far, then 1 万.
        AssertProgress(await SendAsync(Ordinary(giftId: 31164, giftName: "告白气球", price: 99000, tid: "t4")), 100000, 11, "告白气球", 1);
        AssertProgress(await SendAsync(Ordinary(giftId: 31170, giftName: "小电视飞船", price: 900000, tid: "t5")), 1000000, 12, "小电视飞船", 1);

        h.Time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(1198000, Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates)).Spend);
        AssertProgress(await pet.NextAsync("gift"), 1000000, 12, "小电视飞船", 1, ended: true);
    }

    private static void AssertProgress(JsonElement progress, long spend, long count, string topGift, long topCount, bool ended = false) =>
        Assert.Equal((10001L, "测试观众", spend, count, topGift, topCount, 0L, 0L, false, ended),
            (progress.GetProperty("uid").GetInt64(), progress.GetProperty("nickname").GetString(), progress.GetProperty("spend").GetInt64(),
                progress.GetProperty("count").GetInt64(), progress.GetProperty("topGift").GetString(), progress.GetProperty("topCount").GetInt64(),
                progress.GetProperty("boxSpend").GetInt64(), progress.GetProperty("boxes").GetInt64(), progress.GetProperty("castle").GetBoolean(),
                progress.GetProperty("ended").GetBoolean()));
}
