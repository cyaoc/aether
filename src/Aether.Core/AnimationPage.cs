using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The shell owns its lifetime; room connections only enqueue entries. All playback decisions live here.</summary>
public sealed class AnimationPage(string dataDirectory, TimeProvider timeProvider, ILogger logger) : IDisposable
{
    private readonly object gate = new();
    private readonly List<Channel<AnimationPageUpdate>> subscribers = [];
    private readonly HashSet<Page> pages = [];
    private AnimationPageStatus status = new(false, Settings.AnimationPort.Initial.Value, 0);
    private Settings? settings;
    private HttpListener? listener;
    private CancellationTokenSource? stop;
    private Task running = Task.CompletedTask;
    private Channel<Playback>? queue;
    private Playback? current;
    private TaskCompletionSource<(WelcomeAnimationState State, string? Reason)>? reported;

    private sealed record Playback(WelcomeAnimationPlayback Update, long ReceivedAt,
        Dictionary<long, long>? LastPlayed, CancellationToken RoomStopped);

    private sealed class Page(HttpListenerResponse response)
    {
        public HttpListenerResponse Response { get; } = response;
        public SemaphoreSlim WriteLock { get; } = new(1);
    }

    public string Address { get { lock (gate) return $"http://localhost:{status.Port}/"; } }

    public AnimationPageStatus Status { get { lock (gate) return status; } }

