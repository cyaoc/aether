using System.ComponentModel;
using System.Globalization;
using Aether.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace Aether.Gui;

public sealed record OnlineRankRow(int Rank, OnlineRankViewer Viewer);

public partial class OnlineRankPicker : UserControl
{
    public static readonly StyledProperty<MainWindowViewModel?> HostProperty = AvaloniaProperty.Register<OnlineRankPicker, MainWindowViewModel?>(nameof(Host));
    public static readonly StyledProperty<TextBox?> TargetProperty = AvaloniaProperty.Register<OnlineRankPicker, TextBox?>(nameof(Target));
    public static readonly StyledProperty<string> AccessibleNameProperty = AvaloniaProperty.Register<OnlineRankPicker, string>(nameof(AccessibleName), "从在线榜选");
    public static readonly StyledProperty<string> DisabledReasonProperty = AvaloniaProperty.Register<OnlineRankPicker, string>(nameof(DisabledReason), "此项只读，不能从在线榜选");
    public static readonly StyledProperty<string> FillTargetTextProperty = AvaloniaProperty.Register<OnlineRankPicker, string>(nameof(FillTargetText), "左边");
    public MainWindowViewModel? Host { get => GetValue(HostProperty); set => SetValue(HostProperty, value); }
    public TextBox? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public string AccessibleName { get => GetValue(AccessibleNameProperty); set => SetValue(AccessibleNameProperty, value); }
    public string DisabledReason { get => GetValue(DisabledReasonProperty); set => SetValue(DisabledReasonProperty, value); }
    public string FillTargetText { get => GetValue(FillTargetTextProperty); set => SetValue(FillTargetTextProperty, value); }
    public event EventHandler<OnlineRankViewer>? Selected;
    private readonly LatestRun read = new();
    private Flyout PickerFlyout => (Flyout)PickerButton.Flyout!;

    public OnlineRankPicker()
    {
        InitializeComponent();
        PickerFlyout.Opened += async (_, _) =>
        {
            if (!PickerButton.IsEnabled) { PickerFlyout.Hide(); return; }
            PickerButton.Classes.Add("open");
            await read.Start(ReadAsync);
        };
        PickerFlyout.Closed += (_, _) => { PickerButton.Classes.Remove("open"); _ = read.StopAsync(); };
        FlyoutContent.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { PickerFlyout.Hide(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        RankList.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        RankList.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Visual visual &&
                visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().Any()) Choose();
        };
        DetachedFromVisualTree += (_, _) => { PickerFlyout.Hide(); _ = read.StopAsync(); Unsubscribe(); };
        AttachedToVisualTree += (_, _) => { Subscribe(); Refresh(); };
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HostProperty)
        {
            if (change.OldValue is MainWindowViewModel old) old.PropertyChanged -= HostChanged;
            if (change.NewValue is MainWindowViewModel next) { next.PropertyChanged -= HostChanged; next.PropertyChanged += HostChanged; }
        }
        if (change.Property == TargetProperty)
        {
            if (change.OldValue is TextBox old) old.PropertyChanged -= TargetChanged;
            if (change.NewValue is TextBox next) { next.PropertyChanged -= TargetChanged; next.PropertyChanged += TargetChanged; }
        }
        if (PickerButton is not null) Refresh();
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (Host is { } host) host.PropertyChanged += HostChanged;
        if (Target is { } target) target.PropertyChanged += TargetChanged;
    }

    private void Unsubscribe()
    {
        if (Host is { } host) host.PropertyChanged -= HostChanged;
        if (Target is { } target) target.PropertyChanged -= TargetChanged;
    }

    private void HostChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsConnected)) Refresh();
    }
    private void TargetChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.IsReadOnlyProperty || e.Property == IsEnabledProperty) Refresh();
    }

    private void Refresh()
    {
        PickerButton.IsEnabled = Host?.IsConnected == true && Target is { IsReadOnly: false, IsEnabled: true };
        ToolTip.SetTip(PickerButton, Host?.IsConnected != true ? "连上直播间后才能选"
            : !PickerButton.IsEnabled ? DisabledReason : "从在线榜选");
        Footer.Text = $"每次打开都重新读；点一位观众，把 UID 填到{FillTargetText}";
        if (!PickerButton.IsEnabled) PickerFlyout.Hide();
    }

    private async Task ReadAsync(CancellationToken token)
    {
        Loading.IsVisible = true;
        RankList.IsVisible = MessagePanel.IsVisible = false;
        RankList.ItemsSource = null;
        RankSummary.Text = "";
        Dispatcher.UIThread.Post(() =>
        {
            if (!token.IsCancellationRequested && PickerFlyout.IsOpen && !RankList.IsVisible) FlyoutContent.Focus();
        }, DispatcherPriority.Loaded);
        try
        {
            var result = await Host!.GetOnlineRankAsync(token);
            if (token.IsCancellationRequested) return;
            Loading.IsVisible = false;
            if (result.Viewers.Count == 0)
            {
                ShowMessage("在线榜上还没有人", "没开播时一般是空的；也可以直接填 UID", false);
                return;
            }
            RankSummary.Text = $"前 {result.Viewers.Count} 位 · 在线 {result.Count} 人";
            var rows = result.Viewers.Select((viewer, index) => new OnlineRankRow(index + 1, viewer)).ToArray();
            RankList.ItemsSource = rows;
            RankList.IsVisible = true;
            RankList.SelectedItem = Viewer.TryParseUid(Target?.Text, out var uid) ? rows.FirstOrDefault(row => row.Viewer.Uid == uid) : null;
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested || !PickerFlyout.IsOpen) return;
                (RankList.ContainerFromIndex(RankList.SelectedIndex) ?? (Control)RankList).Focus();
            }, DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) ShowMessage($"读不到在线榜：{error.Message}", "关掉再点一次会重新读；也可以直接填 UID", true);
        }
    }

    private void ShowMessage(string message, string hint, bool error)
    {
        Loading.IsVisible = false;
        MessagePanel.IsVisible = true;
        MessageText.Text = message;
        MessageText.Classes.Set("error", error);
        MessageText.Classes.Set("secondary", !error);
        MessageHint.Text = hint;
    }

    private void Choose()
    {
        if (!PickerButton.IsEnabled || Target is not { } target || RankList.SelectedItem is not OnlineRankRow row) return;
        target.SetCurrentValue(TextBox.TextProperty, row.Viewer.Uid.ToString(CultureInfo.InvariantCulture));
        Selected?.Invoke(this, row.Viewer);
        PickerFlyout.Hide();
    }
}
