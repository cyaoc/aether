using System.Text.Json.Nodes;

namespace Aether.Core.Tests.Support;

/// <summary>The V1 blind box SEND_GIFT: BAC's SEND_GIFT shape with third-party blind_gift fields, not our own capture;
/// see Fixtures/README.md.</summary>
internal static class BlindGiftFixture
{
    /// <summary>The fixture's gift_info.img_basic.</summary>
    public const string TestIcon = "https://s1.hdslb.com/bfs/live/test-v1.png";

    public static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "blind-gift-v1.json"));

    /// <summary>The fixture with <paramref name="edit"/> applied to its data object.</summary>
    public static string With(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(Json)!;
        edit(root["data"]!.AsObject());
        return root.ToJsonString();
    }

    /// <summary>An ordinary gift of the fixture's 小蛋糕 shape, with its own <paramref name="tid"/> (none when null), <paramref name="price"/>
    /// and <paramref name="num"/>; the fixture's viewer unless <paramref name="uid"/> says otherwise, telling the sender's
    /// <paramref name="guardLevel"/> (no guard_level when null, as in the fixture).</summary>
    public static string Ordinary(long uid = 10001, string nickname = "测试观众", long giftId = 32698, string giftName = "小蛋糕",
        long price = 1500, long num = 1, string? tid = "test-v1-1", int? guardLevel = null) => With(data =>
    {
        if (guardLevel is { } level) data["guard_level"] = level;
        data["blind_gift"] = null;
        data["uid"] = uid;
        data["uname"] = nickname;
        data["giftId"] = giftId;
        data["giftName"] = giftName;
        data["price"] = price;
        data["num"] = num;
        data["total_coin"] = price * num;
        if (tid is null) data.Remove("tid"); else data["tid"] = tid;
    });

    /// <summary>One fresh blind box (its own tid) sent at <paramref name="sentAt"/>; the rest keeps the fixture's values
    /// unless given.</summary>
    public static string Gift(DateTimeOffset sentAt, long uid = 10001, long spend = 5000, long price = 1500,
        string? nickname = null, long? blindId = null) => With(data =>
    {
        data["uid"] = uid;
        data["total_coin"] = spend;
        data["price"] = price;
        data["timestamp"] = sentAt.ToUnixTimeSeconds();
        data["tid"] = Guid.NewGuid().ToString();
        if (nickname is not null) data["uname"] = nickname;
        if (blindId is not null) data["blind_gift"]!["original_gift_id"] = blindId;
    });
}
