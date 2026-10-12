using System.Text.Json;
using static Aether.Core.Tests.Support.BlindGiftV2Fixture;

namespace Aether.Core.Tests.Support;

// Synthetic viewers; field numbers follow #73's pinned xuserreward/v1.proto. This is not a capture.
internal static class EntryV2Fixture
{
    public static string Message(long uid = 10001, string nickname = "测试观众", long guard = 3, long messageType = 1,
        long fanClubRoom = 7734200, long fanClubLevel = 47, long wealthLevel = 45, bool fanClubLit = true) => Wrap(
    [
        .. Integer(1, uid), .. Text(2, nickname), .. Integer(5, messageType),
        .. Bytes(9, [.. Integer(1, 99999), .. Integer(2, fanClubLevel), .. Integer(8, fanClubLit ? 1 : 0), .. Integer(9, 1), .. Integer(12, fanClubRoom)]),
        .. Integer(16, guard), .. Bytes(22, Bytes(4, Integer(1, wealthLevel)))
    ]);

    public static string Wrap(byte[] bytes) => JsonSerializer.Serialize(new
    {
        cmd = "INTERACT_WORD_V2", data = new { pb = Convert.ToBase64String(bytes) }
    });
}
