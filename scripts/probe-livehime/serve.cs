// Probes what 直播姬's browser source can do for 欢迎动画: codecs, sound autoplay, WebGL keying, SSE reconnects.
//
// Serves index.html and clips/ (made by make-clips.sh), pushes plays to the page over SSE, prints what the page reports.
// Listens on all interfaces so 直播姬 on another machine can open it; the real 动画页 will listen on localhost only.
//
//   cd scripts/probe-livehime && sh make-clips.sh && dotnet run serve.cs
//   curl localhost:17890/play                          # next clip in name order
//   curl 'localhost:17890/play?clip=2-alpha.webm'
// Pressing Enter in the terminal also plays the next clip.
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

const int Port = 17890;
var root = Environment.CurrentDirectory;
var clients = new ConcurrentDictionary<HttpListenerResponse, string>();
var next = 0;

var listener = new HttpListener();
listener.Prefixes.Add($"http://*:{Port}/");
listener.Start();
Log($"serving {root}");
foreach (var n in NetworkInterface.GetAllNetworkInterfaces()
    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
    foreach (var a in n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
        Log($"{n.Name}: 直播姬里填 http://{a.Address}:{Port}/");

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        Broadcast(": ping\n\n");
    }
});
_ = Task.Run(() =>
{
    while (Console.ReadLine() is not null) Play(null);
});

while (true)
{
    var context = await listener.GetContextAsync();
    _ = Task.Run(() => Handle(context));
}

async Task Handle(HttpListenerContext context)
{
    var (request, response) = (context.Request, context.Response);
    var path = request.Url!.AbsolutePath;
    try
    {
        if (path == "/events")
        {
            response.ContentType = "text/event-stream";
            response.Headers["Cache-Control"] = "no-store";
            response.SendChunked = true;
            clients[response] = request.UserAgent ?? "";
            Log($"SSE connected from {request.RemoteEndPoint} ({clients.Count} connected): {request.UserAgent}");
            Send(response, "retry: 2000\n\n");
            return; // Kept open; Broadcast writes to it and drops it once a write fails.
        }
        if (path == "/report" && request.HttpMethod == "POST")
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            Log($"report from {request.RemoteEndPoint}: {await reader.ReadToEndAsync()}");
        }
        else if (path == "/play")
        {
            response.StatusCode = Play(request.QueryString["clip"]) is null ? 404 : 204;
        }
        else if (path == "/")
            await WriteFile(response, Path.Combine(root, "index.html"), "text/html; charset=utf-8");
        else if (path.StartsWith("/clips/") && Clips().FirstOrDefault(c => c == Uri.UnescapeDataString(path[7..])) is { } name)
            await WriteFile(response, Path.Combine(root, "clips", name), name.EndsWith(".webm") ? "video/webm" : "video/mp4");
        else
            response.StatusCode = 404;
        response.Close();
    }
    catch (Exception e)
    {
        Log($"{request.HttpMethod} {path} failed: {e.Message}");
        response.Abort();
    }
}

string? Play(string? clip)
{
    var clips = Clips();
    clip ??= clips.Length == 0 ? null : clips[next++ % clips.Length];
    if (clip is null || !clips.Contains(clip)) return null;
    Log($"play {clip} → {clients.Count} connected");
    Broadcast($"data: {{\"clip\":\"{clip}\"}}\n\n");
    return clip;
}

string[] Clips() => Directory.Exists(Path.Combine(root, "clips"))
    ? Directory.GetFiles(Path.Combine(root, "clips")).Select(f => Path.GetFileName(f)).Where(n => n.EndsWith(".mp4") || n.EndsWith(".webm")).Order().ToArray()
    : [];

void Broadcast(string message)
{
    foreach (var client in clients.Keys)
        if (!Send(client, message) && clients.TryRemove(client, out _))
            Log($"SSE disconnected ({clients.Count} connected)");
}

static bool Send(HttpListenerResponse response, string message)
{
    try
    {
        lock (response)
        {
            response.OutputStream.Write(Encoding.UTF8.GetBytes(message));
            response.OutputStream.Flush();
        }
        return true;
    }
    catch { return false; }
}

static async Task WriteFile(HttpListenerResponse response, string file, string type)
{
    response.ContentType = type;
    response.Headers["Cache-Control"] = "no-store";
    await using var stream = File.OpenRead(file);
    response.ContentLength64 = stream.Length;
    await stream.CopyToAsync(response.OutputStream);
}

static void Log(string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
