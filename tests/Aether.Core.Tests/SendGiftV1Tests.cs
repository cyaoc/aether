using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.GiftDecoder;

namespace Aether.Core.Tests;

public sealed class SendGiftV1Tests
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
}
