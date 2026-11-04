using System.Text.Json.Nodes;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.GiftDecoder;

namespace Aether.Core.Tests;

public sealed class SendGiftV1Tests
{
    private const string Face = "https://i0.hdslb.com/bfs/face/test-v1.jpg";
    private const string Icon = "https://s1.hdslb.com/bfs/live/test-v1.png";

    [Fact]
    public void V1_gift_carries_what_the_gift_board_records()
    {
        var message = BlindGiftFixture.With(data => { data["blind_gift"] = null; data["total_coin"] = 1500; });
        Assert.Equal(new Gift(10001, "测试观众", 1500, null)
        {
            GiftId = 32698, GiftName = "小蛋糕", Price = 1500, Num = 1, Tid = "test-v1-1", FaceUrl = Face, IconUrl = Icon
        }, Assert.Single(Decode(message)));
    }

    [Fact]
    public void V1_blind_box_is_the_gift_it_opened()
    {
        var gift = Assert.Single(Decode(BlindGiftFixture.Json));
        Assert.Equal((32698L, "小蛋糕", 1500L, 5000L), (gift.GiftId, gift.GiftName, gift.Price, gift.Spend));
        Assert.Equal(32649, Assert.IsType<BlindBox>(gift.BlindBox).BlindGiftId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public void V1_gift_without_face_still_counts_with_an_empty_address(string? face) =>
        Assert.Equal(("", Icon), Urls("face", face));

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("null")]
    public void V1_gift_without_icon_still_counts_with_an_empty_address(string? giftInfo) =>
        Assert.Equal((Face, ""), Urls("gift_info", giftInfo));

    /// <summary>The ordinary gift's avatar and icon addresses with <paramref name="field"/> set to <paramref name="json"/>,
    /// or removed when that is null.</summary>
    private static (string Face, string Icon) Urls(string field, string? json)
    {
        var message = BlindGiftFixture.With(data =>
        {
            data["blind_gift"] = null;
            if (json is null) data.Remove(field); else data[field] = JsonNode.Parse(json);
        });
        var gift = Assert.Single(Decode(message));
        return (gift.FaceUrl, gift.IconUrl);
    }
}
