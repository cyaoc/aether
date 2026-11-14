using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The web service region's state (ADR 0017): the listener, the streaming software connected to each page, the video
/// queue and the animations feeding it. <see cref="WebServer"/> runs everything that waits, and hands it a step to touch any of
/// this.</summary>
internal sealed class WebService
{
    /// <summary>Why the 抽奖页 cannot show an example while the service is not running and reports no error of its own.</summary>
    public const string NotRunning = "网页服务没有运行，请先在通用设置中打开网页服务。";

    private readonly ILogger logger;
    private readonly Action<string?> examplePage;
    private readonly HashSet<Connection> connections = [];
    private (HttpListener Listener, CancellationTokenSource Stop)? serving;
    private Task running = Task.CompletedTask;

    /// <param name="examplePage">Tells the lottery region whether the 抽奖页 can show an example, and why not.</param>
    /// <param name="character">The current character, whose lines the 桌宠 says.</param>
    public WebService(ChannelWriter<VideoQueue.Playback> queue, TimeProvider timeProvider, ILogger logger, Action<string?> examplePage, Character character)
    {
        this.logger = logger;
        this.examplePage = examplePage;
        Video = new(this, queue, timeProvider);
        WelcomeAnimation = new(this, timeProvider, logger);
        EntryAnimation = new(this, WelcomeAnimation, timeProvider, logger);
        GiftAnimation = new(this, timeProvider, logger);
        DesktopPet = new(this, character);
    }

    /// <summary>One streaming software's event stream to a page. Only its write lock is taken outside a step.</summary>
    public sealed class Connection(HttpListenerResponse response, WebPage page)
    {
        public HttpListenerResponse Response { get; } = response;
        public WebPage Page { get; } = page;
        public SemaphoreSlim WriteLock { get; } = new(1);
        public CancellationTokenSource? StreamStop { get; set; }
    }

    public Subscribers<WebServiceUpdate> Subscribers { get; } = new();
    public VideoQueue Video { get; }
    public WelcomeAnimation WelcomeAnimation { get; }
    public EntryAnimation EntryAnimation { get; }
    public GiftAnimation GiftAnimation { get; }
    public DesktopPet DesktopPet { get; }
    public WebServiceStatus Status { get; private set; } = new(false, Settings.WebPort.Initial.Value, 0, 0, 0);

    /// <summary>The settings applied last. Playback sources and the room's decoder read these, not their room connection's,
    /// so a save reaches them without reconnecting.</summary>
    public Settings? Settings { get; private set; }

    public void Apply(Settings settings)
    {
        Settings = settings;
        DesktopPet.Applied();
    }

    /// <summary>Only a changed port restarts existing connections.</summary>
    public bool Serves(int port) => serving is not null && Status.Port == port;

    /// <summary>Listens on <paramref name="port"/>, and has <paramref name="serve"/> serve it outside the region until the stop.</summary>
    public void Start(int port, Func<HttpListener, CancellationToken, Task> serve)
    {
        // Another StartAsync won the race; starting again would leak its listener.
        if (serving is not null) throw new InvalidOperationException("网页服务正在启动，请稍后再试。");
        var starting = new WebServiceStatus(false, port, 0, 0, 0);
        var server = new HttpListener();
        server.Prefixes.Add(starting.Origin + "/");
        try { server.Start(); }
        catch (HttpListenerException exception)
        {
            server.Close();
            // EADDRINUSE (macOS 48, Linux 98, WSA 10048); http.sys 32 (port held by another program) and
            // 183 (prefix already registered); managed HttpListener 400 (prefix already registered).
            var reason = exception.ErrorCode is 48 or 98 or 10048 or 32 or 183 or 400
                ? $"端口 {port} 被别的程序占用" : $"网页服务启动失败（端口 {port}，错误码 {exception.ErrorCode}）：{exception.Message}";
            logger.LogWarning("网页服务没有运行：{Reason}", reason);
            Report(starting with { Error = reason });
            return;
        }
        var stop = new CancellationTokenSource();
        serving = (server, stop);
        Report(starting with { Running = true });
        running = serve(server, stop.Token);
    }

