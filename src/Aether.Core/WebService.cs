using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The shell owns the service lifetime; each page has its own connections.</summary>
public sealed class WebService : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string Retry = "retry: 2000\n\n";
    private const string Heartbeat = ": heartbeat\n\n";
    internal object Gate { get; } = new();
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly WebPage[] pages;
    private readonly Subscribers<WebServiceUpdate> subscribers;
    private readonly HashSet<Connection> connections = [];
    private WebServiceStatus status = new(false, Settings.WebPort.Initial.Value, 0, 0);
    private HttpListener? listener;
    private CancellationTokenSource? stop;
    private Task running = Task.CompletedTask;
    public VideoPage Video { get; }
    public LotteryPage Lottery { get; }

    public WebService(string dataDirectory, TimeProvider timeProvider, ILogger logger, LotteryController lottery)
    {
        this.timeProvider = timeProvider;
        this.logger = logger;
        subscribers = new(Gate);
        Video = new(this, dataDirectory, timeProvider);
        Lottery = new(this, lottery);
        pages = [Video, Lottery];
    }

    /// <summary>One streaming software's event stream to a page.</summary>
    private sealed class Connection(HttpListenerResponse response, WebPage page)
    {
        public HttpListenerResponse Response { get; } = response;
        public WebPage Page { get; } = page;
        public SemaphoreSlim WriteLock { get; } = new(1);
        public CancellationTokenSource? StreamStop { get; set; }
    }

    public WebServiceStatus Status { get { lock (Gate) return status; } }

    /// <summary>Subscribe independently of WatchAsync. The first update is the current service status.</summary>
    public IAsyncEnumerable<WebServiceUpdate> GetUpdatesAsync(CancellationToken cancellationToken = default) =>
        subscribers.SubscribeAsync(() => status, cancellationToken);

    /// <summary>Only a changed port restarts existing connections.</summary>
    public async Task StartAsync(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        lock (Gate) if (stop is not null && status.Port == port) return;
        await StopAsync().ConfigureAwait(false);
        lock (Gate)
        {
            // Another StartAsync won the race; starting again would leak its listener.
            if (stop is not null) throw new InvalidOperationException("网页服务正在启动，请稍后再试。");
            status = new(false, port, 0, 0);
            var server = new HttpListener();
            server.Prefixes.Add(status.Origin + "/");
            try { server.Start(); }
            catch (HttpListenerException exception)
            {
                server.Close();
                // EADDRINUSE (macOS 48, Linux 98, WSA 10048); http.sys 32 (port held by another program) and
                // 183 (prefix already registered); managed HttpListener 400 (prefix already registered).
                var reason = exception.ErrorCode is 48 or 98 or 10048 or 32 or 183 or 400
                    ? $"端口 {status.Port} 被别的程序占用" : $"网页服务启动失败（端口 {status.Port}，错误码 {exception.ErrorCode}）：{exception.Message}";
                status = status with { Error = reason };
                logger.LogWarning("网页服务没有运行：{Reason}", reason);
                Publish(status);
                return;
            }
            listener = server;
            stop = new CancellationTokenSource();
            status = status with { Running = true };
            Publish(status);
            var token = stop.Token;
            running = Task.Run(() => Task.WhenAll(pages.Select(page => page.RunAsync(token))
                .Append(AcceptAsync(server, token)).Append(HeartbeatsAsync(token))));
        }
    }

    public async Task StopAsync()
    {
        Task pending;
        CancellationTokenSource? stopping;
        lock (Gate)
        {
            stopping = stop;
            if (stopping is null) return;
            stopping.Cancel();
            // Abort before Close: on Windows, Close releases the request queue that Abort needs.
            foreach (var connection in connections) Abort(connection.Response);
            listener!.Close();
            connections.Clear();
            status = status with { Running = false, VideoConnections = 0, LotteryConnections = 0 };
            Publish(status);
            pending = running;
        }
        try { await pending.ConfigureAwait(false); }
        finally
        {
            // Clear even after a fault, or Start would refuse forever.
            lock (Gate)
                if (stop == stopping)
                {
                    stop = null;
                    listener = null;
                    stopping.Dispose();
                }
        }
    }

    internal void Publish(WebServiceUpdate update) => subscribers.Publish(update);

    private void ConnectionsChanged()
    {
        status = status with { VideoConnections = connections.Count(connection => connection.Page == Video),
            LotteryConnections = connections.Count(connection => connection.Page == Lottery) };
        logger.LogInformation("网页服务：视频页 {VideoCount} 个推流软件，抽奖页 {LotteryCount} 个推流软件", status.VideoConnections, status.LotteryConnections);
        Publish(status);
        foreach (var page in pages)
            if (!connections.Any(connection => connection.Page == page)) page.Disconnected();
    }

    private static string Frame(SseEvent message) =>
        (message.Name is null ? "" : "event: " + message.Name + "\n") + "data: " + JsonSerializer.Serialize(message.Data, Json) + "\n\n";

    internal Task<int> BroadcastAsync(WebPage page, SseEvent message, CancellationToken token) =>
        BroadcastFrameAsync(Frame(message), token, page);

    private async Task<int> BroadcastFrameAsync(string text, CancellationToken token, WebPage? page = null)
    {
        Connection[] targets;
        lock (Gate) targets = connections.Where(connection => page is null || connection.Page == page).ToArray();
        var results = await Task.WhenAll(targets.Select(connection => SendAsync(connection, text, token)));
        return results.Count(sent => sent);
    }

    private async Task<bool> SendAsync(Connection connection, string text, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), timeProvider);
        using var sending = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var acquired = false;
        try
        {
            await connection.WriteLock.WaitAsync(sending.Token);
            acquired = true;
            await connection.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(text), sending.Token);
            await connection.Response.OutputStream.FlushAsync(sending.Token);
            return true;
        }
        catch (Exception error) when (error is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
            lock (Gate)
            {
                if (connections.Remove(connection)) ConnectionsChanged();
                connection.StreamStop?.Cancel();
                Abort(connection.Response);
            }
            return false;
        }
        finally { if (acquired) connection.WriteLock.Release(); }
    }

    private async Task HeartbeatsAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), timeProvider, token);
                await BroadcastFrameAsync(Heartbeat, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task AcceptAsync(HttpListener server, CancellationToken token)
    {
        var requests = new HashSet<Task>();
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Close() ends the pending accept; cancelling only a wrapper would leave its failure unobserved.
                var context = await server.GetContextAsync();
                requests.RemoveWhere(task => task.IsCompleted);
                requests.Add(HandleAsync(context, token));
            }
        }
        catch (Exception error) when (token.IsCancellationRequested && error is HttpListenerException or ObjectDisposedException or OperationCanceledException) { }
        finally { await Task.WhenAll(requests); }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
    {
        var (request, response) = (context.Request, context.Response);
        var keepOpen = false;
        try
        {
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            var host = request.Headers["Host"];
            // Browsers omit the default HTTP port from Host even when the URL explicitly contains :80.
            var localHost = string.Equals(host, status.Origin["http://".Length..], StringComparison.OrdinalIgnoreCase)
                || status.Port == 80 && string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
            if (!localHost
                || request.RemoteEndPoint is not { Address: var address } || !IPAddress.IsLoopback(address))
            { response.StatusCode = 403; return; }
            var path = request.Url!.AbsolutePath;
            var name = path.Split('/', 3)[1];
            var page = pages.FirstOrDefault(page => page.Name == name);
            if (page is null) { response.StatusCode = 404; return; }
            if (request.HttpMethod == "GET" && path == $"/{page.Name}/events")
            {
                response.ContentType = "text/event-stream";
                response.SendChunked = true;
                var connection = new Connection(response, page);
                var events = page.EventsAsync(token);
                using var streamStop = events is not null ? CancellationTokenSource.CreateLinkedTokenSource(token) : null;
                connection.StreamStop = streamStop;
                try
                {
                    lock (Gate) { token.ThrowIfCancellationRequested(); connections.Add(connection); ConnectionsChanged(); }
                    keepOpen = await SendAsync(connection, Retry, token);
                    if (keepOpen && streamStop is not null)
                    {
                        // A page's stream lives in this request, so the response closes when it ends.
                        keepOpen = false;
                        await foreach (var message in events!.WithCancellation(streamStop.Token))
                            if (!await SendAsync(connection, Frame(message), token)) break;
                    }
                }
                finally
                {
                    // A heartbeat that fails on this connection cancels StreamStop under Gate; clear it before the using disposes it.
                    lock (Gate)
                    {
                        connection.StreamStop = null;
                        if (streamStop is not null && connections.Remove(connection)) ConnectionsChanged();
                    }
                }
            }
            else if (!await page.HandleAsync(request, response, token)) response.StatusCode = 404;
        }
        // On macOS, RemoteEndPoint throws SocketException once the client has reset the connection.
        catch (Exception error) when (error is IOException or HttpListenerException or SocketException or ObjectDisposedException or OperationCanceledException or UnauthorizedAccessException)
        { Abort(response); }
        finally
        {
            // http.sys can still fail the final write here once the client has gone.
            if (!keepOpen)
                try { response.Close(); }
                catch (Exception error) when (error is IOException or HttpListenerException or ObjectDisposedException) { Abort(response); }
        }
    }

    /// <summary>A request still finishing after StopAsync closed the listener has nothing left to abort on Windows.</summary>
    private static void Abort(HttpListenerResponse response)
    {
        try { response.Abort(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        subscribers.Complete();
    }
}
