using System.Globalization;
using Aether.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Aether.Gui;

public partial class EntryAwardRow : UserControl
{
    private bool errorsRevealed;
    public event Action? Changed;
    public event Action? RemoveRequested;
    public event Action? DrawRequested;
    public LotteryAward Value => new(NameInput.Text ?? "", PrizeInput.Text ?? "", int.TryParse(CapacityInput.Text,
        NumberStyles.None, CultureInfo.InvariantCulture, out var capacity) ? capacity : 0, (Grade)GradeInput.SelectedIndex);

    public EntryAwardRow() : this(new("", "", 1, Grade.Grand), true) { }
    public EntryAwardRow(LotteryAward award, bool isNew = false)
    {
        InitializeComponent();
        errorsRevealed = !isNew;
        NameInput.Text = award.Name;
        PrizeInput.Text = award.Prize;
        CapacityInput.Text = award.Capacity.ToString(CultureInfo.InvariantCulture);
        GradeInput.ItemsSource = LotteryPanel.GradeNames;
        GradeInput.SelectedIndex = (int)award.Grade;
        NameInput.TextChanged += (_, _) => Changed?.Invoke();
        PrizeInput.TextChanged += (_, _) => Changed?.Invoke();
        CapacityInput.TextChanged += (_, _) => Changed?.Invoke();
        GradeInput.SelectionChanged += (_, _) => Changed?.Invoke();
        LostFocus += (_, _) => { errorsRevealed = true; Changed?.Invoke(); };
        RemoveButton.Click += (_, _) => { if (RemoveButton.IsEnabled) RemoveRequested?.Invoke(); };
        DrawButton.Click += (_, _) => { if (DrawButton.IsEnabled) DrawRequested?.Invoke(); };
    }

    public bool Refresh(int index, int count, EntryLottery lottery, bool busy, bool canControl)
    {
        var editable = lottery.Phase == EntryLotteryPhase.Preparing;
        var value = Value;
        RowBorder.BorderThickness = new Thickness(0, index > 0 ? 1 : 0, 0, 0);
        AutomationProperties.SetName(NameInput, $"第 {index + 1} 个奖项名称");
        AutomationProperties.SetName(PrizeInput, $"第 {index + 1} 个奖项的奖品");
        AutomationProperties.SetName(CapacityInput, $"第 {index + 1} 个奖项名额");
        AutomationProperties.SetName(GradeInput, $"第 {index + 1} 个奖项档次");
        AutomationProperties.SetName(RemoveButton, $"删除第 {index + 1} 个奖项");
        NameInput.IsReadOnly = PrizeInput.IsReadOnly = CapacityInput.IsReadOnly = !editable || busy;
        CapacityPanel.IsVisible = GradeInput.IsVisible = RemoveButton.IsVisible = editable;
        GradeInput.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy && count > 1;
        GradeBadge.IsVisible = DrawCount.IsVisible = DrawButton.IsVisible = !editable;
        LotteryPanel.SetGrade(GradeBadge, GradeText, value.Grade);
        var drawn = lottery.Draws.Count(draw => draw.AwardIndex == index);
        var full = drawn >= value.Capacity;
        DrawCount.Text = $"已抽 {drawn} / {value.Capacity}";
        DrawCount.Classes.Set("full", full);
        DrawButton.Classes.Set("full", full);
        DrawText.Text = full ? "抽完了" : lottery.Phase == EntryLotteryPhase.Open ? "截止后抽" : "抽一个";
        AutomationProperties.SetName(DrawButton, full ? $"{value.Name}已抽完" : $"抽一位{value.Name}");
        DrawButton.IsEnabled = !busy && canControl && lottery.Phase == EntryLotteryPhase.Closed && !lottery.IsDrawing
            && !full && lottery.Entries.Length > lottery.Draws.Length;
        ToolTip.SetTip(DrawButton, lottery.Entries.Length <= lottery.Draws.Length ? "没有还没中奖的报名观众可抽" : null);
        var error = LotteryController.ValidateAward(value);
        var shown = editable && errorsRevealed ? error : null;
        NameInput.Classes.Set("invalid", shown is not null && string.IsNullOrWhiteSpace(value.Name));
        PrizeInput.Classes.Set("invalid", shown is not null && string.IsNullOrWhiteSpace(value.Prize));
        CapacityInput.Classes.Set("invalid", shown is not null && value.Capacity <= 0);
        ErrorMessage.Text = shown;
        ErrorMessage.IsVisible = shown is not null;
        return error is not null;
    }

    public void FocusInput() { NameInput.Focus(); this.BringIntoView(); }
}
