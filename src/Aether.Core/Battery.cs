namespace Aether.Core;

/// <summary>电池 (GLOSSARY): 1 电池 = 100 金瓜子. Amounts travel as integer 金瓜子; settings and what viewers see are in 电池.</summary>
public static class Battery
{
    private const long Coins = 100;

    public static long ToCoins(long batteries) => batteries * Coins;

    public static decimal FromCoins(long coins) => (decimal)coins / Coins;

    /// <summary>"12.5 电池": at most two decimals, none on a whole number.</summary>
    public static string Text(long coins) => $"{FromCoins(coins):0.##} 电池";
}
