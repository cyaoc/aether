using Aether.Core;
using Avalonia.Controls;

namespace Aether.Gui;

/// <summary>Number boxes for thresholds that must each be a positive number higher than the one before, as Core checks them, with
/// one error line under them all: the 送礼动画's four tiers, the 桌宠's 中礼物 and 大礼物.</summary>
/// <param name="notRising">The error when the order is wrong; <paramref name="notRisingToSave"/> says so on the save bar.</param>
internal sealed class ThresholdInputs(IReadOnlyList<NumericUpDown> inputs, TextBlock error, string notRising, string notRisingToSave)
{
    /// <summary>Marks what is wrong; null when the thresholds can be saved, otherwise why not, for the save bar.</summary>
    public string? Check()
    {
        var values = inputs.Select(input => int.TryParse(input.Text, out var value) ? value : 0).ToArray();
        var invalid = Settings.InvalidGiftThreshold(values) is not null;
        var notPositive = values.Any(value => value <= 0);
        error.Text = !invalid ? "" : notPositive ? "门槛要填正整数" : notRising;
        error.IsVisible = invalid;
        foreach (var (input, index) in inputs.Select((input, index) => (input, index)))
            input.Classes.Set("invalid", values[index] <= (index == 0 ? 0 : values[index - 1]));
        return !invalid ? null : notPositive ? "门槛要填正整数，改好后才能保存" : notRisingToSave;
    }
}
