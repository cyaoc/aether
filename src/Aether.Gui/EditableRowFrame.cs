using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui;

public sealed class EditableRowFrame : Border
{
    private EditableRow? row;

    public EditableRowFrame()
    {
        DataContextChanged += (_, _) => WatchRow(DataContext as EditableRow);
        AttachedToVisualTree += (_, _) => WatchRow(DataContext as EditableRow);
        DetachedFromVisualTree += (_, _) => WatchRow(null);
        LostFocus += (_, _) => row?.RevealErrors();
    }

    private void WatchRow(EditableRow? current)
    {
        if (row is not null) row.FocusRequested -= FocusInput;
        row = current;
        if (row is null) return;
        row.FocusRequested += FocusInput;
        if (row.FocusPending) FocusInput();
    }

    private void FocusInput()
    {
        var requested = row;
        Dispatcher.UIThread.Post(() =>
        {
            if (requested is null || row != requested || !requested.FocusPending ||
                requested.RemoveRow is null || TopLevel.GetTopLevel(this) is null) return;
            var input = this.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control => control.IsEffectivelyVisible && control.Classes.Contains("rowFocus"));
            if (input is null) return;
            requested.FocusPending = false;
            input.Focus();
            this.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
}
