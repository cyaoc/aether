using System.Text;
using System.Text.Json;

namespace Aether.Core.Tests.Support;

// Rebuilt from https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5103541545,
// corrected by #issuecomment-5105694004: discount_price=6, total_coin=7.
// uid, uname, tid, timestamp and switch were omitted there and are synthetic; this is not a capture.
internal static class BlindGiftV2Fixture
{
    public static byte[][] TenBoxes =>
    [
        Item(35311, "好运柚叶", 1, 2500, 5000, "test-v2-1"),
        Item(35208, "星光铃铛", 8, 5200, 40000, "test-v2-2"),
        Item(35207, "幸运泡泡", 1, 1500, 5000, "test-v2-3")
    ];

    public static string Message(byte[]? bytes = null, bool nested = false)
    {
        var payload = new { pb = Convert.ToBase64String(bytes ?? Broadcast()) };
        return JsonSerializer.Serialize(new { cmd = "SEND_GIFT_V2", data = nested ? (object)new { data = payload } : payload });
    }

    public static byte[] Broadcast(byte[][]? items = null, long uid = 10001, long blindId = 35206, bool? shown = true) =>
    [
        .. Integer(1, uid), .. Text(2, "测试观众"),
        .. Bytes(9, [.. Integer(1, 144), .. Integer(2, blindId), .. Text(3, "幸运盲盒"),
            .. Text(5, "爆出"), .. Integer(6, 5000)]),
        .. (items ?? TenBoxes).SelectMany(item => Bytes(10, item)),
        .. (shown is { } value ? Integer(11, value ? 1 : 0) : [])
    ];

    public static byte[] Item(long id = 35311, string name = "好运柚叶", long num = 1, long price = 2500,
        long spend = 5000, string? tid = "test-v2-1", string coin = "gold", long timestamp = 1732634266) =>
    [
        .. Integer(1, id), .. Text(2, name), .. Integer(3, num), .. Integer(5, price),
        .. Integer(6, price), .. Integer(7, spend), .. Text(8, coin),
        .. (tid is null ? [] : Text(9, tid)), .. Integer(10, timestamp)
    ];

    public static byte[] Integer(int field, long value) => [.. Varint((ulong)field << 3), .. Varint(unchecked((ulong)value))];
    public static byte[] Text(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
    public static byte[] Bytes(int field, byte[] value) => [.. Varint(((ulong)field << 3) | 2), .. Varint((ulong)value.Length), .. value];

    private static byte[] Varint(ulong value)
    {
        List<byte> bytes = [];
        while (value >= 128)
        {
            bytes.Add((byte)((value & 127) | 128));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }
}