    /// <summary>Subscribe independently of WatchAsync. The first update is the current page status.</summary>
    public async IAsyncEnumerable<AnimationPageUpdate> GetUpdatesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // shortcut: unbounded like WatchAsync; bound this if a shell ever keeps a subscription without reading it.
        var updates = Channel.CreateUnbounded<AnimationPageUpdate>();
        lock (gate) { subscribers.Add(updates); updates.Writer.TryWrite(status); }
        try
        {
            await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken)) yield return update;
        }
        finally { lock (gate) subscribers.Remove(updates); }
    }

    /// <summary>Start from a settings snapshot. Call StopAsync before applying a different snapshot.</summary>
    public void Start(Settings snapshot)
    {
        if (snapshot.Error is { } error) throw error;
        lock (gate)
        {
            if (stop is not null) throw new InvalidOperationException("请先停掉动画页，再重新启动。");
            settings = snapshot;
            status = new(false, snapshot.WelcomeAnimationPort.Value, 0);
            if (!snapshot.WelcomeAnimationEnabled.Value) { Publish(status); return; }
            var server = new HttpListener();
            server.Prefixes.Add(Address);
            try { server.Start(); }
            catch (HttpListenerException exception)
            {
                server.Close();
                // EADDRINUSE (macOS 48, Linux 98, WSA 10048); http.sys 32 (port held by another program) and
                // 183 (prefix already registered); managed HttpListener 400 (prefix already registered).
                var reason = exception.ErrorCode is 48 or 98 or 10048 or 32 or 183 or 400
                    ? $"端口 {status.Port} 被占用" : $"动画页启动失败（端口 {status.Port}，错误码 {exception.ErrorCode}）：{exception.Message}";
                status = status with { Error = reason };
                logger.LogWarning("动画页没有运行：{Reason}", reason);
                Publish(status);
                return;
            }
            listener = server;
            stop = new CancellationTokenSource();
            // shortcut: pending entries are unbounded; add a capacity if mapped viewers can flood the queue.
            queue = Channel.CreateUnbounded<Playback>(new UnboundedChannelOptions { SingleReader = true });
            status = status with { Running = true };
            Publish(status);
            var token = stop.Token;
            running = Task.Run(() => Task.WhenAll(AcceptAsync(server, token), HeartbeatsAsync(token), PlayAsync(queue.Reader, token)));
        }
    }

    /// <summary>Uses the normal playback queue, without checking or updating any room's ten-minute interval.</summary>
    public void TestPlay(long uid) => Enqueue(uid, timeProvider.GetTimestamp(), null, CancellationToken.None);

    internal void Enqueue(long uid, long receivedAt, Dictionary<long, long>? lastPlayed, CancellationToken roomStopped)
    {
        lock (gate)
        {
            if (settings is null || !settings.WelcomeAnimationVideos.TryGetValue(uid, out var video)) return;
            var item = new Playback(new(Guid.NewGuid().ToString("N"), uid, video.Value, lastPlayed is null,
                WelcomeAnimationState.Queued), receivedAt, lastPlayed, roomStopped);
            if (!status.Running) Report(item, WelcomeAnimationState.Skipped, status.Error ?? "动画页未运行");
            else if (pages.Count == 0) Report(item, WelcomeAnimationState.Skipped, "没有推流软件连着");
            else
            {
                Report(item, WelcomeAnimationState.Queued);
                queue!.Writer.TryWrite(item);
            }
        }
    }

    public async Task StopAsync()
    {
        Task pending;
        CancellationTokenSource? stopping;
        lock (gate)
        {
            stopping = stop;
            if (stopping is null) return;
            stopping.Cancel();
            // Abort before Close: on Windows, Close releases the request queue that Abort needs.
            foreach (var page in pages) page.Response.Abort();
            listener!.Close();
            pages.Clear();
            queue!.Writer.TryComplete();
            status = status with { Running = false, ConnectedPages = 0 };
            Publish(status);
            pending = running;
        }
        try { await pending.ConfigureAwait(false); }
        finally
        {
            // Clear even after a fault, or Start would refuse forever.
            lock (gate)
                if (stop == stopping)
                {
                    stop = null;
                    listener = null;
                    stopping.Dispose();
                }
        }
    }

    private async Task PlayAsync(ChannelReader<Playback> reader, CancellationToken token)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(token))
            {
                TaskCompletionSource<(WelcomeAnimationState State, string? Reason)> completion;
                lock (gate)
                {
                    var reason = item.RoomStopped.IsCancellationRequested ? "直播间连接结束"
                        : timeProvider.GetElapsedTime(item.ReceivedAt) > TimeSpan.FromSeconds(30) ? "排队超过 30 秒"
                        : pages.Count == 0 ? "没有推流软件连着"
                        : !File.Exists(VideoPath(item.Update.FileName)) ? "视频文件不存在"
                        : item.LastPlayed?.TryGetValue(item.Update.Uid, out var last) == true
                            && timeProvider.GetElapsedTime(last) < TimeSpan.FromMinutes(10) ? "10 分钟内播过" : null;
                    if (reason is not null) { Report(item, WelcomeAnimationState.Skipped, reason); continue; }
                    current = item;
                    reported = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                // Once pushed, the room ending does not cut the video short: pages outlive room connections (ADR 0006).
                try
                {
                    var payload = JsonSerializer.Serialize(new { id = item.Update.Id, uid = item.Update.Uid.ToString(CultureInfo.InvariantCulture) });
                    var sent = await BroadcastAsync("data: " + payload + "\n\n", token);
                    if (sent == 0) { Report(item, WelcomeAnimationState.Skipped, "没有推流软件连着"); continue; }
                    if (item.LastPlayed is { } played) played[item.Update.Uid] = timeProvider.GetTimestamp();
                    Report(item, WelcomeAnimationState.Playing);
                    try
                    {
                        var result = await completion.Task.WaitAsync(TimeSpan.FromMinutes(2), timeProvider, token);
                        Report(item, result.State, result.Reason);
                    }
                    catch (TimeoutException) { Report(item, WelcomeAnimationState.Failed, "等待播放回报超过 2 分钟"); }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { Report(item, WelcomeAnimationState.Skipped, "动画页已停止"); }
                finally { lock (gate) { current = null; reported = null; } }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            while (reader.TryRead(out var item)) Report(item, WelcomeAnimationState.Skipped, "动画页已停止");
        }
    }

    private string VideoPath(string fileName) => Settings.WelcomeAnimationFile(dataDirectory, fileName);

    private void Report(Playback item, WelcomeAnimationState state, string? reason = null)
    {
        var update = item.Update with { State = state, Reason = reason };
        var progress = state switch
        {
            WelcomeAnimationState.Queued => "排队中", WelcomeAnimationState.Playing => "播放中",
            WelcomeAnimationState.Completed => "已播放", WelcomeAnimationState.Failed => "播放失败", _ => "没播"
        };
        logger.Log(state == WelcomeAnimationState.Failed ? LogLevel.Warning : LogLevel.Information,
            "[欢迎动画 {PlaybackId}] 观众 {Uid}，{FileName}，试播={IsTest}：{Progress} {Reason}",
            update.Id, update.Uid, update.FileName, update.IsTest, progress, reason);
        lock (gate) Publish(update);
    }

    private void Publish(AnimationPageUpdate update)
    {
        foreach (var subscriber in subscribers) subscriber.Writer.TryWrite(update);
    }

    private void PageCountChanged()
    {
        status = status with { ConnectedPages = pages.Count };
        logger.LogInformation("动画页：已有 {Count} 个推流软件连着", pages.Count);
        Publish(status);
        if (pages.Count == 0) reported?.TrySetResult((WelcomeAnimationState.Skipped, "推流软件已全部断开"));
    }

    private async Task<int> BroadcastAsync(string text, CancellationToken token)
    {
        Page[] targets;
        lock (gate) targets = pages.ToArray();
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
            lock (gate)
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
            if (request.HttpMethod == "GET" && path == "/")
            {
                response.ContentType = "text/html; charset=utf-8";
                using var html = typeof(AnimationPage).Assembly.GetManifestResourceStream("Aether.Core.AnimationPage.html")!;
                response.ContentLength64 = html.Length;
                await html.CopyToAsync(response.OutputStream, token);
            }
            else if (request.HttpMethod == "GET" && path == "/events")
            {
                response.ContentType = "text/event-stream";
                response.SendChunked = true;
                var page = new Page(response);
                lock (gate) { token.ThrowIfCancellationRequested(); pages.Add(page); PageCountChanged(); }
                keepOpen = await SendAsync(page, "retry: 2000\n\n", token);
            }
            else if (request.HttpMethod == "GET" && path.StartsWith("/videos/", StringComparison.Ordinal)
                && long.TryParse(path[8..], NumberStyles.None, CultureInfo.InvariantCulture, out var uid)
                && settings!.WelcomeAnimationVideos.TryGetValue(uid, out var video))
            {
                try
                {
                    await using var file = new FileStream(VideoPath(video.Value), FileMode.Open, FileAccess.Read, FileShare.Read,
                        81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    response.ContentType = Path.GetExtension(video.Value).Equals(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm" : "video/mp4";
                    response.ContentLength64 = file.Length;
                    await file.CopyToAsync(response.OutputStream, token);
                }
                catch (FileNotFoundException) { response.StatusCode = 404; }
                catch (DirectoryNotFoundException) { response.StatusCode = 404; }
            }
            else if (request.HttpMethod == "POST" && path == "/report")
                await ReceiveReportAsync(request, response, token);
            else response.StatusCode = 404;
        }
        catch (Exception error) when (error is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException or UnauthorizedAccessException)
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

    private sealed record PageReport(string? Id, bool? Success, string? Reason);

    private async Task ReceiveReportAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        if (request.Headers["Origin"] is { } origin && origin != new Uri(Address).GetLeftPart(UriPartial.Authority))
        { response.StatusCode = 403; return; }
        if (!request.HasEntityBody || request.ContentLength64 > 4096
            || request.ContentType?.Split(';')[0] != "application/json") { response.StatusCode = 400; return; }
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), timeProvider);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            var body = new byte[4097];
            var length = await request.InputStream.ReadAtLeastAsync(body, body.Length, throwOnEndOfStream: false, timeout.Token);
            if (length > 4096) { response.StatusCode = 400; return; }
            var result = JsonSerializer.Deserialize<PageReport>(body.AsSpan(0, length), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (result is not { Id: not null, Success: not null } || result.Reason?.Length > 300)
            { response.StatusCode = 400; return; }
            lock (gate)
                if (current?.Update.Id == result.Id)
                    reported?.TrySetResult(result.Success.Value
                        ? (WelcomeAnimationState.Completed, null)
                        : (WelcomeAnimationState.Failed, result.Reason ?? "动画页播放失败"));
            response.StatusCode = 204;
        }
        catch (JsonException) { response.StatusCode = 400; }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        lock (gate)
        {
            foreach (var subscriber in subscribers) subscriber.Writer.TryComplete();
            subscribers.Clear();
        }
    }
}
