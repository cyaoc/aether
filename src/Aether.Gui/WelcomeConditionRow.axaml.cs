using System.Globalization;
using Aether.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Aether.Gui;

public partial class WelcomeConditionRow : UserControl
{
    private bool errorsRevealed;
    public WelcomeCondition Kind { get; }
    public bool CanEdit { get; }
    public event Action? Changed;
    public event Action? RemoveRequested;

    public WelcomeConditionRow() : this(WelcomeCondition.Uid, "", true) { }

    /// <summary>The name a kind goes by in its row and in hints about it.</summary>
    public static string KindName(WelcomeCondition kind) => Appearance(kind).Name;

    private static (string Name, string Icon) Appearance(WelcomeCondition kind) => kind switch
    {
        WelcomeCondition.Uid => ("指定观众", "M20 21v-2a7 7 0 0 0-14 0v2 M17 7a4 4 0 1 1-8 0a4 4 0 1 1 8 0"),
        WelcomeCondition.FanClubLevel => ("粉丝团等级", "M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1.1-1.1a5.5 5.5 0 0 0-7.8 7.8L12 21l8.8-8.6a5.5 5.5 0 0 0 0-7.8z"),
        WelcomeCondition.Guard => ("大航海身份", "M2 8l5 4 5-9 5 9 5-4-3 12H5z"),
        _ => ("荣耀等级", "M12 3l2.8 5.7 6.2.9-4.5 4.4 1.1 6.2L12 17.3l-5.6 2.9 1.1-6.2L3 9.6l6.2-.9z")
    };

    public WelcomeConditionRow(WelcomeCondition kind, string value, bool canEdit, bool isNew = false)
    {
        errorsRevealed = !isNew || kind != WelcomeCondition.Uid;
        Kind = kind;
        CanEdit = canEdit;
        InitializeComponent();
        (TypeName.Text, var icon) = Appearance(kind);
        TypeIcon.Data = Geometry.Parse(icon);
        UidInput.IsVisible = kind == WelcomeCondition.Uid;
        ThresholdInput.IsVisible = kind != WelcomeCondition.Uid;
        GuardInput.IsVisible = kind == WelcomeCondition.Guard;
        LevelInput.IsVisible = LevelUnit.IsVisible = kind is WelcomeCondition.FanClubLevel or WelcomeCondition.WealthLevel;
        UidInput.Text = value;
        UidInput.IsReadOnly = !canEdit;
        if (LevelInput.IsVisible)
        {
            LevelInput.Value = int.Parse(value, CultureInfo.InvariantCulture);
            LevelInput.Text = value;
        }
        LevelInput.IsReadOnly = !canEdit;
        LevelInput.AllowSpin = canEdit;
        AutomationProperties.SetName(LevelInput, TypeName.Text + "门槛");
        GuardInput.ItemsSource = Settings.WelcomeGuards.Skip(1).ToArray();
        GuardInput.SelectedItem = value;
        GuardInput.IsEnabled = canEdit;
        RemoveButton.IsVisible = canEdit;
        AutomationProperties.SetName(RemoveButton, $"删除{TypeName.Text}条件");
        ReadOnlyMessage.IsVisible = !canEdit;
        UidInput.TextChanged += (_, _) => Changed?.Invoke();
        UidInput.LostFocus += (_, _) => { errorsRevealed = true; Changed?.Invoke(); };
        LevelInput.PropertyChanged += (_, e) =>
        {
            if (e.Property == NumericUpDown.TextProperty) Changed?.Invoke();
        };
        GuardInput.SelectionChanged += (_, _) => Changed?.Invoke();
        RemoveButton.Click += (_, _) => { if (CanEdit) RemoveRequested?.Invoke(); };
    }

    public string Value
    {
        get
        {
            var text = Kind switch
            {
                WelcomeCondition.Uid => UidInput.Text,
                WelcomeCondition.Guard => GuardInput.SelectedItem?.ToString(),
                _ => LevelInput.Text
            } ?? "";
            return Kind != WelcomeCondition.Guard && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture) : text;
        }
    }

    public void ShowSeparator(bool visible) => OrSeparator.IsVisible = visible;

    /// <summary>Shows why this row cannot be saved, given the UIDs of the rows above it; true when it cannot.</summary>
    public bool ShowError(ISet<long> uidsAbove)
    {
        string? error = null;
        if (CanEdit && Kind == WelcomeCondition.Uid)
        {
            if (string.IsNullOrWhiteSpace(Value)) error = "填一个 UID，或删掉这条";
            else if (!long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid) || uid <= 0)
                error = "UID 必须是正整数";
            else if (!uidsAbove.Add(uid)) error = "和上面那条重复了";
        }
        else if (CanEdit && Kind is WelcomeCondition.FanClubLevel or WelcomeCondition.WealthLevel
            && (!int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level < 1))
            error = "至少 1 级";
        ErrorMessage.Text = errorsRevealed ? error : null;
        ErrorMessage.IsVisible = errorsRevealed && error is not null;
        return error is not null;
    }

    public void FocusInput()
    {
        var input = Kind switch
        {
            WelcomeCondition.Uid => (Control)UidInput,
            WelcomeCondition.Guard => GuardInput,
            _ => LevelInput
        };
        input.Focus();
        this.BringIntoView();
    }
}
