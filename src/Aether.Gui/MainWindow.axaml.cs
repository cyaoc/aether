using System.Collections.Specialized;
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

    public MainWindow() : this(Aether.Core.DataDirectory.Locate()) { }

    public MainWindow(string dataDirectory)
    {
        InitializeComponent();
        this.dataDirectory = dataDirectory;
        settingsPages = Enum.GetValues<SettingsSection>().Select(section => new SettingsPage(dataDirectory, section)).ToArray();
        foreach (var page in settingsPages)
        {
            page.IsVisible = false;
            page.SaveCompleted += RefreshSettingsStatus;
            page.GeneralSettingsRequested += () => Navigation.SelectedIndex = 6;
            PageHost.Children.Add(page);
        }
        PageHost.Children.Insert(4, new LotteryPage { IsVisible = false });
        RefreshSettingsStatus();
        Navigation.SelectedIndex = 0;
        // Check before ComboBox handles Enter: selecting an item closes its popup during the same event.
        RoomInput.AddHandler(KeyDownEvent, OnRoomInputKeyDown, RoutingStrategies.Tunnel);
        RoomInput.DropDownOpened += (_, _) => MoveRoomCaretToEnd();
        RoomInput.DropDownClosed += (_, _) => MoveRoomCaretToEnd();
        // No history, no ▾: stop the click (here) and keys (OnRoomInputKeyDown) that would open an empty list,
        // instead of closing a native popup after it has already appeared.
        RoomInput.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (RoomInput.ItemCount == 0 && e.Source is Visual source &&
                !source.GetSelfAndVisualAncestors().OfType<TextBox>().Any()) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        RoomInput.PropertyChanged += (_, e) =>
        {
            if (e.Property != ItemsControl.ItemCountProperty) return;
            // Deleting the room in the box clears and restores its Text, which leaves the caret at the start.
            MoveRoomCaretToEnd();
            if (RoomInput.ItemCount == 0) RoomInput.IsDropDownOpen = false;
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
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.StartWebServiceAsync(dataDirectory);
            viewModel.ReadAccountCommand.Execute(null);
        }
    }

    private async void OpenSettingsWithoutLogin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        await viewModel.OpenSettingsCommand.ExecuteAsync(null);
        Navigation.SelectedIndex = 6;
    }

    private void Navigate(object? sender, SelectionChangedEventArgs e)
    {
        if (settingsPages is null) return;
        // Navigation items and PageHost children share one order, with 抽奖 after 欢迎动画.
        for (var index = 0; index < PageHost.Children.Count; index++)
            PageHost.Children[index].IsVisible = index == Navigation.SelectedIndex;
        RefreshSettingsStatus();
        if (Navigation.SelectedIndex == 0) QueueScrollToLatest();
    }

    private void RefreshSettingsStatus()
    {
        var current = Settings.Load(dataDirectory);
        // An invalid file has no usable saved values to advertise.
        ShowEnabled(BlindBoxStatus, current.Error is null ? current.BlindBoxEnabled.Value : null);
        ShowEnabled(WelcomeStatus, current.Error is null ? current.WelcomeEnabled.Value : null);
        ShowEnabled(AnimationStatus, current.Error is null ? current.WelcomeAnimationEnabled.Value : null);
        if (IsVisible && DataContext is MainWindowViewModel viewModel) _ = viewModel.RefreshWebServiceAsync(current);
        foreach (var page in settingsPages) page.RefreshFileStatus(current);
    }

    private static void ShowEnabled(TextBlock status, bool? enabled)
    {
        status.Text = enabled switch { true => "开", false => "关", null => "" };
        status.Classes.Set("success", enabled == true);
        status.Classes.Set("muted", enabled != true);
    }

    private void OnRoomInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        // The keys ComboBox opens on; a handled Alt+F4 would stop Windows from closing the window.
        if (RoomInput.ItemCount == 0 && (e.Key == Key.F4 && !alt || alt && e.Key is Key.Up or Key.Down))
        {
            e.Handled = true;
            return;
        }
        if (RoomInput.IsDropDownOpen)
        {
            if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.Shift)
            {
                if (RoomInput.SelectedItem is string room) viewModel.RemoveRecentRoomCommand.Execute(room);
                e.Handled = true;
            }
            // Editable ComboBox keeps focus in its TextBox, which consumes arrow keys before list navigation.
            else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down)
            {
                if (RoomInput.ItemCount > 0)
                    RoomInput.SelectedIndex = Math.Clamp(RoomInput.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, RoomInput.ItemCount - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                RoomInput.IsDropDownOpen = false;
                e.Handled = true;
            }
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (viewModel.ConnectCommand.CanExecute(null)) viewModel.ConnectCommand.Execute(null);
    }

    private void MoveRoomCaretToEnd() => Dispatcher.UIThread.Post(() =>
    {
        // Deleting the last item closes the popup before its Text binding has finished updating.
        if (RoomInput.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } text)
        {
            // Reset both ends: temporary empty Text can coerce only the presenter's selection to zero.
            text.SelectionStart = text.SelectionEnd = 0;
            text.SelectionStart = text.SelectionEnd = text.CaretIndex = text.Text?.Length ?? 0;
        }
    });

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
        // Wait for virtualization layout, then include the list's trailing padding in the scroll.
        Dispatcher.UIThread.Post(() =>
        {
            var latest = DanmakuList.ItemCount > 0 ? DanmakuList.Items[^1] : null;
            if (followLatest && DanmakuList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } viewer)
                viewer.ScrollToEnd();
            // Virtualization's offset correction must not be mistaken for user input.
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
        await Task.WhenAll(settingsPages.Select(page => page.PendingSave));
        if (DataContext is MainWindowViewModel viewModel) await viewModel.ShutdownAsync();
        canClose = true;
        Dispatcher.UIThread.Post(Close);
    }
}
