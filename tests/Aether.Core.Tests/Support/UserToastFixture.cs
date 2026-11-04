using System.Text.Json.Nodes;

namespace Aether.Core.Tests.Support;

// USER_TOAST_MSG_V2: the JSON shape and the fields kept here follow sjh8130/bili_danmaku's Testing.md (updated 2024-08-01),
// blivedm's UserToastV2Message and blivechat's userToastV2Callback; the 138000 and 198000 prices are the ones in BAC's
// USER_TOAST_MSG and GUARD_BUY samples. uid, names, face and payflow_id are test values. Pushes of several months or of
// a unit other than 月 have never been captured: they are built from those field names and the streamer's 2026-11 price
// list. This is not a capture.
internal static class UserToastFixture
{
    public const string TestFace = "https://i0.hdslb.com/bfs/face/test-toast.jpg";
    public const string TestPayflowId = "test-payflow-1";

    /// <summary>A purchase by one viewer, with <paramref name="edit"/> applied to its data object; the default is a 舰长
    /// bought for one month at the list price.</summary>
    public static string Message(int guardLevel = 3, long price = 198000, long num = 1, string unit = "月",
        long source = 0, long uid = 10001, string payflowId = TestPayflowId, Action<JsonObject>? edit = null)
    {
        var data = new JsonObject
        {
            ["sender_uinfo"] = new JsonObject
            {
                ["uid"] = uid, ["base"] = new JsonObject { ["name"] = "测试观众", ["face"] = TestFace }
            },
            ["guard_info"] = new JsonObject { ["guard_level"] = guardLevel },
            ["pay_info"] = new JsonObject
            {
                ["payflow_id"] = payflowId, ["price"] = price, ["num"] = num, ["unit"] = unit
            },
            // Only passed through: 10003 for 舰长 is in BAC's samples, the other two levels follow that pattern unverified.
            ["gift_info"] = new JsonObject { ["gift_id"] = 10000 + guardLevel },
            ["option"] = new JsonObject { ["source"] = source }
        };
        edit?.Invoke(data);
        return new JsonObject { ["cmd"] = "USER_TOAST_MSG_V2", ["data"] = data }.ToJsonString();
    }
}
