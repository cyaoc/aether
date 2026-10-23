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

public enum RoomConnectionState { Disconnected, Connecting, Connected, Reconnecting, WaitingForScan }

/// <summary>What the sidebar shows for the account; Account is set only when a read finds one.</summary>
public sealed record AccountStatus(string Text, Account? Account = null)
{
    public static AccountStatus Reading { get; } = new("读取中");
    public static AccountStatus LoggedOut { get; } = new("未登录");
    public static AccountStatus Failed { get; } = new("账号信息读取失败");
    public static AccountStatus Of(Account? account) => account is null ? LoggedOut : new(account.Nickname, account);
    public bool ReadFailed => this == Failed;
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AetherClient client;
    private readonly ILogger<MainWindowViewModel> logger;
    private readonly GuiSettings settings;
    private readonly TimeProvider clock;

    public MainWindowViewModel(AetherClient client, ILogger<MainWindowViewModel> logger, GuiSettings settings,
        TimeProvider clock)
    {
        this.client = client;
        this.logger = logger;
        this.settings = settings;
        this.clock = clock;
        keepRecentDanmaku = settings.KeepRecentDanmaku;
        roomNumber = settings.RecentRooms.FirstOrDefault() ?? "";
        WebService = new(client, logger, clock, shutdown.Token);
        Lottery = new(client, logger, settings, SaveGuiSettings, clock, shutdown.Token);
    }

    public SendQueueViewModel SendQueue { get; } = new();
    public WebServiceViewModel WebService { get; }
    public LotteryViewModel Lottery { get; }
    [ObservableProperty] private bool keepRecentDanmaku;
    [ObservableProperty] private string roomNumber;
    public string[] RecentRooms => settings.RecentRooms;
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
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool isLoggingOut;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private bool isRestarting;
    private bool preserveHistory;
    private string? activeRoomNumber;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    [NotifyPropertyChangedFor(nameof(IsLoggedOut))]
    private AccountStatus accountStatus = AccountStatus.Reading;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool isLoginPage;
    [ObservableProperty] private string loginError = "";
    private readonly LatestRun accountRead = new();
    private readonly CancellationTokenSource shutdown = new();

    private bool CanConnect => AccountStatus != AccountStatus.LoggedOut && !IsLoginPage && !IsLoggingOut && !IsRestarting && !shutdown.IsCancellationRequested;
    private bool CanReconnect => CanConnect && HasRoomConnection;
    private bool CanLogin => IsLoginPage && !IsLoggingOut && !shutdown.IsCancellationRequested;
    public bool IsConnected => ConnectionState == RoomConnectionState.Connected;
    public bool HasRoomConnection => ConnectionState != RoomConnectionState.Disconnected;
    public string Status => ConnectionState switch
    {
        RoomConnectionState.Disconnected => "未连接",
        RoomConnectionState.Connecting => "连接中",
        RoomConnectionState.Connected => $"已连接 · {(connectedAt is { } start ? (int)clock.GetElapsedTime(start).TotalMinutes : 0)} 分钟",
        RoomConnectionState.Reconnecting => "重连中",
        RoomConnectionState.WaitingForScan => "等待扫码",
        _ => throw new ArgumentOutOfRangeException(nameof(ConnectionState))
    };
    public bool HasQrCode => QrCode is not null;
    public bool IsLoggedOut => AccountStatus == AccountStatus.LoggedOut;
    [ObservableProperty] private ObservableCollection<Danmaku> danmaku = [];

    private const int RecentDanmakuLimit = 1000;
    public static string KeepRecentDanmakuLabel { get; } = $"只保留最近 {RecentDanmakuLimit} 条";
    public static IMultiValueConverter IsAccountDanmaku { get; } = new FuncMultiValueConverter<object?, bool>(
        values => values.ToArray() is [long uid, Account account] && uid == account.Uid);
    public static FuncValueConverter<int, bool> IsCaptain { get; } = new(tier => tier == 1);
    public static FuncValueConverter<int, bool> IsAdmiral { get; } = new(tier => tier == 2);
    public static FuncValueConverter<int, bool> IsGovernor { get; } = new(tier => tier == 3);
    public static FuncValueConverter<string?, string> ReplyPrefix { get; } = new(name => string.IsNullOrEmpty(name) ? "" : $"@{name} ");

