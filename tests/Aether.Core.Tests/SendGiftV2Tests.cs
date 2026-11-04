using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.BlindGiftV2Fixture;
using static Aether.Core.Tests.Support.GiftDecoder;

namespace Aether.Core.Tests;

public sealed class SendGiftV2Tests
{
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
}
