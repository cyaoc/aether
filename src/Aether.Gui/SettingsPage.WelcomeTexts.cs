using Aether.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class SettingsPage
{
    private WelcomeTextRow? editedWelcomeText;
    private IEnumerable<WelcomeTextRow> WelcomeTexts => WelcomeTextRows.Children.Cast<WelcomeTextRow>();

    private void LoadWelcomeTexts()
    {
        WelcomeTextRows.Children.Clear();
        WelcomeTextFold.IsExpanded = false;
        foreach (var text in settings.WelcomeTexts.Value) InsertWelcomeText(text);
        editedWelcomeText = WelcomeTexts.FirstOrDefault();
        WelcomeTextReadOnly.IsVisible = !settings.WelcomeTexts.CanEdit;
        AddWelcomeTextButton.IsVisible = settings.WelcomeTexts.CanEdit;
        foreach (var button in new[] { InsertAtButton, InsertNicknameButton, InsertGuardButton })
            button.IsEnabled = settings.WelcomeTexts.CanEdit;
        RefreshWelcomeTexts();
    }

    private WelcomeTextRow InsertWelcomeText(string text, bool isNew = false)
    {
        var row = new WelcomeTextRow(text, settings.WelcomeTexts.CanEdit, isNew);
        WelcomeTextRows.Children.Add(row);
        row.Changed += Validate;
        row.Editing += () => { editedWelcomeText = row; RefreshWelcomeTexts(); };
        row.RemoveRequested += () =>
        {
            var index = WelcomeTextRows.Children.IndexOf(row);
            WelcomeTextRows.Children.Remove(row);
            if (editedWelcomeText == row)
                editedWelcomeText = WelcomeTexts.ElementAt(Math.Min(index, WelcomeTextRows.Children.Count - 1));
            RefreshWelcomeTexts();
            Validate();
        };
        return row;
    }

    /// <summary>Updates what depends on which rows exist: the count, the hints, the insert target and each row's ×.</summary>
    private void RefreshWelcomeTexts()
    {
        var count = WelcomeTextRows.Children.Count;
        WelcomeTextCount.Text = $"{count} 条";
        WelcomeRandomHint.Text = count == 1 ? "写多条时，每次随机发一条" : "每次随机发一条";
        WelcomeInsertTarget.IsVisible = count > 1 && settings.WelcomeTexts.CanEdit;
        WelcomeInsertTarget.Text = $"到第 {WelcomeTextRows.Children.IndexOf(editedWelcomeText!) + 1} 条";
        var index = 0;
        foreach (var row in WelcomeTexts) row.Refresh(index++, count);
        RefreshWelcomeTextFold();
    }

    private void RefreshWelcomeTextFold() => WelcomeTextFold.ApplyTo(WelcomeTexts, row => row.HasVisibleError, "条");

    private void AddWelcomeText(object? sender, RoutedEventArgs e)
    {
        if (!settings.WelcomeTexts.CanEdit) return;
        var row = InsertWelcomeText("", isNew: true);
        editedWelcomeText = row;
        WelcomeTextFold.IsExpanded = true;
        RefreshWelcomeTexts();
        Validate();
        Dispatcher.UIThread.Post(row.FocusInput, DispatcherPriority.Loaded);
    }

    private void InsertPlaceholder(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token } && editedWelcomeText is { } row)
        {
            var hidden = !row.IsVisible;
            if (hidden)
            {
                WelcomeTextFold.IsExpanded = true;
                RefreshWelcomeTextFold();
            }
            row.Insert(token);
            if (hidden) Dispatcher.UIThread.Post(row.FocusInput, DispatcherPriority.Loaded);
        }
        Validate();
    }

    /// <summary>A click on an insert button leaves focus in the row, so inserting does not count as leaving a new row.</summary>
    private void InsertButtonGettingFocus(object? sender, FocusChangingEventArgs e)
    {
        if (e.NavigationMethod == NavigationMethod.Pointer) e.TryCancel();
    }

    /// <summary>Shows each row's error, checked as a one-text list; true when any row cannot be saved.</summary>
    private bool ShowWelcomeTextErrors()
    {
        var invalid = false;
        foreach (var row in WelcomeTexts)
        {
            var error = !settings.WelcomeTexts.CanEdit ? null
                : string.IsNullOrWhiteSpace(row.Value) ? "填上欢迎语，或删掉这条"
                : Settings.Validate(settings.WelcomeTexts.Name, Settings.TextListText([row.Value]));
            invalid |= row.ShowError(error);
        }
        RefreshWelcomeTextFold();
        return invalid;
    }
}
