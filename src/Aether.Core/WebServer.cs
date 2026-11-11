using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Connection = Aether.Core.WebService.Connection;

namespace Aether.Core;

/// <summary>The web service outside its region (ADR 0017): starting and stopping the listener, accepting requests, serving the
/// pages' files and events, writing to each streaming software and heartbeats. These flows, and the pages they run, hand
/// <see cref="Region"/> a step whenever they touch its state, and read its snapshots.</summary>
internal sealed class WebServer
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string Retry = "retry: 2000\n\n";
    private const string Heartbeat = ": heartbeat\n\n";
    private const string CurrentCharacter = "/characters/current/";
    // Both sides go through GetFullPath so Windows expands 8.3 short names in the program directory alike.
    internal static readonly string PagesDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Pages")) + Path.DirectorySeparatorChar;
    private readonly IReadOnlyList<WebPage> pages;
    private readonly Func<WebServiceStatus> status;
    private readonly Func<Settings?> settings;

    /// <param name="pages">Makes the pages this server serves, given the server, which the video page plays through.</param>
    public WebServer(Region<WebService> region, TimeProvider time, Func<WebServer, IReadOnlyList<WebPage>> pages)
    {
        Region = region;
        Time = time;
        status = region.Snapshot(s => s.Status);
        settings = region.Snapshot(s => s.Settings);
        this.pages = pages(this);
    }

    public Region<WebService> Region { get; }

    public TimeProvider Time { get; }

    public WebServiceStatus Status => status();

    /// <inheritdoc cref="WebService.Settings"/>
    public Settings? Settings => settings();

    /// <summary>Applies feature settings immediately; only a port change restarts the web service.</summary>
    public async Task ApplySettingsAsync(Settings settings)
    {
        if (settings.Error is { } error) throw error;
        await Region.Run(s => s.Apply(settings)).ConfigureAwait(false);
        await StartAsync(settings.WebServicePort.Value).ConfigureAwait(false);
    }

    /// <summary>Only a changed port restarts existing connections.</summary>
    public async Task StartAsync(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (await Region.Run(s => s.Serves(port)).ConfigureAwait(false)) return;
        await StopAsync().ConfigureAwait(false);
        await Region.Run(s => s.Start(port, ServeAsync)).ConfigureAwait(false);
    }

    /// <summary>Returns once the listener and every page have stopped, so a shell can wait for it before it exits.</summary>
    public async Task StopAsync()
    {
        // The step hands out the serving it stopped, to wait for here: the step itself does not await.
        if (await Region.Run<Task?>(s => s.Stop()).ConfigureAwait(false) is not { } stopping) return;
        try { await stopping.ConfigureAwait(false); }
        finally { await Region.Run(s => s.Stopped(stopping)).ConfigureAwait(false); }
    }

    /// <summary>Sends <paramref name="message"/> to every streaming software on <paramref name="page"/>, and counts those it
    /// reached.</summary>
    public Task<int> BroadcastAsync(WebPage page, SseEvent message, CancellationToken token) =>
        BroadcastFrameAsync(Frame(message), token, page);

    /// <summary>Whether a page's report comes from a page of this service: a browser names the site it posts from, and another
    /// site's page must not end what plays here.</summary>
    public bool FromItsPage(HttpListenerRequest request) =>
        request.Headers["Origin"] is not { } origin || origin == new Uri(Status.Origin).GetLeftPart(UriPartial.Authority);

    /// <summary>The request path is outside input: anything that resolves outside Pages counts as a missing file.</summary>
    internal static string? PagesFile(string path)
    {
        var file = Path.GetFullPath(Path.Join(PagesDirectory, path));
        return file.StartsWith(PagesDirectory, StringComparison.Ordinal) && File.Exists(file) ? file : null;
    }

    // Called in the Start step: Task.Run keeps every flow off the region's thread, and the empty step first lets the Start step
    // end, so requests are checked against the port it started on, not the snapshot from before it.
    private Task ServeAsync(HttpListener server, CancellationToken token) => Task.Run(async () =>
    {
        await Region.Run(_ => { });
        await Task.WhenAll(pages.Select(page => page.RunAsync(token)).Append(AcceptAsync(server, token)).Append(HeartbeatsAsync(token)));
    });

    private static string Frame(SseEvent message) =>
        (message.Name is null ? "" : "event: " + message.Name + "\n") + "data: " + JsonSerializer.Serialize(message.Data, Json) + "\n\n";

    private async Task<int> BroadcastFrameAsync(string text, CancellationToken token, WebPage? page = null)
    {
        var targets = await Region.Run(s => s.Connected(page));
        var results = await Task.WhenAll(targets.Select(connection => SendAsync(connection, text, token)));
        return results.Count(sent => sent);
    }

    /// <summary>The connection's write lock is the one lock left: a stalled streaming software holds up only its own writes, for
    /// at most 5 seconds, and is then dropped.</summary>
    private async Task<bool> SendAsync(Connection connection, string text, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), Time);
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
            Region.Post(s => s.Drop(connection));
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
                await Task.Delay(TimeSpan.FromSeconds(15), Time, token);
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
        // Windows may bind its request-queue handle just as StopAsync closes it, and a loop that comes back for the next request
        // after the close finds the listener no longer started.
        catch (Exception error) when (token.IsCancellationRequested && error is HttpListenerException or ObjectDisposedException or OperationCanceledException
            or InvalidOperationException or ArgumentException { ParamName: "handle" }) { }
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
            if (!IsThisHost(request.Headers["Host"])) { response.StatusCode = 403; return; }
            IPEndPoint? remoteEndPoint;
            try { remoteEndPoint = request.RemoteEndPoint; }
            // The managed listener can clear its socket while a disconnected request is being dispatched.
            catch (NullReferenceException) { WebService.Abort(response); return; }
            if (remoteEndPoint is not { Address: var address } || !IPAddress.IsLoopback(address))
            { response.StatusCode = 403; return; }
            var path = request.Url!.AbsolutePath;
            var name = path.Split('/', 3)[1];
            var page = pages.FirstOrDefault(page => page.Name == name);
            if (page is null) { if (!await ServeResourceAsync(request, response, CharacterFile(path), token)) response.StatusCode = 404; }
            else if (request.HttpMethod == "GET" && path == $"/{page.Name}/events") keepOpen = await ConnectAsync(page, response, token);
            else if (!await ServeResourceAsync(request, response, path == $"/{page.Name}" ? path + "/index.html" : path, token)
                && !await page.HandleAsync(request, response, token)) response.StatusCode = 404;
        }
        // On macOS, RemoteEndPoint throws SocketException once the client has reset the connection.
        catch (Exception error) when (error is IOException or HttpListenerException or SocketException or ObjectDisposedException or OperationCanceledException or UnauthorizedAccessException)
        { WebService.Abort(response); }
        finally
        {
            // http.sys can still fail the final write here once the client has gone.
            if (!keepOpen)
                try { response.Close(); }
                catch (Exception error) when (error is IOException or HttpListenerException or ObjectDisposedException) { WebService.Abort(response); }
        }
    }

    private bool IsThisHost(string? host)
    {
        var status = Status;
        // Browsers omit the default HTTP port from Host even when the URL explicitly contains :80.
        return string.Equals(host, status.Origin["http://".Length..], StringComparison.OrdinalIgnoreCase)
            || status.Port == 80 && string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Connects a streaming software to a page's events; true when the response stays open for later broadcasts.</summary>
    private async Task<bool> ConnectAsync(WebPage page, HttpListenerResponse response, CancellationToken token)
    {
        response.ContentType = "text/event-stream";
        response.SendChunked = true;
        var connection = new Connection(response, page);
        if (page.EventsAsync(token) is not { } events)
        {
            await Region.Run(s => s.Connect(connection, token));
            return await SendAsync(connection, Retry, token);
        }
        using var streamStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Set before the step that shares the connection.
        connection.StreamStop = streamStop;
        await Region.Run(s => s.Connect(connection, token));
        try
        {
            if (await SendAsync(connection, Retry, token))
                await foreach (var message in events.WithCancellation(streamStop.Token))
                    if (!await SendAsync(connection, Frame(message), token)) break;
        }
        finally { await Region.Run(s => s.EndStream(connection)); }
        // A page's stream lives in this request, so the response closes when it ends.
        return false;
    }

    // Pages ask for the current character's files by /characters/current/, whoever she is.
    private static string CharacterFile(string path) =>
        path.StartsWith(CurrentCharacter, StringComparison.Ordinal) ? $"/characters/{Character.CurrentNumber}/{path[CurrentCharacter.Length..]}" : path;

    private static async Task<bool> ServeResourceAsync(HttpListenerRequest request, HttpListenerResponse response, string path, CancellationToken token)
    {
        if (request.HttpMethod != "GET") return false;
        var contentType = Path.GetExtension(path) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".ogg" => "audio/ogg",
            ".woff2" => "font/woff2",
            ".webp" => "image/webp",
            ".ttf" => "font/ttf",
            ".txt" => "text/plain; charset=utf-8",
            _ => null
        };
        if (contentType is null || PagesFile(path) is not { } file) return false;
        await using var content = File.OpenRead(file);
        response.ContentType = contentType;
        response.ContentLength64 = content.Length;
        await content.CopyToAsync(response.OutputStream, token);
        return true;
    }
}
