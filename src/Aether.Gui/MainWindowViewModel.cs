using System.Collections.ObjectModel;
using Aether.Core;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Aether.Gui;

public partial class MainWindowViewModel(AetherClient client, ILogger<MainWindowViewModel> logger) : ObservableObject
{
    [ObservableProperty] private string roomNumber = "";
    [ObservableProperty] private string status = "未连接";
    [ObservableProperty] private string message = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQrCode))]
    private Bitmap? qrCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isWatching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(LogoutCommand))]
    private bool isLoggingOut;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(LogoutCommand))]
    private bool isClosing;

    public bool CanConnect => !IsWatching && !IsLoggingOut && !IsClosing;
    public bool HasQrCode => QrCode is not null;
    private bool CanLogout => !IsLoggingOut && !IsClosing;
    public ObservableCollection<Danmaku> Danmaku { get; } = [];

    [RelayCommand(CanExecute = nameof(CanConnect), IncludeCancelCommand = true)]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!long.TryParse(RoomNumber, out var roomId) || roomId <= 0)
        {
            Message = "请输入有效的房间号（正整数）。";
            return;
        }
        IsWatching = true;
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
            IsWatching = false;
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
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLogout))]
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
        IsClosing = true;
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
