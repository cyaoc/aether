using System.ComponentModel;
using Aether.Core;
using Avalonia.Interactivity;

namespace Aether.Gui;

public partial class GiftBoardSettingsPage : SettingsPage
{
    public event Action? GeneralSettingsRequested;
    private GiftBoardViewModel? board;

    public GiftBoardSettingsPage() : this(DataDirectory.Locate()) { }

    public GiftBoardSettingsPage(string dataDirectory) : base(dataDirectory)
    {
        InitializeComponent();
        Title = "礼物榜";
        TimingHint = "保存后立即生效";
        Bind(EnabledInput, EnabledReadOnly, null, settings => settings.GiftBoardEnabled);
        Bind(ThresholdInput, ThresholdReadOnly, ThresholdError, settings => settings.GiftBoardThreshold);
        Bind([(Settings.GiftBoardByUnitPrice, ByUnitPriceInput), (Settings.GiftBoardByTotalPrice, ByTotalPriceInput)], ThresholdByReadOnly,
            settings => settings.GiftBoardThresholdBy);
        Bind(ResetOnLiveInput, ResetOnLiveReadOnly, null, settings => settings.GiftBoardResetOnLive);
        Bind(MascotInput, MascotReadOnly, null, settings => settings.GiftBoardMascot);
        Bind(VolumeInput, VolumeReadOnly, settings => settings.GiftBoardVolume);
        PageStatus.GeneralSettingsRequested += () => GeneralSettingsRequested?.Invoke();
        LoadSettings();
    }

    protected override bool AppliesToWebService => true;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (board is not null) board.PropertyChanged -= OnBoardChanged;
        board = viewModel?.GiftBoard;
        if (board is not null) board.PropertyChanged += OnBoardChanged;
        ShowEmptyList();
    }

    private void OnBoardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GiftBoardViewModel.Enabled)) ShowEmptyList();
    }

    protected override void OnRoomStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.OnRoomStateChanged(sender, e);
        if (e.PropertyName == nameof(MainWindowViewModel.HasRoomConnection)) ShowEmptyList();
    }

    private void AskClear(object? sender, RoutedEventArgs e) => viewModel?.GiftBoard.RequestClear();

    private void AskRestore(object? sender, RoutedEventArgs e) => viewModel?.GiftBoard.RequestRestore();

    private void CancelConfirmation(object? sender, RoutedEventArgs e) => viewModel?.GiftBoard.CancelConfirmation();

    private async void Confirm(object? sender, RoutedEventArgs e)
    {
        if (viewModel is null) return;
        var room = viewModel.RoomNumber;
        var (done, error) = await viewModel.GiftBoard.ConfirmAsync();
        Message = (done, error) switch
        {
            (GiftBoardConfirmation.Clear, { } reason) => $"清空失败：{reason}",
            (GiftBoardConfirmation.Restore, { } reason) => $"恢复失败：{reason}",
            (GiftBoardConfirmation.Clear, _) => $"已清空直播间 {room} 的礼物榜",
            (GiftBoardConfirmation.Restore, _) => "已恢复上一期",
            _ => Message,
        };
    }

    // The list is empty for one of three reasons; the board being off is the switch Core applied, as the list follows it.
    private void ShowEmptyList() => BoardEmpty.Text = viewModel is not { HasRoomConnection: true } ? "连上直播间后，这里显示这个直播间的礼物榜。"
        : !viewModel.GiftBoard.Enabled ? "礼物榜关着，不记录也不显示。"
        : "还没有人上榜。达到门槛的礼物会出现在这里和直播画面上。";
}
