using System.Text.Json.Nodes;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.GiftDecoder;

namespace Aether.Core.Tests;

public sealed class UserToastV2Tests
{
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

    // B站 often leaves the avatar out of a purchase.
    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public void A_purchase_without_a_face_still_counts_with_an_empty_address(string? face)
    {
        var message = UserToastFixture.Message(edit: data =>
        {
            if (data["sender_uinfo"]?["base"] is not JsonObject viewer) return;
            if (face is null) viewer.Remove("face"); else viewer["face"] = JsonNode.Parse(face);
        });
        Assert.Equal("", Assert.Single(Decode(message)).FaceUrl);
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
}
