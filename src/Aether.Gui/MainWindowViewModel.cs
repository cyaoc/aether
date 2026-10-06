using System.Collections.ObjectModel;
using Aether.Core;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Aether.Gui;

public enum RoomConnectionState { Disconnected, Connecting, Connected, Reconnecting }

public partial class MainWindowViewModel(AetherClient client, ILogger<MainWindowViewModel> logger, GuiSettings settings) : ObservableObject
{
    [ObservableProperty] private bool keepRecentDanmaku = settings.KeepRecentDanmaku;
    [ObservableProperty] private string roomNumber = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    private RoomConnectionState connectionState = RoomConnectionState.Disconnected;

    [ObservableProperty] private string message = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQrCode))]
    private Bitmap? qrCode;

    // A room connection started mid-logout would load the credential that logout is deleting.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isLoggingOut;

    private bool CanConnect => !IsLoggingOut;
    public bool IsConnected => ConnectionState == RoomConnectionState.Connected;
    public string Status => ConnectionState switch
    {
        RoomConnectionState.Disconnected => "未连接",
        RoomConnectionState.Connecting => "连接中",
        RoomConnectionState.Connected => "已连接",
        RoomConnectionState.Reconnecting => "重连中",
        _ => throw new ArgumentOutOfRangeException(nameof(ConnectionState))
    };
    public bool HasQrCode => QrCode is not null;
    [ObservableProperty] private ObservableCollection<Danmaku> danmaku = [];

    private const int RecentDanmakuLimit = 1000;
    public static string KeepRecentDanmakuLabel { get; } = $"只保留最近 {RecentDanmakuLimit} 条";

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
        Message = "";
        Danmaku.Clear();
        try
        {
            // Core also does synchronous SQLite/protocol work; keep it off the UI thread.
            await Task.Run(async () =>
            {
                await foreach (var update in client.WatchAsync(roomId, cancellationToken))
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested) Apply(update);
                    });
            }, cancellationToken);
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
        }
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
                ClearQrCode();
                ConnectionState = RoomConnectionState.Connecting;
                break;
            case Connected:
                ClearQrCode();
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

    [RelayCommand]
    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        IsLoggingOut = true;
        Message = "";
        try
        {
            await StopWatchingAsync();
            await Task.Run(() => client.LogoutAsync(cancellationToken), cancellationToken);
            Message = "已退出登录，下次连接时请重新扫码。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogError(error, "退出登录失败");
            Message = $"退出登录失败：{error.Message}";
        }
        finally { IsLoggingOut = false; }
    }

    private async Task StopWatchingAsync()
    {
        ConnectCommand.Cancel();
        if (ConnectCommand.ExecutionTask is { } task) await task;
    }

    public async Task ShutdownAsync()
    {
        LogoutCommand.Cancel();
        await StopWatchingAsync();
        if (LogoutCommand.ExecutionTask is { } task) await task;
    }

    private void ClearQrCode()
    {
        var previous = QrCode;
        QrCode = null;
        previous?.Dispose();
    }
}