    public Task StartAsync(string dataDirectory)
    {
        if (shutdown.IsCancellationRequested) return Task.CompletedTask;
        // The 抽奖页 and the GUI read the same lottery, so the GUI starts following it with the web service.
        Lottery.ObserveLottery();
        var webSettings = WebService.ApplyWebSettingsAsync(Settings.Load(dataDirectory, logger));
        return Task.WhenAll(webSettings, ReadAccountCommand.ExecuteAsync(null));
    }

    partial void OnConnectionStateChanged(RoomConnectionState value)
    {
        if (value != RoomConnectionState.Disconnected) return;
        connectionTimer?.Dispose();
        connectionTimer = null;
        connectedAt = null;
        SendQueue.EndPending();
    }

    partial void OnKeepRecentDanmakuChanged(bool value)
    {
        // One Reset instead of a RemoveAt(0) event per dropped item after hours unchecked.
        if (value && Danmaku.Count > RecentDanmakuLimit) Danmaku = new(Danmaku.TakeLast(RecentDanmakuLimit));
        settings.KeepRecentDanmaku = value;
        SaveGuiSettings();
    }

    private void SaveGuiSettings()
    {
        try { settings.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogError(error, "保存 GUI 设置失败");
            Message = $"保存 GUI 设置失败：{error.Message}";
        }
    }

    [RelayCommand]
    private void RemoveRecentRoom(string room)
    {
        // Removing the selected item can clear the editable ComboBox's two-way Text binding.
        var input = RoomNumber;
        SaveRecentRooms([.. RecentRooms.Where(value => value != room)]);
        RoomNumber = input;
    }

    private void SaveRecentRooms(string[] rooms)
    {
        settings.RecentRooms = rooms;
        OnPropertyChanged(nameof(RecentRooms));
        SaveGuiSettings();
    }

