using System.Globalization;
using Aether.Core;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Aether.Gui;

public partial class PoolPrizeRow : UserControl
{
    private bool errorsRevealed;
    public event Action? Changed;
    public event Action? RemoveRequested;
    public PoolPrize Value => new(NameInput.Text ?? "", int.TryParse(WeightInput.Text, NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var weight) ? weight : 0, (PrizeGrade)GradeInput.SelectedIndex);

    public PoolPrizeRow() : this(new("", 1, PrizeGrade.Third), true) { }
    public PoolPrizeRow(PoolPrize prize, bool isNew = false)
    {
        InitializeComponent();
        errorsRevealed = !isNew;
        NameInput.Text = prize.Name;
        WeightInput.Value = prize.Weight;
        GradeInput.ItemsSource = new[] { "大奖", "二奖", "三奖" };
        GradeInput.SelectedIndex = (int)prize.Grade;
        NameInput.TextChanged += (_, _) => Changed?.Invoke();
        WeightInput.PropertyChanged += (_, e) => { if (e.Property == NumericUpDown.TextProperty) Changed?.Invoke(); };
        GradeInput.SelectionChanged += (_, _) => Changed?.Invoke();
        LostFocus += (_, _) => { errorsRevealed = true; Changed?.Invoke(); };
        RemoveButton.Click += (_, _) => RemoveRequested?.Invoke();
    }

    public bool Refresh(int index, int count, long total)
    {
        AutomationProperties.SetName(NameInput, $"第 {index + 1} 件奖品名称");
        AutomationProperties.SetName(WeightInput, $"第 {index + 1} 件奖品占比");
        AutomationProperties.SetName(GradeInput, $"第 {index + 1} 件奖品档次");
        AutomationProperties.SetName(RemoveButton, $"删除第 {index + 1} 件奖品");
        RemoveButton.IsEnabled = count > 1;
        Percentage.Text = total > 0 && Value.Weight > 0 ? $"{100d * Value.Weight / total:0.#}%" : "—";
        var error = PoolLottery.ValidatePrize(Value);
        ErrorMessage.Text = errorsRevealed ? error : null;
        ErrorMessage.IsVisible = errorsRevealed && error is not null;
        return error is not null;
    }

    public void FocusInput() { NameInput.Focus(); this.BringIntoView(); }
}
