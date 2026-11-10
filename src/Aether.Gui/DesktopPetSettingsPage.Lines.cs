using Aether.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Aether.Gui;

/// <summary>The 台词 tab: her kinds of lines by group, one opened at a time, edited like the welcome texts; the file keeps only the
/// kinds that differ from the character's own.</summary>
public partial class DesktopPetSettingsPage
{
    private DesktopPetLineKind[] lineKinds = [];
    private readonly LineInsertTarget lineTarget;

    private void BuildLineKinds()
    {
        LineKindList.ContainerPrepared += (_, e) =>
        {
            // A group's heading only names the kinds under it.
            var heading = LineKindList.Items[e.Index] is ShowGroup;
            e.Container.IsEnabled = !heading;
            e.Container.Classes.Set("heading", heading);
        };
        LineKindList.SelectionChanged += (_, _) => OpenLineKind();
    }

    private void LoadLines()
    {
        var changed = settings.DesktopPetChangedLines;
        var selected = LineKindList.SelectedIndex;
        lineKinds = [.. shows.Lines.Select(kind => new DesktopPetLineKind(kind, changed.ContainsKey(kind.Id), changed.GetValueOrDefault(kind.Id)?.CanEdit ?? true))];
        foreach (var kind in lineKinds)
        {
            lineTarget.Follow(kind.Rows);
            kind.Rows.RowChanged += Validate;
        }
        object[] items = [.. shows.Groups.SelectMany(group => lineKinds.Where(kind => kind.Kind.Group == group.Id).ToArray() is { Length: > 0 } kinds
            ? kinds.Prepend<object>(group) : [])];
        LineKindList.ItemsSource = items;
        LineKindList.SelectedItem = selected >= 0 ? items[selected] : items.OfType<DesktopPetLineKind>().First();
    }

    private void OpenLineKind()
    {
        if (LineKindList.SelectedItem is not DesktopPetLineKind kind) return;
        kind.Open(settings.DesktopPetLines(Character.Current)[kind.Kind.Id]);
        LineRows.ItemsSource = kind.Rows;
        lineTarget.Row = kind.Rows.FirstOrDefault();
        LineKindTitle.Text = kind.Kind.Label;
        LineKindNote.Text = kind.Kind.Note + "；每次随机一句，不连着重复";
        PlaceholderButtons.ItemsSource = kind.Kind.Placeholders;
        InsertBar.IsVisible = kind.Kind.Placeholders.Count > 0;
        NoPlaceholders.IsVisible = kind.Kind.Placeholders.Count == 0;
        LinesReadOnly.IsVisible = !kind.CanEdit;
        AddLineButton.IsVisible = kind.CanEdit;
        RefreshInsertTarget();
        Validate();
    }

    /// <summary>Marks each opened kind's lines that cannot be saved and whether it differs from hers; why the page cannot be saved, or
    /// null.</summary>
    private string? CheckLines()
    {
        string? error = null;
        foreach (var kind in lineKinds.Where(kind => kind.Opened))
        {
            kind.Changed = !kind.Lines.SequenceEqual(Character.Current.Lines[kind.Kind.Id]);
            foreach (var row in kind.Rows) row.Error = kind.CanEdit ? kind.Kind.Check([row.Value]) : null;
            if (error is null && kind.Rows.Any(row => row.Error is not null)) error = $"{kind.Kind.Label}的台词有错，改好后才能保存";
        }
        RestoreButton.IsEnabled = LineKindList.SelectedItem is DesktopPetLineKind { Changed: true, CanEdit: true };
        return error;
    }

    /// <summary>The opened kinds whose lines differ from the saved ones.</summary>
    private Dictionary<string, IReadOnlyList<string>> EditedLines()
    {
        var saved = settings.DesktopPetLines(Character.Current);
        return lineKinds.Where(kind => kind.Opened && !kind.Lines.SequenceEqual(saved[kind.Kind.Id]))
            .ToDictionary(kind => kind.Kind.Id, kind => (IReadOnlyList<string>)[.. kind.Lines]);
    }

    private void RefreshInsertTarget()
    {
        InsertTarget.Text = LineRows.ItemsSource is EditableRows<WelcomeTextRow> { Count: > 1 } rows && lineTarget.Row is { } row
            ? $"到第 {rows.IndexOf(row) + 1} 句" : "";
    }

    private void InsertPlaceholder(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name }) lineTarget.Row?.Insert("{" + name + "}");
    }

    private void InsertButtonGettingFocus(object? sender, FocusChangingEventArgs e) => LineInsertTarget.KeepFocus(e);

    private void AddLine(object? sender, RoutedEventArgs e)
    {
        if (LineKindList.SelectedItem is not DesktopPetLineKind kind) return;
        lineTarget.Row = new("", kind.CanEdit, isNew: true);
        kind.Rows.AddNew(lineTarget.Row);
    }

    private void RestoreLines(object? sender, RoutedEventArgs e)
    {
        if (LineKindList.SelectedItem is not DesktopPetLineKind kind) return;
        kind.Fill(Character.Current.Lines[kind.Kind.Id]);
        lineTarget.Row = kind.Rows.FirstOrDefault();
        RefreshInsertTarget();
    }
}
