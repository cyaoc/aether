namespace Aether.Core;

public readonly record struct EntryTier
{
    public int Value { get; }
    private EntryTier(int value) => Value = value;
    public static IReadOnlyList<EntryTier> All { get; } = [new(30), new(40), new(50), new(60)];
    public static EntryTier? FromLevel(long level) => All.Where(tier => tier.Value <= level).Select(tier => (EntryTier?)tier).LastOrDefault();
    public static EntryTier? FromSpend(long coins, IReadOnlyList<int> batteryThresholds) =>
        All.Where((_, index) => coins >= Battery.ToCoins(batteryThresholds[index])).Select(tier => (EntryTier?)tier).LastOrDefault();
    public static EntryTier? Parse(int value) => All.Where(tier => tier.Value == value).Select(tier => (EntryTier?)tier).FirstOrDefault();
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
