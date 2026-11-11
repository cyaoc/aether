using Avalonia.Controls;
using Avalonia.Input;

namespace Aether.Gui;

/// <summary>Lines edited one per row, whose insert buttons put a placeholder into the line being edited: the one whose text box had
/// focus last, or, once it is deleted, the one now in its place. The welcome texts and the 桌宠's lines.</summary>
internal sealed class LineInsertTarget
{
    private readonly Action changed;

    /// <param name="lines">The list the lines' text boxes are in.</param>
    /// <param name="changed">Shows which line the buttons now fill.</param>
    public LineInsertTarget(ItemsControl lines, Action changed)
    {
        this.changed = changed;
        lines.GotFocus += (_, e) =>
        {
            // Only the text box picks the line; pressing another line's × must not retarget the insert buttons.
            if (e.Source is not TextBox { DataContext: WelcomeTextRow row }) return;
            Row = row;
            changed();
        };
    }

    /// <summary>The line the insert buttons fill.</summary>
    public WelcomeTextRow? Row { get; set; }

    /// <summary>Keeps the target among <paramref name="rows"/> as lines are deleted.</summary>
    public void Follow(EditableRows<WelcomeTextRow> rows) => rows.CollectionChanged += (_, e) =>
    {
        if (e.OldItems?.Contains(Row) == true) Row = rows.ElementAtOrDefault(Math.Min(e.OldStartingIndex, rows.Count - 1));
        changed();
    };

    /// <summary>A click on an insert button leaves focus in the line, so inserting does not count as leaving a new line.</summary>
    public static void KeepFocus(FocusChangingEventArgs e)
    {
        if (e.NavigationMethod == NavigationMethod.Pointer) e.TryCancel();
    }
}