    [RelayCommand(CanExecute = nameof(CanConnect), IncludeCancelCommand = true)]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!Room.TryParseNumber(RoomNumber, out var roomId))
        {
            Message = Room.InvalidNumber;
            return;
        }
        ConnectionState = RoomConnectionState.Connecting;
        activeRoomNumber = RoomNumber;
        Message = "";
        if (!preserveHistory)
        {
            Danmaku.Clear();
            SendQueue.Clear();
            WebService.ClearAnimationHistory();
        }
        preserveHistory = false;
        try
        {
            // Core runs the room connection off the UI thread; this loop only shows what it reports.
            await foreach (var update in client.WatchAsync(roomId, enableLottery: true, cancellationToken))
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

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        LoginError = "";
        try
        {
            // Logging in saves the credential to SQLite, so the stream is read off the UI thread too.
            await Task.Run(async () =>
            {
                await foreach (var update in client.LoginAsync(cancellationToken))
                    if (update is LoginQrCode qr) await Dispatcher.UIThread.InvokeAsync(() => ShowQrCode(qr.Content));
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); // Core ends the stream quietly when cancelled.
            IsLoginPage = false;
            // A read that still finds no account brings the login page back with a fresh code.
            _ = ReadAccountAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogError(error, "扫码登录失败");
            LoginError = $"登录失败：{error.Message}";
        }
        finally { ClearQrCode(); }
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
        preserveHistory = true;
        ConnectCommand.Execute(null);
    }

    internal void Apply(WatchUpdate update)
    {
        switch (update)
        {
            case WatchQrCode qr:
                ShowQrCode(qr.Content);
                ConnectionState = RoomConnectionState.WaitingForScan;
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
                // Auto-reconnects land here too; they leave the room first, so gui.yml is not rewritten each time.
                if (activeRoomNumber is { } room && !ConnectCommand.IsCancellationRequested && RecentRooms.FirstOrDefault() != room)
                    SaveRecentRooms([room, .. RecentRooms]);
                break;
            case Reconnecting:
                ConnectionState = RoomConnectionState.Reconnecting;
                break;
            case Danmaku danmaku:
                Danmaku.Add(danmaku);
                if (KeepRecentDanmaku && Danmaku.Count > RecentDanmakuLimit) Danmaku.RemoveAt(0);
                break;
            case SendQueueUpdate progress:
                SendQueue.Apply(progress);
                break;
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        LoginCommand.Cancel();
        if (LoginCommand.ExecutionTask is { } login) await login;
        ClearQrCode();
        IsLoginPage = false;
    }

    [RelayCommand]
    private void OpenLoginPage() => ShowLoginPage();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ReadAccountAsync()
    {
        if (IsLoggingOut || shutdown.IsCancellationRequested) return Task.CompletedTask;
        AccountStatus = AccountStatus.Reading;
        return accountRead.Start(ReadAsync);

        async Task ReadAsync(CancellationToken token)
        {
            try
            {
                // Loading the saved credential can open SQLite, so even the synchronous part runs off the UI thread.
                var result = await Task.Run(() => client.GetAccountAsync(token), token);
                token.ThrowIfCancellationRequested();
                AccountStatus = AccountStatus.Of(result);
                if (result is null)
                {
                    await StopWatchingAsync();
                    token.ThrowIfCancellationRequested();
                    ShowLoginPage();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (token.IsCancellationRequested) return;
                logger.LogError(error, "账号信息读取失败");
                AccountStatus = AccountStatus.Failed;
            }
        }
    }

    private void ShowLoginPage()
    {
        if (shutdown.IsCancellationRequested) return;
        IsLoginPage = true;
        if (LoginCommand.CanExecute(null)) LoginCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        IsLoggingOut = true;
        Message = "";
        var loggedOut = false;
        try
        {
            LoginCommand.Cancel();
            await accountRead.StopAsync();
            if (LoginCommand.ExecutionTask is { } login) await login;
            await StopWatchingAsync();
            if (ReconnectCommand.ExecutionTask is { } restart) await restart;
            await Task.Run(() => client.LogoutAsync(cancellationToken), cancellationToken);
            AccountStatus = AccountStatus.LoggedOut;
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
        if (loggedOut) ShowLoginPage();
        else _ = ReadAccountAsync();
    }

    private async Task StopWatchingAsync()
    {
        ConnectCommand.Cancel();
        if (ConnectCommand.ExecutionTask is { } task) await task;
    }

    // Finish draws before disconnecting the room they require; stop the web service last, as the CLI does, because the receive loop sends it welcome animations.
    public async Task ShutdownAsync()
    {
        shutdown.Cancel();
        ConnectCommand.NotifyCanExecuteChanged();
        ReconnectCommand.NotifyCanExecuteChanged();
        LoginCommand.NotifyCanExecuteChanged();
        LoginCommand.Cancel();
        LogoutCommand.Cancel();
        var accountStopped = accountRead.StopAsync();
        await Lottery.StopAsync();
        await accountStopped;
        await StopWatchingAsync();
        if (ReconnectCommand.ExecutionTask is { } restart) await restart;
        if (LogoutCommand.ExecutionTask is { } task) await task;
        if (LoginCommand.ExecutionTask is { } login) await login;
        await WebService.StopAsync();
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

    private void ShowQrCode(string content)
    {
        using var data = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var code = new PngByteQRCode(data);
        using var png = new MemoryStream(code.GetGraphic(8));
        var bitmap = new Bitmap(png);
        ClearQrCode();
        QrCode = bitmap;
    }
}
