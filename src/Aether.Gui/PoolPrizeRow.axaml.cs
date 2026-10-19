using System.Globalization;
using Aether.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Aether.Gui;

public partial class PoolPrizeRow : UserControl
{
    private bool errorsRevealed;
    public event Action? Changed;
    public event Action? RemoveRequested;
    public PoolPrize Value => new(NameInput.Text ?? "", int.TryParse(WeightInput.Text, NumberStyles.None,
        CultureInfo.InvariantCulture, out var weight) ? weight : 0, (Grade)GradeInput.SelectedIndex);

    public PoolPrizeRow() : this(new("", 1, Grade.Third), true) { }
    public PoolPrizeRow(PoolPrize prize, bool isNew = false)
    {
        InitializeComponent();
        errorsRevealed = !isNew;
        NameInput.Text = prize.Name;
        WeightInput.Text = prize.Weight.ToString(CultureInfo.InvariantCulture);
        GradeInput.ItemsSource = LotteryController.GradeNames;
        GradeInput.SelectedIndex = (int)prize.Grade;
        NameInput.TextChanged += (_, _) => Changed?.Invoke();
        WeightInput.TextChanged += (_, _) => Changed?.Invoke();
        GradeInput.SelectionChanged += (_, _) => Changed?.Invoke();
        LostFocus += (_, _) => { errorsRevealed = true; Changed?.Invoke(); };
        RemoveButton.Click += (_, _) => RemoveRequested?.Invoke();
    }

    /// <summary>Rows share one bordered list; every row but the first draws the line above it.</summary>
    public bool Refresh(int index, int count, long total, bool editable)
    {
        RowBorder.BorderThickness = new Thickness(0, index > 0 ? 1 : 0, 0, 0);
        AutomationProperties.SetName(NameInput, $"第 {index + 1} 件奖品名称");
        AutomationProperties.SetName(WeightInput, $"第 {index + 1} 件奖品占比");
        AutomationProperties.SetName(GradeInput, $"第 {index + 1} 件奖品档次");
        AutomationProperties.SetName(RemoveButton, $"删除第 {index + 1} 件奖品");
        NameInput.IsReadOnly = WeightInput.IsReadOnly = !editable;
        GradeInput.IsEnabled = editable;
        RemoveButton.IsVisible = editable;
        RemoveButton.IsEnabled = count > 1;
        var value = Value;
        Percentage.Text = total > 0 && value.Weight > 0 ? $"{100d * value.Weight / total:0.#}%" : "—";
        var error = LotteryController.ValidatePrize(value);
        var shown = errorsRevealed ? error : null;
        NameInput.Classes.Set("invalid", shown is not null && string.IsNullOrWhiteSpace(value.Name));
        WeightInput.Classes.Set("invalid", shown is not null && value.Weight <= 0);
        ErrorMessage.Text = shown;
        ErrorMessage.IsVisible = shown is not null;
        return error is not null;
    }

    public void FocusInput() { NameInput.Focus(); this.BringIntoView(); }
}
