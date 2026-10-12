using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Gui;

/// <summary>Closes with true only when the user confirms logging out.</summary>
public partial class ConfirmLogoutWindow : Window
{
    public ConfirmLogoutWindow() => InitializeComponent();

    private void Cancel(object? sender, RoutedEventArgs e) => Close(false);

    private void Confirm(object? sender, RoutedEventArgs e) => Close(true);
}
