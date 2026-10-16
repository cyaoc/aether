using Aether.Core;
using Avalonia.Controls;
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
        foreach (var text in settings.WelcomeTexts.Value) InsertWelcomeText(text);
        editedWelcomeText = WelcomeTexts.FirstOrDefault();
        WelcomeTextReadOnly.IsVisible = !settings.WelcomeTexts.CanEdit;
        AddWelcomeTextButton.IsVisible = settings.WelcomeTexts.CanEdit;
        foreach (var button in new[] { InsertAtButton, InsertNicknameButton, InsertGuardButton })
            button.IsEnabled = settings.WelcomeTexts.CanEdit;
        RefreshWelcomeTexts();
    }

    private WelcomeTextRow InsertWelcomeText(string text)
    {
        var row = new WelcomeTextRow(text, settings.WelcomeTexts.CanEdit);
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

    private void RefreshWelcomeTexts()
    {
        var count = WelcomeTextRows.Children.Count;
        WelcomeTextCount.Text = $"{count} 条";
        WelcomeRandomHint.Text = count == 1 ? "写多条时，每次随机发一条" : "每次随机发一条";
        WelcomeInsertTarget.IsVisible = count > 1;
        WelcomeInsertTarget.Text = $"到第 {WelcomeTextRows.Children.IndexOf(editedWelcomeText!) + 1} 条";
        var index = 0;
        foreach (var row in WelcomeTexts) row.Refresh(index++, count);
    }

    private void AddWelcomeText(object? sender, RoutedEventArgs e)
    {
        if (!settings.WelcomeTexts.CanEdit) return;
        var row = InsertWelcomeText("");
        editedWelcomeText = row;
        RefreshWelcomeTexts();
        Validate();
        Dispatcher.UIThread.Post(row.FocusInput, DispatcherPriority.Loaded);
    }

    private void InsertPlaceholder(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token }) editedWelcomeText?.Insert(token);
        Validate();
    }

    private bool ShowWelcomeTextErrors()
    {
        var invalid = false;
        foreach (var row in WelcomeTexts) invalid |= row.ShowError();
        return invalid;
    }
}
