using Aether.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aether.Gui;

public partial class NewLotteryWindow : Window
{
    public NewLotteryWindow() => InitializeComponent();
    private void Cancel(object? sender, RoutedEventArgs e) => Close(null);
    private void Next(object? sender, RoutedEventArgs e) => Close((LotteryMode?)(EntryMode.IsChecked == true ? LotteryMode.Entry : LotteryMode.Pool));
}
