using Avalonia.Automation;
using Avalonia.Controls;

namespace Aether.Gui;

public partial class WelcomeTextRow : UserControl
{
    public event Action? Changed;
    public event Action? Editing;
    public event Action? RemoveRequested;
    public string Value => ValueInput.Text ?? "";

    public WelcomeTextRow() : this("", true) { }

    public WelcomeTextRow(string text, bool canEdit)
    {
        InitializeComponent();
        ValueInput.Text = text;
        ValueInput.IsReadOnly = !canEdit;
        RemoveButton.IsVisible = canEdit;
        ValueInput.TextChanged += (_, _) => Changed?.Invoke();
        ValueInput.GotFocus += (_, _) => Editing?.Invoke();
        RemoveButton.Click += (_, _) => { if (RemoveButton.IsEnabled && canEdit) RemoveRequested?.Invoke(); };
    }

    public void Refresh(int index, int count)
    {
        RemoveButton.IsEnabled = count > 1;
        AutomationProperties.SetName(ValueInput, $"第 {index + 1} 条欢迎语");
        AutomationProperties.SetName(RemoveButton, $"删除第 {index + 1} 条欢迎语");
    }

    public bool ShowError(string? error)
    {
        ErrorMessage.Text = error;
        ErrorMessage.IsVisible = error is not null;
        return error is not null;
    }

    public void FocusInput() => ValueInput.Focus();

    public void Insert(string token)
    {
        if (ValueInput.IsReadOnly) return;
        var text = Value;
        if (token == "{@}")
            for (var at = 0; at < text.Length; at++)
            {
                if (text.AsSpan(at).StartsWith("{{", StringComparison.Ordinal)) at++;
                else if (text.AsSpan(at).StartsWith(token, StringComparison.Ordinal)) return;
            }
        var index = token == "{@}" ? 0 : ValueInput.CaretIndex;
        ValueInput.Text = text.Insert(index, token);
        ValueInput.CaretIndex = ValueInput.SelectionStart = ValueInput.SelectionEnd = index + token.Length;
        ValueInput.Focus();
    }
}
