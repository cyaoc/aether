using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

public partial class WelcomeTextRow(string text, bool canEdit, bool isNew = false) : EditableRow(isNew, canEdit)
{
    private string value = text;
    public string Value { get => value; set => SetInput(ref this.value, value ?? ""); }
    [ObservableProperty] private int caretIndex;
    [ObservableProperty] private int selectionStart;
    [ObservableProperty] private int selectionEnd;

    public void Insert(string token)
    {
        if (!CanEdit) return;
        var text = Value;
        if (token == "{@}")
            for (var at = 0; at < text.Length; at++)
            {
                if (text.AsSpan(at).StartsWith("{{", StringComparison.Ordinal)) at++;
                else if (text.AsSpan(at).StartsWith(token, StringComparison.Ordinal)) return;
            }
        var index = token == "{@}" ? 0 : CaretIndex;
        Value = text.Insert(index, token);
        CaretIndex = SelectionStart = SelectionEnd = index + token.Length;
        RequestFocus();
    }
}
