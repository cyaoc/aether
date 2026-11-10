using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.Gui;

/// <summary>A kind of line in the 台词 tab's list, and its lines as the streamer edits them, read once the kind is opened.</summary>
/// <param name="changed">Whether the saved lines differ from the character's.</param>
public sealed partial class DesktopPetLineKind(LineKind kind, bool changed, bool canEdit) : ObservableObject
{
    public LineKind Kind => kind;
    public bool CanEdit => canEdit;
    /// <summary>Its lines differ from the character's: 已改.</summary>
    [ObservableProperty] private bool changed = changed;
    public bool Opened { get; private set; }
    /// <summary>Empty until the kind is opened.</summary>
    public EditableRows<WelcomeTextRow> Rows { get; } = new(1);
    public IEnumerable<string> Lines => Rows.Select(row => row.Value);

    /// <summary>Fills the rows with <paramref name="saved"/> the first time.</summary>
    public void Open(IReadOnlyList<string> saved)
    {
        if (Opened) return;
        Opened = true;
        Fill(saved);
    }

    public void Fill(IEnumerable<string> lines)
    {
        Rows.Clear();
        foreach (var line in lines) Rows.Add(new(line, canEdit));
    }
}
