using System.Text;
using System.Text.Json;
using Aether.Core.Gifts;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Aether.Core.Tests.Support.BlindGiftV2Fixture;

namespace Aether.Core.Tests;

public sealed class GiftDecodingTests
{
    [Fact]
    public void V1_gift_carries_what_the_gift_board_records()
    {
        var message = BlindGiftFixture.With(data => { data["blind_gift"] = null; data["total_coin"] = 1500; });
        Assert.Equal(new Gift(10001, "测试观众", 1500, null)
        {
            GiftId = 32698, GiftName = "小蛋糕", Price = 1500, Num = 1, Tid = "test-v1-1",
            FaceUrl = "https://i0.hdslb.com/bfs/face/test-v1.jpg", IconUrl = "https://s1.hdslb.com/bfs/live/test-v1.png"
        }, Assert.Single(Decode(message)));
    }

    [Fact]
    public void V1_blind_box_is_the_gift_it_opened()
    {
        var gift = Assert.Single(Decode(BlindGiftFixture.Json));
        Assert.Equal((32698L, "小蛋糕", 1500L, 5000L), (gift.GiftId, gift.GiftName, gift.Price, gift.Spend));
        Assert.Equal(32649, Assert.IsType<BlindBox>(gift.BlindBox).BlindGiftId);
    }

    [Fact]
    public void V2_gift_carries_what_the_gift_board_records_and_its_own_tid()
    {
        var message = Message(Broadcast([Item(num: 3, spend: 7500, tid: "test-v2-9")], blindId: 0));
        Assert.Equal(new Gift(10001, "测试观众", 7500, null)
        {
            GiftId = 35311, GiftName = "好运柚叶", Price = 2500, Num = 3, Tid = "test-v2-9", FaceUrl = TestFace, IconUrl = TestIcon
        }, Assert.Single(Decode(message)));
    }

    [Fact]
    public void V2_blind_box_items_are_the_gifts_they_opened_each_with_its_icon_and_the_viewers_face()
    {
        var gifts = Decode(Message(Broadcast([Item(icon: "https://s1.hdslb.com/bfs/live/a.png"),
            Item(35208, "星光铃铛", 8, 5200, 40000, "test-v2-2", icon: "https://s1.hdslb.com/bfs/live/b.png")])));
        Assert.Equal([35311L, 35208L], gifts.Select(gift => gift.GiftId));
        Assert.Equal([2500L, 5200L], gifts.Select(gift => gift.Price));
        Assert.Equal(["https://s1.hdslb.com/bfs/live/a.png", "https://s1.hdslb.com/bfs/live/b.png"], gifts.Select(gift => gift.IconUrl));
        Assert.All(gifts, gift =>
        {
            Assert.Equal(TestFace, gift.FaceUrl);
            Assert.Equal(35206, Assert.IsType<BlindBox>(gift.BlindBox).BlindGiftId);
        });
    }

    [Fact]
    public void V2_gift_without_face_or_icon_still_counts_with_empty_urls()
    {
        var gift = Assert.Single(Decode(Message(Broadcast([Item(icon: null)], blindId: 0, face: null))));
        Assert.Equal(("", ""), (gift.FaceUrl, gift.IconUrl));
    }

    [Fact]
    public void A_guard_purchase_is_a_gift_of_its_level_at_its_cost()
    {
        Assert.Equal(new Gift(10001, "测试观众", 198000, null)
        {
            GiftId = 10003, GiftName = "舰长", Price = 198000, Num = 1, FaceUrl = UserToastFixture.TestFace,
            Tid = UserToastFixture.TestPayflowId, GuardLevel = 3
        }, Assert.Single(Decode(UserToastFixture.Message())));
    }

