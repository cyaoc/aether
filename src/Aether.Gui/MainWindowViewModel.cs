using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reflection;
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
    private readonly TimeProvider clock;
    private readonly AutoUpdater updater;

    public MainWindowViewModel(AetherClient client, ILogger<MainWindowViewModel> logger, TimeProvider clock, AutoUpdater updater)
    {
        this.client = client;
        this.logger = logger;
        this.clock = clock;
        this.updater = updater;
        WebService = new(client, logger, clock, shutdown.Token);
        Lottery = new(client, logger, clock, shutdown.Token);
        GiftBoard = new(client, logger, clock, shutdown.Token);
        ConnectCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectCommand.CanBeCanceled)) DisconnectCommand.NotifyCanExecuteChanged();
        };
    }

    public SendQueueViewModel SendQueue { get; } = new();
    public WebServiceViewModel WebService { get; }
    public LotteryViewModel Lottery { get; }
    public GiftBoardViewModel GiftBoard { get; }
    // Written as a whole: every change saves the row, and the state's own rule keeps the recent rooms in shape.
    private GuiState guiState = GuiState.Default;
    public bool KeepRecentDanmaku
    {
        get => guiState.KeepRecentDanmaku;
        set
        {
            if (value == KeepRecentDanmaku) return;
            guiState = guiState with { KeepRecentDanmaku = value };
            OnPropertyChanged();
            // One Reset instead of a RemoveAt(0) event per dropped item after hours unchecked.
            if (value && Danmaku.Count > RecentDanmakuLimit) Danmaku = new(Danmaku.TakeLast(RecentDanmakuLimit));
            SaveGuiState();
        }
    }
    [ObservableProperty] private string roomNumber = "";
    public ImmutableArray<string> RecentRooms => guiState.RecentRooms;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(HasRoomConnection))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private RoomConnectionState connectionState = RoomConnectionState.Disconnected;
    private Connected? connectedRoom;
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
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    [NotifyPropertyChangedFor(nameof(IsLoggedOut))]
    private AccountStatus accountStatus = AccountStatus.Reading;
    [ObservableProperty] private bool isSplashVisible = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSplashOpaque))]
    private double splashOpacity = 1;
    // Pages turn enabled as the fade starts: their disabled look must not show through it.
    public bool IsSplashOpaque => SplashOpacity == 1;
    internal TimeSpan SplashMinimumDuration { get; set; } = TimeSpan.FromMilliseconds(600);
    public TimeSpan SplashFadeDuration { get; internal set; } = TimeSpan.FromMilliseconds(150);
    [ObservableProperty] private string loginError = "";
    /// <summary>The update the splash shows, from when it is found until the window closes or the splash fades.</summary>
    [ObservableProperty] private UpdateProgress? updateProgress;
    /// <summary>Raised once an update is installed: the window closes and the new version starts.</summary>
    public event Action? UpdateInstalled;
    private Task updating = Task.CompletedTask;
    // The startup read only notes a missing account: the splash decides when the QR login starts.
    private bool nextReadDefersLogin;
    private readonly LatestRun accountRead = new();
    private readonly CancellationTokenSource shutdown = new();

    private bool CanConnect => !IsLoggedOut && !IsLoggingOut && !IsRestarting && !shutdown.IsCancellationRequested;
    private bool CanReconnect => CanConnect && HasRoomConnection;
    private bool CanDisconnect => ConnectCommand.CanBeCanceled;
    private bool CanLogin => IsLoggedOut && !IsLoggingOut && !shutdown.IsCancellationRequested;
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
    /// <summary>What the gui_state row holds, as the window and its log name it; below the label it quotes, which it needs initialised first.</summary>
    internal static string GuiStateText { get; } = $"最近的房间号和“{KeepRecentDanmakuLabel}”";
    public static string AppVersion { get; } = "v" + typeof(MainWindowViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    public static IMultiValueConverter IsAccountDanmaku { get; } = new FuncMultiValueConverter<object?, bool>(
        values => values.ToArray() is [long uid, Account account] && uid == account.Uid);
    public static FuncValueConverter<int, bool> IsCaptain { get; } = new(tier => tier == 1);
    public static FuncValueConverter<int, bool> IsAdmiral { get; } = new(tier => tier == 2);
    public static FuncValueConverter<int, bool> IsGovernor { get; } = new(tier => tier == 3);
    public static FuncValueConverter<string?, string> ReplyPrefix { get; } = new(name => string.IsNullOrEmpty(name) ? "" : $"@{name} ");

    /// <summary>Only while connected: the picker that calls this is disabled otherwise.</summary>
    internal Task<OnlineRank> GetOnlineRankAsync(CancellationToken cancellationToken) =>
        client.GetOnlineRankAsync(connectedRoom!.RoomId, connectedRoom.BroadcasterUid, cancellationToken);

    /// <summary>The splash's motion table (docs/design/splash): it decides once the account read and the update check are both done.
    /// An update holds it until the new version restarts; otherwise the QR login and web service start, and it fades.</summary>
    public async Task StartAsync(string dataDirectory)
    {
        if (shutdown.IsCancellationRequested) return;
        try
        {
            var minimum = Task.Delay(SplashMinimumDuration, clock, shutdown.Token);
            // Off the UI thread, so the splash shows at once even with a whole unpacked package left behind.
            await Task.Run(updater.CleanUp);
            // Before the update check and the state read, which both take what gui.yml held. While it is still there its switch
            // decides, so a broken settings file never updates someone who turned updates off; a settings file whose values
            // cannot be used means on (ADR 0016).
            var autoUpdate = await Task.Run(() => GuiFileUpgrade.Run(dataDirectory, client, logger)
                ?? (Settings.Load(dataDirectory, logger) is { Error: null } settings ? settings.AutoUpdateEnabled.Value : true));
            ShowGuiState(await Task.Run(ReadGuiState));
            var check = autoUpdate ? Task.Run(() => updater.CheckAsync(shutdown.Token)) : Task.FromResult<ReleaseUpdate?>(null);
            nextReadDefersLogin = true;
            try { await ReadAccountCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(3), clock, shutdown.Token); }
            catch (TimeoutException)
            {
                logger.LogError("账号信息读取超时");
                // Not awaited: opening SQLite ignores the token, and a late result is dropped either way.
                _ = accountRead.StopAsync();
                AccountStatus = AccountStatus.Failed;
            }
            if (await check is { } release)
            {
                IProgress<UpdateProgress?> progress = new Progress<UpdateProgress?>(value => UpdateProgress = value);
                // Closing the window waits for this, so an install is never cut off halfway.
                var installing = updater.UpdateAsync(release, progress, shutdown.Token);
                updating = installing;
                if (await installing)
                {
                    if (!shutdown.IsCancellationRequested) UpdateInstalled?.Invoke();
                    return;
                }
                // Skipped or failed: the splash goes on as with no update, showing nothing. Reported like the steps, it lands after them.
                progress.Report(null);
            }
            shutdown.Token.ThrowIfCancellationRequested();
            if (IsLoggedOut) StartLogin();
            // The web service starts after the decision, so an old version still holding its port never meets the new one.
            // The 抽奖页 and the GUI read the same lottery, so the GUI starts following it with the web service.
            Lottery.ObserveLottery();
            GiftBoard.Observe();
            await Task.WhenAll(WebService.ApplyWebSettingsAsync(Settings.Load(dataDirectory, logger)), FadeAsync());

            async Task FadeAsync()
            {
                await minimum;
                SplashOpacity = 0;
                await Task.Delay(SplashFadeDuration, clock, shutdown.Token);
                IsSplashVisible = false;
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    }

    [RelayCommand]
    private void SkipUpdate() => updater.Skip();

    partial void OnConnectionStateChanged(RoomConnectionState value)
    {
        if (value != RoomConnectionState.Disconnected) return;
        connectionTimer?.Dispose();
        connectionTimer = null;
        connectedAt = null;
        connectedRoom = null;
        SendQueue.EndPending();
    }

    internal GuiState ReadGuiState()
    {
        try { return client.GuiState.Load(); }
        catch (Exception error)
        {
            logger.LogWarning(error, "读取{What}失败，使用默认值", GuiStateText);
            return GuiState.Default;
        }
    }

    /// <summary>The room input stays empty until this fills it with the latest room connected; one already typed in stays.</summary>
    internal void ShowGuiState(GuiState state)
    {
        // Not through the setters, so showing what was read does not save it back; the same rooms again leave the list alone.
        var shown = guiState;
        guiState = state;
        if (shown.KeepRecentDanmaku != state.KeepRecentDanmaku) OnPropertyChanged(nameof(KeepRecentDanmaku));
        if (!shown.RecentRooms.SequenceEqual(state.RecentRooms)) OnPropertyChanged(nameof(RecentRooms));
        if (RoomNumber.Length == 0) RoomNumber = RecentRooms.FirstOrDefault() ?? "";
    }

    private void SaveGuiState()
    {
        try { client.GuiState.Save(guiState); }
        catch (Exception error)
        {
            logger.LogError(error, "保存{What}失败", GuiStateText);
            Message = $"保存{GuiStateText}失败：{error.Message}";
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

    private void SaveRecentRooms(ImmutableArray<string> rooms)
    {
        guiState = guiState with { RecentRooms = rooms };
        OnPropertyChanged(nameof(RecentRooms));
        SaveGuiState();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
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
            await foreach (var update in client.WatchAsync(roomId, enableStreamOverlay: true, cancellationToken))
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

    /// <summary>The 断开 button: the streamer stops, so a running 礼物榜 期 ends before the room connection does. It can be
    /// clicked when the connection can be cancelled, as the cancel command the button used before.</summary>
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        await GiftBoard.EndBeforeLeavingAsync();
        ConnectCommand.Cancel();
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
            case Connected connected:
                connectedRoom = connected;
                EndQrLogin();
                connectedAt ??= clock.GetTimestamp();
                connectionTimer ??= clock.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
                {
                    if (connectedAt is not null) OnPropertyChanged(nameof(Status));
                }), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
                ConnectionState = RoomConnectionState.Connected;
                // Auto-reconnects land here too; they leave the room first, so the recent rooms are not rewritten each time.
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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ReadAccountAsync()
    {
        var startLogin = !nextReadDefersLogin;
        nextReadDefersLogin = false;
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
                if (result is null && startLogin)
                {
                    await StopWatchingAsync();
                    token.ThrowIfCancellationRequested();
                    StartLogin();
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

    private void StartLogin()
    {
        if (shutdown.IsCancellationRequested) return;
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
            await GiftBoard.EndBeforeLeavingAsync();
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
        if (loggedOut) StartLogin();
        else _ = ReadAccountAsync();
    }

    private async Task StopWatchingAsync()
    {
        ConnectCommand.Cancel();
        if (ConnectCommand.ExecutionTask is { } task) await task;
    }

    // Stop the web service first so later cleanup cannot hold its port; finish draws, and end a running 礼物榜 期, before
    // disconnecting the room they require. A step that fails is only logged: the window is closing all the same.
    public async Task ShutdownAsync()
    {
        RunShutdownStep("取消进行中的操作", shutdown.Cancel);
        RunShutdownStep("刷新连接按钮", ConnectCommand.NotifyCanExecuteChanged);
        RunShutdownStep("刷新重连按钮", ReconnectCommand.NotifyCanExecuteChanged);
        RunShutdownStep("刷新登录按钮", LoginCommand.NotifyCanExecuteChanged);
        RunShutdownStep("取消登录", LoginCommand.Cancel);
        RunShutdownStep("取消退出登录", LogoutCommand.Cancel);
        var accountStopped = accountRead.StopAsync();
        await RunShutdownStepAsync("等自动更新结束", () => updating);
        await RunShutdownStepAsync("停网页服务", WebService.StopAsync);
        await RunShutdownStepAsync("停抽奖", Lottery.StopAsync);
        await RunShutdownStepAsync("结束礼物榜这一期", GiftBoard.EndBeforeLeavingAsync);
        await RunShutdownStepAsync("停礼物榜", GiftBoard.StopAsync);
        await RunShutdownStepAsync("停账号读取", () => accountStopped);
        await RunShutdownStepAsync("断开直播间", StopWatchingAsync);
        await RunShutdownStepAsync("等重连结束", () => ReconnectCommand.ExecutionTask ?? Task.CompletedTask);
        await RunShutdownStepAsync("等退出登录结束", () => LogoutCommand.ExecutionTask ?? Task.CompletedTask);
        await RunShutdownStepAsync("等登录结束", () => LoginCommand.ExecutionTask ?? Task.CompletedTask);
    }

    private async Task RunShutdownStepAsync(string step, Func<Task> run)
    {
        try { await run(); }
        catch (Exception error) { LogShutdownStepFailed(step, error); }
    }

    private void RunShutdownStep(string step, Action run)
    {
        try { run(); }
        catch (Exception error) { LogShutdownStepFailed(step, error); }
    }

    private void LogShutdownStepFailed(string step, Exception error) => logger.LogError(error, "关窗口时{Step}失败", step);

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
