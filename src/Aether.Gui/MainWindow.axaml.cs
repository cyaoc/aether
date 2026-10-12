using System.Collections.Specialized;
using System.ComponentModel;
using Aether.Core;
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
    private readonly SettingsPage[] settingsPages;
    private MainWindowViewModel? observedViewModel;

    public MainWindow() : this(Aether.Core.DataDirectory.Locate()) { }

    public MainWindow(string dataDirectory)
    {
        InitializeComponent();
        this.dataDirectory = dataDirectory;
        settingsPages = Enum.GetValues<SettingsSection>().Select(section => new SettingsPage(dataDirectory, section)).ToArray();
        foreach (var page in settingsPages)
        {
            page.IsVisible = false;
            page.SaveCompleted += saved =>
            {
                RefreshSettingsStatus();
                if (saved && page.Section != SettingsSection.General && observedViewModel?.HasRoomConnection == true)
                    page.FindControl<StackPanel>("ReconnectNotice")!.IsVisible = true;
            };
            PageHost.Children.Add(page);
        }
        RefreshSettingsStatus();
        Navigation.SelectedIndex = 0;
        DataContextChanged += (_, _) =>
        {
            if (observedViewModel is not null) observedViewModel.PropertyChanged -= OnRoomStateChanged;
            observedViewModel = DataContext as MainWindowViewModel;
            if (observedViewModel is not null) observedViewModel.PropertyChanged += OnRoomStateChanged;
        };
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel viewModel) viewModel.ReadAccountCommand.Execute(null);
    }

    private void Navigate(object? sender, SelectionChangedEventArgs e)
    {
        if (settingsPages is null) return;
        for (var index = 0; index < PageHost.Children.Count; index++)
            PageHost.Children[index].IsVisible = index == Navigation.SelectedIndex;
        RefreshSettingsStatus();
        if (Navigation.SelectedIndex == 0) QueueScrollToLatest();
    }

    private void RefreshSettingsStatus()
    {
        var current = Settings.Load(dataDirectory);
        // An invalid file has no usable saved values to advertise.
        BlindBoxStatus.Text = current.Error is null ? (current.BlindBoxEnabled.Value ? "开" : "关") : "";
        WelcomeStatus.Text = current.Error is null ? (current.WelcomeEnabled.Value ? "开" : "关") : "";
        foreach (var page in settingsPages) page.RefreshFileStatus(current);
    }

    private void OnRoomStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.ConnectionState) || observedViewModel?.HasRoomConnection == true) return;
        foreach (var page in settingsPages) page.FindControl<StackPanel>("ReconnectNotice")!.IsVisible = false;
    }

    private void ConnectRoom(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainWindowViewModel viewModel) return;
        e.Handled = true;
        if (viewModel.ConnectCommand.CanExecute(null)) viewModel.ConnectCommand.Execute(null);
    }

    private async void ConfirmLogout(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.IsLoggingOut) return;
        // Closing the window meanwhile has already started shutdown; a late confirm must not log out under it.
        if (await new ConfirmLogoutWindow().ShowDialog<bool>(this) && !closing) await viewModel.LogoutCommand.ExecuteAsync(null);
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
