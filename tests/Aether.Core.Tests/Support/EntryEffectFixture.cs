using System.Text.Json;

namespace Aether.Core.Tests.Support;

// Synthetic viewers; JSON shape follows the local 2026-10-16 capture documented in #154.
internal static class EntryEffectFixture
{
    public static string Message(string command = "ENTRY_EFFECT", long uid = 10001, string nickname = "测试观众",
        long guard = 3, long wealthLevel = 45, long fanClubLevel = 47, long medalAnchor = 90001, long triggerTime = 0) =>
        JsonSerializer.Serialize(new
        {
            cmd = command,
            data = new
            {
                uid, target_id = 90001, trigger_time = triggerTime,
                uinfo = new
                {
                    uid, @base = new { name = nickname }, guard = new { level = guard },
                    wealth = new { level = wealthLevel }, medal = new { level = fanClubLevel, ruid = medalAnchor }
                }
            }
        });
}
