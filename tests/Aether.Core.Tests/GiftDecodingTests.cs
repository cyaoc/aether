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

    private static Gift[] Decode(string message, ILogger? logger = null)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var command = root.GetProperty("cmd").GetString();
        Assert.NotNull(command);
        return GiftMessages.Decode(command, root, Encoding.UTF8.GetBytes(message), logger ?? NullLogger.Instance);
    }
}