    // Prices are 金瓜子 from the streamer's 2026-11 price list. A push's price is the unit price up to the single-month
    // list price (198000 舰长, 1998000 提督, 19998000 总督) and the total above it; a unit other than 月 counts once.
    // No capture of a push with num above 1 or another unit exists, so those rows are synthetic (Fixtures/README.md).
    [Theory]
    [InlineData(3, 138000, 1, "月", 138000, 1, 138000)]
    [InlineData(3, 168000, 1, "月", 168000, 1, 168000)]
    [InlineData(3, 198000, 1, "月", 198000, 1, 198000)]
    [InlineData(2, 1599000, 1, "月", 1599000, 1, 1599000)]
    [InlineData(2, 1998000, 1, "月", 1998000, 1, 1998000)]
    [InlineData(1, 15999000, 1, "月", 15999000, 1, 15999000)]
    [InlineData(1, 19998000, 1, "月", 19998000, 1, 19998000)]
    [InlineData(3, 534000, 3, "月", 534000, 3, 178000)]
    [InlineData(3, 178000, 3, "月", 534000, 3, 178000)]
    [InlineData(2, 19576000, 12, "月", 19576000, 12, 1631333)]
    [InlineData(2, 1631333, 12, "月", 19575996, 12, 1631333)]
    [InlineData(1, 51994000, 3, "月", 51994000, 3, 17331333)]
    [InlineData(1, 17331333, 3, "月", 51993999, 3, 17331333)]
    [InlineData(3, 198000, 3, "月", 594000, 3, 198000)]
    [InlineData(3, 99000, 1, "5天", 99000, 1, 99000)]
    [InlineData(3, 99000, 2, "5天", 99000, 1, 99000)]
    public void The_spend_count_and_unit_price_of_a_purchase_follow_the_price_rule(
        int guardLevel, long price, long num, string unit, long spend, long count, long unitPrice)
    {
        var gift = Assert.Single(Decode(UserToastFixture.Message(guardLevel, price, num, unit)));
        Assert.Equal((spend, count, unitPrice), (gift.Spend, gift.Num, gift.Price));
    }

    [Fact]
    public void A_purchase_without_a_payflow_id_has_no_tid() =>
        Assert.Null(Assert.Single(Decode(UserToastFixture.Message(payflowId: ""))).Tid);

    [Fact]
    public void A_purchase_that_cost_nothing_is_not_a_gift() =>
        Assert.Empty(Decode(UserToastFixture.Message(price: 0)));

    [Theory]
    [InlineData(0, 198000, 1, 10001)]
    [InlineData(4, 198000, 1, 10001)]
    [InlineData(3, -1, 1, 10001)]
    [InlineData(3, 198000, 0, 10001)]
    [InlineData(3, 198000, 1, -1)]
    public void A_purchase_with_an_impossible_level_price_count_or_viewer_is_a_format_error(
        int guardLevel, long price, long num, long uid) =>
        Assert.Throws<FormatException>(() => Decode(UserToastFixture.Message(guardLevel, price, num, uid: uid)));

    [Fact]
    public void A_purchase_whose_spend_overflows_is_rejected() =>
        Assert.Throws<OverflowException>(() => Decode(UserToastFixture.Message(num: long.MaxValue)));

    [Fact]
    public void An_ordinary_purchase_is_not_logged()
    {
        var logger = new RecordingLogger();
        Decode(UserToastFixture.Message(), logger);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(178000, 3, "月")]
    [InlineData(99000, 1, "5天")]
    public void A_purchase_of_several_months_or_another_unit_is_logged_with_its_raw_values(long price, long num, string unit)
    {
        var logger = new RecordingLogger();
        Decode(UserToastFixture.Message(3, price, num, unit), logger);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal<object?>([3, num, price, unit], [entry.Properties["GuardLevel"], entry.Properties["Num"], entry.Properties["Price"], entry.Properties["Unit"]]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)] // B站 pushes every purchase twice: source 0 first, then 2.
    public void Only_source_0_counts_as_the_purchase(long source) =>
        Assert.Empty(Decode(UserToastFixture.Message(source: source)));

    private static Gift[] Decode(string message, ILogger? logger = null)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var command = root.GetProperty("cmd").GetString();
        Assert.NotNull(command);
        return GiftMessages.Decode(command, root, Encoding.UTF8.GetBytes(message), logger ?? NullLogger.Instance);
    }
}
