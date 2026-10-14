namespace Aether.Core;

internal static class Guard
{
    // B站's privilege_type: 1 总督, 2 提督, 3 舰长; our tiers run from low to high.
    public static int TierFromPrivilegeType(long privilegeType) => privilegeType is >= 1 and <= 3 ? 4 - (int)privilegeType : 0;

    public static string Name(int tier) => tier is >= 1 and <= 3 ? Settings.WelcomeGuards[tier] : "";
}
