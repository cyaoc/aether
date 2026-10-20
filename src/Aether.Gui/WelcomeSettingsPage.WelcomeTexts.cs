using Aether.Core;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Aether.Gui;

public partial class WelcomeSettingsPage
{
    private WelcomeTextRow? editedWelcomeText;
    internal EditableRows<WelcomeTextRow> WelcomeTexts { get; } = new(1);

    private void LoadWelcomeTexts()
    {
        WelcomeTexts.Clear();
        WelcomeTextFold.IsExpanded = false;
        foreach (var text in settings.WelcomeTexts.Value) WelcomeTexts.Add(new(text, settings.WelcomeTexts.CanEdit));
        editedWelcomeText = WelcomeTexts.FirstOrDefault();
        WelcomeTextReadOnly.IsVisible = !settings.WelcomeTexts.CanEdit;
        AddWelcomeTextButton.IsVisible = settings.WelcomeTexts.CanEdit;
        foreach (var button in new[] { InsertAtButton, InsertNicknameButton, InsertGuardButton })
            button.IsEnabled = settings.WelcomeTexts.CanEdit;
        RefreshWelcomeTexts();
    }

    private void WelcomeTextsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems?.Contains(editedWelcomeText) == true)
            editedWelcomeText = WelcomeTexts.ElementAtOrDefault(Math.Min(e.OldStartingIndex, WelcomeTexts.Count - 1));
        RefreshWelcomeTexts();
    }

    private void WelcomeTextGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control { DataContext: WelcomeTextRow row })
        {
            editedWelcomeText = row;
            RefreshWelcomeTexts();
        }
    }

    /// <summary>Updates what depends on which rows exist: the count, the hints, the insert target and each row's ×.</summary>
    private void RefreshWelcomeTexts()
    {
        var count = WelcomeTexts.Count;
        WelcomeTextCount.Text = $"{count} 条";
        WelcomeRandomHint.Text = count == 1 ? "写多条时，每次随机发一条" : "每次随机发一条";
        WelcomeInsertTarget.IsVisible = count > 1 && settings.WelcomeTexts.CanEdit;
        WelcomeInsertTarget.Text = $"到第 {WelcomeTexts.IndexOf(editedWelcomeText!) + 1} 条";
        RefreshWelcomeTextFold();
    }

    private void RefreshWelcomeTextFold() => WelcomeTextFold.ApplyTo(WelcomeTexts, "条");

    private void AddWelcomeText(object? sender, RoutedEventArgs e)
    {
        if (!settings.WelcomeTexts.CanEdit) return;
        var row = new WelcomeTextRow("", settings.WelcomeTexts.CanEdit, isNew: true);
        editedWelcomeText = row;
        WelcomeTextFold.IsExpanded = true;
        WelcomeTexts.AddNew(row);
        RefreshWelcomeTexts();
    }

    private void InsertPlaceholder(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token } && editedWelcomeText is { } row)
        {
            if (!row.IsShown)
            {
                WelcomeTextFold.IsExpanded = true;
                RefreshWelcomeTextFold();
            }
            row.Insert(token);
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
            row.Error = error;
            invalid |= error is not null;
        }
        RefreshWelcomeTextFold();
        return invalid;
    }
}
