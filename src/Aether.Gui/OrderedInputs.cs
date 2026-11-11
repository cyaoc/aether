using Aether.Core;
using Avalonia.Controls;

namespace Aether.Gui;

/// <summary>Number boxes of one group whose numbers must each be positive and follow one another in <paramref name="order"/>, as
/// Core checks them, with one error line under them all: the 送礼动画's four tiers, the 桌宠's 中礼物 and 大礼物 thresholds and her
/// 日常小动作's least and most seconds.</summary>
internal sealed class OrderedInputs(IReadOnlyList<NumericUpDown> inputs, TextBlock error, Settings.NumberOrder order, OrderTexts texts)
{
    /// <summary>Marks what is wrong; null when the numbers can be saved, otherwise why not, for the save bar.</summary>
    public string? Check()
    {
        var values = inputs.Select(input => int.TryParse(input.Text, out var value) ? value : 0).ToArray();
        var invalid = order.Invalid(values) is not null;
        var notPositive = values.Any(value => value <= 0);
        error.Text = !invalid ? "" : notPositive ? texts.NotPositive : texts.OutOfOrder;
        error.IsVisible = invalid;
        foreach (var (input, index) in inputs.Select((input, index) => (input, index)))
            input.Classes.Set("invalid", !order.Follows(index == 0 ? null : values[index - 1], values[index]));
        return !invalid ? null : notPositive ? texts.NotPositive + "，改好后才能保存" : texts.OutOfOrderToSave;
    }
}

/// <param name="NotPositive">The error when a number is not positive.</param>
/// <param name="OutOfOrder">The error when the order is wrong; <paramref name="OutOfOrderToSave"/> says so on the save bar.</param>
internal sealed record OrderTexts(string NotPositive, string OutOfOrder, string OutOfOrderToSave);
