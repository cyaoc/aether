using System.Globalization;

namespace Aether.Core;

public sealed record AnimationChoice(EntryTier? Tier = null, string? Video = null)
{
    public static AnimationChoice Off { get; } = new();
    public static AnimationChoice? Parse(string text)
    {
        if (text == "0") return Off;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && EntryTier.Parse(value) is { } tier)
            return new(tier);
        return Settings.ValidateVideoFileName(text) is null ? new(Video: text) : null;
    }
    public override string ToString() => Video ?? Tier?.ToString() ?? "0";
}
