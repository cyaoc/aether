using System.Collections.ObjectModel;
using Aether.Core;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Aether.Gui;

public partial class MainWindowViewModel(AetherClient client, ILogger<MainWindowViewModel> logger, GuiSettings settings) : ObservableObject
{
    [ObservableProperty] private bool keepRecentDanmaku = settings.KeepRecentDanmaku;
    [ObservableProperty] private string roomNumber = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    private string status = "未连接";

    [ObservableProperty] private string message = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQrCode))]
    private Bitmap? qrCode;

    // A room connection started mid-logout would load the credential that logout is deleting.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isLoggingOut;

    private bool CanConnect => !IsLoggingOut;
    public bool IsConnected => Status == "已连接";
    public bool HasQrCode => QrCode is not null;
    public ObservableCollection<Danmaku> Danmaku { get; } = [];

    partial void OnKeepRecentDanmakuChanged(bool value)
    {
        TrimDanmaku();
        settings.KeepRecentDanmaku = value;
        try { settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogError(error, "保存 GUI 设置失败");
            Message = $"保存设置失败：{error.Message}";
        }
    }

    private void TrimDanmaku()
    {
        if (!KeepRecentDanmaku) return;
        while (Danmaku.Count > 1000) Danmaku.RemoveAt(0);
    }

    [RelayCommand(CanExecute = nameof(CanConnect), IncludeCancelCommand = true)]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!long.TryParse(RoomNumber, out var roomId) || roomId <= 0)
        {
            Message = "请输入有效的房间号（正整数）。";
            return;
        }
        Status = "连接中";
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
            logger.LogError(error, "观看直播间失败");
            Message = $"连接失败：{error.Message}";
        }
        finally
        {
            ClearQrCode();
            Status = "未连接";
        }
    }

    private void Apply(WatchUpdate update)
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
                Status = "连接中";
                break;
            case Connected:
                ClearQrCode();
                Status = "已连接";
                break;
            case Reconnecting:
                Status = "重连中";
                break;
            case Danmaku danmaku:
                Danmaku.Add(danmaku);
                TrimDanmaku();
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
