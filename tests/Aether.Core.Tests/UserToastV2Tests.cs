using System.Text.Json.Nodes;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.GiftDecoder;

namespace Aether.Core.Tests;

public sealed class UserToastV2Tests
{
    [Fact]
    public void A_guard_purchase_is_a_gift_of_its_level_at_its_cost_by_a_viewer_of_that_level()
    {
        Assert.Equal(new Gift(10001, "测试观众", 198000)
        {
            GiftId = 10003, GiftName = "舰长", Price = 198000, Num = 1, Value = 198000, FaceUrl = UserToastFixture.TestFace,
            Tid = UserToastFixture.TestPayflowId, GuardLevel = 3, ViewerGuardLevel = 3
        }, Assert.Single(Decode(UserToastFixture.Message())));
    }

    // Prices are 金瓜子 from the streamer's 2026-11 price list. A push's price is the unit price up to the single-month
    // list price (198000 舰长, 1998000 提督, 19998000 总督) and the total above it; a unit other than 月 counts once.
    // The only capture of a push with num above 1 is a 12-month 续费 (below); none exists of another unit, so those rows
    // are synthetic (Fixtures/README.md).
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

    // On the 大航海 panel, or a 舰长一号 sent from the gift panel.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void An_ordinary_purchase_is_not_logged(long source)
    {
        var logger = new RecordingLogger();
        Decode(UserToastFixture.Message(source: source), logger);
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

    // 0 is bought on the 大航海 panel and 1 is a 舰长一号 sent from the gift panel; what 2 is has never been captured (#427).
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_purchase_counts_whatever_its_source(long source) =>
        Assert.Equal(198000, Assert.Single(Decode(UserToastFixture.Message(source: source))).Spend);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_purchase_of_a_source_never_seen_counts_and_is_logged_whole(bool withoutOption)
    {
        var logger = new RecordingLogger();
        var message = UserToastFixture.Message(source: 2, edit: data => { if (withoutOption) data.Remove("option"); });
        Assert.Equal(198000, Assert.Single(Decode(message, logger)).Spend);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(withoutOption ? null : 2L, entry.Properties["Source"]);
        Assert.Contains(UserToastFixture.TestPayflowId, Assert.IsType<string>(entry.Properties["Json"]));
    }

    // The rows of the capture's table in Fixtures/README.md: bought on the 大航海 panel at the 续费 price, a 舰长一号 from the
    // gift panel (source 1, at the list price), 12 months whose price is the total, and a 开通 at a price off the price list.
    [Theory]
    [InlineData(1, 168000, 1, 168000)]
    [InlineData(2, 198000, 1, 198000)]
    [InlineData(3, 2016000, 12, 168000)]
    [InlineData(4, 1900, 1, 1900)]
    public void A_captured_purchase_is_a_captain_at_what_was_paid(int row, long spend, long count, long unitPrice)
    {
        var gift = Assert.Single(Decode(UserToastFixture.Captured(row)));
        Assert.Equal(new Gift(10001, "测试观众", spend)
        {
            GiftId = 10003, GiftName = "舰长", Price = unitPrice, Num = count, Value = spend,
            Tid = $"261127000000000000000000{row}", GuardLevel = 3, ViewerGuardLevel = 3
        }, gift);
    }
}
