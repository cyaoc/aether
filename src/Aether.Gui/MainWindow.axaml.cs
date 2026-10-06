using Avalonia.Controls;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class MainWindow : Window
{
    private bool closing;
    private bool canClose;

    public MainWindow() => InitializeComponent();

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || canClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        if (DataContext is MainWindowViewModel viewModel) await viewModel.ShutdownAsync();
        canClose = true;
        Dispatcher.UIThread.Post(Close);
    }
}
