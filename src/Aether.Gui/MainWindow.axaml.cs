using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui;

public partial class MainWindow : Window
{
    private bool closing;
    private bool canClose;
    private bool followLatest = true;
    private bool scrollPending;
    private readonly string dataDirectory;
    private SettingsWindow? settingsWindow;

    public MainWindow() : this(Aether.Core.DataDirectory.Locate()) { }

    public MainWindow(string dataDirectory)
    {
        InitializeComponent();
        this.dataDirectory = dataDirectory;
        DanmakuList.Items.CollectionChanged += OnDanmakuChanged;
        DanmakuList.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        DanmakuList.AddHandler(PointerWheelChangedEvent, (_, _) => PauseFollowingForInput(), RoutingStrategies.Tunnel);
        DanmakuList.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual visual &&
                visual.GetSelfAndVisualAncestors().OfType<ScrollBar>().Any()) PauseFollowingForInput();
        }, RoutingStrategies.Tunnel);
        DanmakuList.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                PauseFollowingForInput();
        }, RoutingStrategies.Tunnel);
    }

    private void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(dataDirectory);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show(this);
    }

    private void PauseFollowingForInput()
    {
        followLatest = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (DanmakuList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } viewer)
                followLatest = IsAtBottom(viewer);
        }, DispatcherPriority.Loaded);
    }

    private static bool IsAtBottom(ScrollViewer viewer) =>
        viewer.Offset.Y >= viewer.Extent.Height - viewer.Viewport.Height - 1;

    private void OnDanmakuChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DanmakuList.ItemCount == 0) followLatest = true;
        QueueScrollToLatest();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer viewer) return;
        if (!followLatest)
        {
            followLatest = IsAtBottom(viewer);
            return;
        }
        if (scrollPending) return;
        // Content/layout growth moves the bottom without expressing the user's scroll intent.
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            QueueScrollToLatest();
            return;
        }
        if (e.OffsetDelta.Y != 0)
            followLatest = IsAtBottom(viewer);
    }

    private void QueueScrollToLatest()
    {
        if (!followLatest || scrollPending || DanmakuList.ItemCount == 0) return;
        scrollPending = true;
        // Wait for collection changes and virtualization layout before locating the last item.
        Dispatcher.UIThread.Post(() =>
        {
            var latest = DanmakuList.ItemCount > 0 ? DanmakuList.Items[^1] : null;
            if (followLatest && DanmakuList.ItemCount > 0)
                DanmakuList.ScrollIntoView(DanmakuList.ItemCount - 1);
            // ScrollIntoView can trigger another virtualization layout. Its offset correction
            // must not be mistaken for the user leaving the bottom.
            Dispatcher.UIThread.Post(() =>
            {
                scrollPending = false;
                if (DanmakuList.ItemCount > 0 && !ReferenceEquals(latest, DanmakuList.Items[^1]))
                    QueueScrollToLatest();
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);
    }

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