    /// <summary>Closes the listener and every connection; returns what still runs, for the caller to wait for outside the region,
    /// or null when nothing does.</summary>
    public Task? Stop()
    {
        if (serving is not (var listener, var stop)) return null;
        stop.Cancel();
        // Abort before Close: on Windows, Close releases the request queue that Abort needs.
        foreach (var connection in connections) Abort(connection.Response);
        listener.Close();
        connections.Clear();
        Report(Status with { Running = false, VideoConnections = 0, LotteryConnections = 0, GiftBoardConnections = 0, DesktopPetConnections = 0 });
        DesktopPet.ConnectionsChanged();
        return running;
    }

    /// <summary>Clears what <paramref name="stopped"/> left, even after a fault, or Start would refuse forever; a later start's
    /// is left alone.</summary>
    public void Stopped(Task stopped)
    {
        if (running != stopped || serving is not (_, var stop)) return;
        serving = null;
        stop.Dispose();
    }

    /// <summary>Counts a streaming software in once its request is read; a stop that came first refuses it.</summary>
    public void Connect(Connection connection, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        connections.Add(connection);
        ConnectionsChanged();
    }

    /// <summary>A page's stream lives in its request, so the connection ends with it.</summary>
    public void EndStream(Connection connection)
    {
        // A write that fails on this connection cancels StreamStop in Drop; clear it before the request disposes it.
        connection.StreamStop = null;
        if (connections.Remove(connection)) ConnectionsChanged();
    }

    /// <summary>A write failed or timed out: the connection is gone, and so is its page's stream.</summary>
    public void Drop(Connection connection)
    {
        if (connections.Remove(connection)) ConnectionsChanged();
        connection.StreamStop?.Cancel();
        Abort(connection.Response);
    }

    /// <summary>Every connection to <paramref name="page"/>, or to any page when null.</summary>
    public Connection[] Connected(WebPage? page) => [.. connections.Where(connection => page is null || connection.Page == page)];

    public void Publish(WebServiceUpdate update) => Subscribers.Publish(update);

    /// <summary>Ids are unique and one item plays at a time, so only the source that started it has a test play by this id.</summary>
    public void StopTestPlay(string id)
    {
        WelcomeAnimation.StopTestPlay(id);
        EntryAnimation.StopTestPlay(id);
        GiftAnimation.StopTestPlay(id);
    }

    private void ConnectionsChanged()
    {
        var status = Status with { VideoConnections = Count<VideoPage>(), LotteryConnections = Count<LotteryPage>(),
            GiftBoardConnections = Count<GiftBoardPage>(), DesktopPetConnections = Count<DesktopPetPage>() };
        logger.LogInformation("网页服务：视频页 {VideoCount} 个推流软件，抽奖页 {LotteryCount} 个推流软件，礼物榜页 {GiftBoardCount} 个推流软件，桌宠页 {DesktopPetCount} 个推流软件",
            status.VideoConnections, status.LotteryConnections, status.GiftBoardConnections, status.DesktopPetConnections);
        Report(status);
        if (status.VideoConnections == 0) Video.Disconnected();
        DesktopPet.ConnectionsChanged();
    }

    private int Count<TPage>() where TPage : WebPage => connections.Count(connection => connection.Page is TPage);

    private void Report(WebServiceStatus status)
    {
        // Posted from the step that starts or stops the service: an example play ordered after a stop is refused, and one before it
        // is stopped, as when both ran under one lock.
        if (status.Running != Status.Running || status.Error != Status.Error)
            examplePage(WhyNotRunning(status));
        Status = status;
        Subscribers.Publish(status);
    }

    /// <summary>Why nothing can be shown on a page while <paramref name="status"/> holds; null while the service runs.</summary>
    internal static string? WhyNotRunning(WebServiceStatus status) => status.Running ? null : status.Error ?? NotRunning;

    /// <summary>A request still finishing after StopAsync closed the listener has nothing left to abort on Windows.</summary>
    internal static void Abort(HttpListenerResponse response)
    {
        try { response.Abort(); }
        catch (ObjectDisposedException) { }
    }
}
