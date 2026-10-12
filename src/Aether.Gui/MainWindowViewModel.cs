using System.Collections.ObjectModel;
using Aether.Core;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Aether.Gui;

public enum RoomConnectionState { Disconnected, Connecting, Connected, Reconnecting }

/// <summary>What the sidebar shows for the account; Account is set only when a read finds one.</summary>
public sealed record AccountStatus(string Text, Account? Account = null)
{
    public static AccountStatus Reading { get; } = new("读取中");
    public static AccountStatus LoggedOut { get; } = new("未登录");
    public static AccountStatus Failed { get; } = new("账号信息读取失败");
    public static AccountStatus Of(Account? account) => account is null ? LoggedOut : new(account.Nickname, account);
    public bool ReadFailed => this == Failed;
}

public partial class MainWindowViewModel(AetherClient client, ILogger<MainWindowViewModel> logger, GuiSettings settings,
    TimeProvider clock) : ObservableObject
{
    [ObservableProperty] private bool keepRecentDanmaku = settings.KeepRecentDanmaku;
    [ObservableProperty] private string roomNumber = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(HasRoomConnection))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private RoomConnectionState connectionState = RoomConnectionState.Disconnected;
    private long? connectedAt;
    private ITimer? connectionTimer;

    [ObservableProperty] private string message = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQrCode))]
    private Bitmap? qrCode;

    // A room connection started mid-logout would load the credential that logout is deleting.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private bool isLoggingOut;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private bool isRestarting;
    private bool preserveDanmaku;
    private string? activeRoomNumber;

    [ObservableProperty] private AccountStatus accountStatus = AccountStatus.Reading;
    private CancellationTokenSource? accountReadStop;
    private Task accountReadTask = Task.CompletedTask;
    private bool shuttingDown;

    private bool CanConnect => !IsLoggingOut && !IsRestarting && !shuttingDown;
    private bool CanReconnect => CanConnect && HasRoomConnection;
    public bool IsConnected => ConnectionState == RoomConnectionState.Connected;
    public bool HasRoomConnection => ConnectionState != RoomConnectionState.Disconnected;
    public string Status => ConnectionState switch
    {
        RoomConnectionState.Disconnected => "未连接",
        RoomConnectionState.Connecting => "连接中",
        RoomConnectionState.Connected => $"已连接 · {(connectedAt is { } start ? (int)clock.GetElapsedTime(start).TotalMinutes : 0)} 分钟",
        RoomConnectionState.Reconnecting => "重连中",
        _ => throw new ArgumentOutOfRangeException(nameof(ConnectionState))
    };
    public bool HasQrCode => QrCode is not null;
    [ObservableProperty] private ObservableCollection<Danmaku> danmaku = [];

    private const int RecentDanmakuLimit = 1000;
    public static string KeepRecentDanmakuLabel { get; } = $"只保留最近 {RecentDanmakuLimit} 条";
    public static IMultiValueConverter IsAccountDanmaku { get; } = new FuncMultiValueConverter<object?, bool>(
        values => values.ToArray() is [long uid, Account account] && uid == account.Uid);

    partial void OnConnectionStateChanged(RoomConnectionState value)
    {
        if (value != RoomConnectionState.Disconnected) return;
        connectionTimer?.Dispose();
        connectionTimer = null;
        connectedAt = null;
    }

    partial void OnKeepRecentDanmakuChanged(bool value)
    {
        // One Reset instead of a RemoveAt(0) event per dropped item after hours unchecked.
        if (value && Danmaku.Count > RecentDanmakuLimit) Danmaku = new(Danmaku.TakeLast(RecentDanmakuLimit));
        settings.KeepRecentDanmaku = value;
        try { settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogError(error, "保存 GUI 设置失败");
            Message = $"保存设置失败：{error.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect), IncludeCancelCommand = true)]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!long.TryParse(RoomNumber, out var roomId) || roomId <= 0)
        {
            Message = "请输入有效的房间号（正整数）。";
            return;
        }
        ConnectionState = RoomConnectionState.Connecting;
        activeRoomNumber = RoomNumber;
        Message = "";
        if (!preserveDanmaku) Danmaku.Clear();
        preserveDanmaku = false;
        try
        {
            // Core runs the room connection off the UI thread; this loop only shows what it reports.
            await foreach (var update in client.WatchAsync(roomId, cancellationToken))
                Apply(update);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogError(error, "直播间连接失败");
            Message = $"连接失败：{error.Message}";
        }
        finally
        {
            ClearQrCode();
            ConnectionState = RoomConnectionState.Disconnected;
            activeRoomNumber = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReconnect))]
    private async Task ReconnectAsync()
    {
        if (!CanReconnect || activeRoomNumber is not { } room) return;
        IsRestarting = true;
        try { await StopWatchingAsync(); }
        finally { IsRestarting = false; }
        if (!CanConnect) return;
        RoomNumber = room;
        preserveDanmaku = true;
        ConnectCommand.Execute(null);
    }

    internal void Apply(WatchUpdate update)
    {
        switch (update)
        {
            case WatchQrCode qr:
                using (var data = QRCodeGenerator.GenerateQrCode(qr.Content, QRCodeGenerator.ECCLevel.M))
                using (var code = new PngByteQRCode(data))
                using (var png = new MemoryStream(code.GetGraphic(8)))
                {
                    var bitmap = new Bitmap(png);
                    ClearQrCode();
                    QrCode = bitmap;
                }
                break;
            case Connecting:
                EndQrLogin();
                ConnectionState = RoomConnectionState.Connecting;
                break;
            case Connected:
                EndQrLogin();
                connectedAt ??= clock.GetTimestamp();
                connectionTimer ??= clock.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
                {
                    if (connectedAt is not null) OnPropertyChanged(nameof(Status));
                }), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
                ConnectionState = RoomConnectionState.Connected;
                break;
            case Reconnecting:
                ConnectionState = RoomConnectionState.Reconnecting;
                break;
            case Danmaku danmaku:
                Danmaku.Add(danmaku);
                if (KeepRecentDanmaku && Danmaku.Count > RecentDanmakuLimit) Danmaku.RemoveAt(0);
                break;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ReadAccountAsync()
    {
        if (IsLoggingOut || shuttingDown) return Task.CompletedTask;
        accountReadStop?.Cancel();
        var previous = accountReadTask;
        var stop = new CancellationTokenSource();
        accountReadStop = stop;
        AccountStatus = AccountStatus.Reading;
        return accountReadTask = ReadAsync();

        async Task ReadAsync()
        {
            try
            {
                await previous;
                // Loading the saved credential can open SQLite, so even the synchronous part runs off the UI thread.
                var result = await Task.Run(() => client.GetAccountAsync(stop.Token), stop.Token);
                stop.Token.ThrowIfCancellationRequested();
                AccountStatus = AccountStatus.Of(result);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (stop.IsCancellationRequested) return;
                logger.LogError(error, "账号信息读取失败");
                AccountStatus = AccountStatus.Failed;
            }
            finally
            {
                if (ReferenceEquals(accountReadStop, stop)) accountReadStop = null;
                stop.Dispose();
            }
        }
    }

    [RelayCommand]
    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        IsLoggingOut = true;
        Message = "";
        var loggedOut = false;
        try
        {
            await StopAccountReadAsync();
            await StopWatchingAsync();
            if (ReconnectCommand.ExecutionTask is { } restart) await restart;
            await Task.Run(() => client.LogoutAsync(cancellationToken), cancellationToken);
            AccountStatus = AccountStatus.LoggedOut;
            Message = "已退出登录，下次连接时请重新扫码。";
            loggedOut = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogError(error, "退出登录失败");
            Message = $"退出登录失败：{error.Message}";
        }
        finally { IsLoggingOut = false; }
        // The stopped read left the panel on 读取中, and a failed logout may have kept the credential.
        if (!loggedOut) _ = ReadAccountAsync();
    }

    private async Task StopAccountReadAsync()
    {
        accountReadStop?.Cancel();
        await accountReadTask;
    }

    private async Task StopWatchingAsync()
    {
        ConnectCommand.Cancel();
        if (ConnectCommand.ExecutionTask is { } task) await task;
    }

    public async Task ShutdownAsync()
    {
        shuttingDown = true;
        ConnectCommand.NotifyCanExecuteChanged();
        ReconnectCommand.NotifyCanExecuteChanged();
        LogoutCommand.Cancel();
        await StopAccountReadAsync();
        await StopWatchingAsync();
        if (ReconnectCommand.ExecutionTask is { } restart) await restart;
        if (LogoutCommand.ExecutionTask is { } task) await task;
    }

    // Reconnects skip Connecting, so a QR login can end at either update.
    private void EndQrLogin()
    {
        if (HasQrCode) _ = ReadAccountAsync();
        ClearQrCode();
    }

    private void ClearQrCode()
    {
        var previous = QrCode;
        QrCode = null;
        previous?.Dispose();
    }
}
