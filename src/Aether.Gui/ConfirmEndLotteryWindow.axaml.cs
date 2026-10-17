using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Gui;

public partial class ConfirmEndLotteryWindow : Window
{
    public ConfirmEndLotteryWindow() => InitializeComponent();
    private void Cancel(object? sender, RoutedEventArgs e) => Close(false);
    private void Confirm(object? sender, RoutedEventArgs e) => Close(true);
}
