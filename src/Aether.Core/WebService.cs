using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The shell owns the service lifetime; each page has its own connections.</summary>
public sealed class WebService : IDisposable
{
    internal object Gate { get; } = new();
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly List<Channel<WebServiceUpdate>> subscribers = [];
    private readonly HashSet<Page> pages = [];
    private WebServiceStatus status = new(false, Settings.WebPort.Initial.Value, 0, 0);
    private HttpListener? listener;
    private CancellationTokenSource? stop;
    private Task running = Task.CompletedTask;
    public VideoPage Video { get; }

    public WebService(string dataDirectory, TimeProvider timeProvider, ILogger logger)
    {
        this.timeProvider = timeProvider;
        this.logger = logger;
        Video = new(this, dataDirectory, timeProvider);
    }

    private sealed class Page(HttpListenerResponse response, string path)
    {
        public HttpListenerResponse Response { get; } = response;
        public string Path { get; } = path;
        public SemaphoreSlim WriteLock { get; } = new(1);
    }

    public string Address => $"http://localhost:{Status.Port}/";

    public WebServiceStatus Status { get { lock (Gate) return status; } }

    /// <summary>Subscribe independently of WatchAsync. The first update is the current page status.</summary>
    public async IAsyncEnumerable<WebServiceUpdate> GetUpdatesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // ponytail: unbounded like WatchAsync; bound this if a shell ever keeps a subscription without reading it.
        var updates = Channel.CreateUnbounded<WebServiceUpdate>();
        lock (Gate) { subscribers.Add(updates); updates.Writer.TryWrite(status); }
        try
        {
            await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken)) yield return update;
        }
        finally { lock (Gate) subscribers.Remove(updates); }
    }

    /// <summary>Only a changed port restarts existing connections.</summary>
    public async Task StartAsync(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        lock (Gate) if (stop is not null && status.Port == port) return;
        await StopAsync().ConfigureAwait(false);
        lock (Gate)
        {
            status = new(false, port, 0, 0);
            var server = new HttpListener();
            server.Prefixes.Add(Address);
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
            running = Task.Run(() => Task.WhenAll(AcceptAsync(server, token), HeartbeatsAsync(token), Video.RunAsync(token)));
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
            foreach (var page in pages) Abort(page.Response);
            listener!.Close();
            pages.Clear();
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

    internal void Publish(WebServiceUpdate update)
    {
        lock (Gate) foreach (var subscriber in subscribers) subscriber.Writer.TryWrite(update);
    }

    private void PageCountChanged()
    {
        status = status with { VideoConnections = pages.Count(page => page.Path == "/video/events"),
            LotteryConnections = pages.Count(page => page.Path == "/lottery/events") };
        logger.LogInformation("网页服务：视频页 {VideoCount} 个推流软件，抽奖页 {LotteryCount} 个推流软件", status.VideoConnections, status.LotteryConnections);
        Publish(status);
        if (status.VideoConnections == 0) Video.Disconnected();
    }

    internal async Task<int> BroadcastAsync(string text, CancellationToken token, string? path = null)
    {
        Page[] targets;
        lock (Gate) targets = pages.Where(page => path is null || page.Path == path).ToArray();
        var results = await Task.WhenAll(targets.Select(page => SendAsync(page, text, token)));
        return results.Count(sent => sent);
    }

    private async Task<bool> SendAsync(Page page, string text, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), timeProvider);
        using var sending = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var acquired = false;
        try
        {
            await page.WriteLock.WaitAsync(sending.Token);
            acquired = true;
            await page.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(text), sending.Token);
            await page.Response.OutputStream.FlushAsync(sending.Token);
            return true;
        }
        catch (Exception error) when (error is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
            lock (Gate)
            {
                if (pages.Remove(page)) PageCountChanged();
                Abort(page.Response);
            }
            return false;
        }
        finally { if (acquired) page.WriteLock.Release(); }
    }

    private async Task HeartbeatsAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), timeProvider, token);
                await BroadcastAsync(": heartbeat\n\n", token);
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
            var localHost = string.Equals(host, $"localhost:{status.Port}", StringComparison.OrdinalIgnoreCase)
                || status.Port == 80 && string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
            if (!localHost
                || request.RemoteEndPoint is not { Address: var address } || !IPAddress.IsLoopback(address))
            { response.StatusCode = 403; return; }
            var path = request.Url!.AbsolutePath;
            if (request.HttpMethod == "GET" && path is "/video/events" or "/lottery/events")
            {
                response.ContentType = "text/event-stream";
                response.SendChunked = true;
                var page = new Page(response, path);
                lock (Gate) { token.ThrowIfCancellationRequested(); pages.Add(page); PageCountChanged(); }
                keepOpen = await SendAsync(page, "retry: 2000\n\n", token);
            }
            else if (path.StartsWith("/video", StringComparison.Ordinal))
                await Video.HandleAsync(request, response, token);
            else if (request.HttpMethod == "GET" && path == "/lottery")
            {
                response.ContentType = "text/html; charset=utf-8";
                var html = Encoding.UTF8.GetBytes("<!doctype html><html><meta charset=\"utf-8\"><title>Aether 抽奖页</title><body style=\"margin:0;background:transparent\"><script>new EventSource('/lottery/events');</script></body></html>");
                response.ContentLength64 = html.Length;
                await response.OutputStream.WriteAsync(html, token);
            }
            else response.StatusCode = 404;
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
        lock (Gate)
        {
            foreach (var subscriber in subscribers) subscriber.Writer.TryComplete();
            subscribers.Clear();
        }
    }
}
