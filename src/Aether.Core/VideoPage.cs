using System.Net;
using System.Text.Json;
using System.Threading.Channels;

namespace Aether.Core;

/// <summary>Serial playback by file and source; viewer selection belongs to the caller.</summary>
public sealed class VideoPage(WebService service, string dataDirectory, TimeProvider timeProvider)
{
    // shortcut: pending videos are unbounded; add a capacity if playback sources can flood the queue.
    private readonly Channel<Playback> queue = Channel.CreateUnbounded<Playback>(new UnboundedChannelOptions { SingleReader = true });
    private Playback? current;
    private TaskCompletionSource<(PlaybackState State, string? Reason)>? reported;
    private sealed record Playback(string Id, string FileName, string Source, long ReceivedAt,
        Func<string?>? SkipReason, Action<VideoPlayback>? Updated);

    public void Enqueue(string fileName, string source) => Enqueue(fileName, source, timeProvider.GetTimestamp(), null, null);

    internal void Enqueue(string fileName, string source, long receivedAt, Func<string?>? skipReason, Action<VideoPlayback>? updated)
    {
        if (Settings.ValidateVideoFileName(fileName) is { } error) throw new ArgumentException(error, nameof(fileName));
        lock (service.Gate)
        {
            var item = new Playback(Guid.NewGuid().ToString("N"), fileName, source, receivedAt, skipReason, updated);
            if (!service.Status.Running) Report(item, PlaybackState.Skipped, service.Status.Error ?? "网页服务未运行");
            else if (service.Status.VideoConnections == 0) Report(item, PlaybackState.Skipped, "没有推流软件连着");
            else { Report(item, PlaybackState.Queued); queue.Writer.TryWrite(item); }
        }
    }

    internal void Disconnected() => reported?.TrySetResult((PlaybackState.NoReport, "推流软件已全部断开"));

    private void Report(Playback item, PlaybackState state, string? reason = null)
    {
        var update = new VideoPlayback(item.Id, item.FileName, item.Source, state, reason);
        lock (service.Gate) { service.Publish(update); item.Updated?.Invoke(update); }
    }

    private string VideoPath(string fileName) => Settings.VideoFile(dataDirectory, fileName);

    internal async Task RunAsync(CancellationToken token)
    {
        var reader = queue.Reader;
        try
        {
            await foreach (var item in reader.ReadAllAsync(token))
            {
                TaskCompletionSource<(PlaybackState State, string? Reason)> completion;
                var exists = File.Exists(VideoPath(item.FileName));
                lock (service.Gate)
                {
                    var reason = timeProvider.GetElapsedTime(item.ReceivedAt) > TimeSpan.FromSeconds(30) ? "排队超过 30 秒"
                        : service.Status.VideoConnections == 0 ? "没有推流软件连着"
                        : !exists ? "视频文件不存在"
                        : item.SkipReason?.Invoke();
                    if (reason is not null) { Report(item, PlaybackState.Skipped, reason); continue; }
                    current = item;
                    reported = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                // Once pushed, the room ending does not cut the video short: pages outlive room connections (ADR 0006).
                try
                {
                    var payload = JsonSerializer.Serialize(new { id = item.Id });
                    var sent = await service.BroadcastAsync("data: " + payload + "\n\n", token, "/video/events");
                    if (sent == 0) { Report(item, PlaybackState.Skipped, "没有推流软件连着"); continue; }
                    Report(item, PlaybackState.Playing);
                    try
                    {
                        var result = await completion.Task.WaitAsync(TimeSpan.FromMinutes(2), timeProvider, token);
                        Report(item, result.State, result.Reason);
                    }
                    catch (TimeoutException) { Report(item, PlaybackState.NoReport, "等了 2 分钟"); }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { Report(item, PlaybackState.Skipped, "视频页已停止"); }
                finally { lock (service.Gate) { current = null; reported = null; } }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            while (reader.TryRead(out var item)) Report(item, PlaybackState.Skipped, "视频页已停止");
        }
    }

    internal async Task HandleAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        var path = request.Url!.AbsolutePath;
        if (request.HttpMethod == "GET" && path == "/video")
        {
            response.ContentType = "text/html; charset=utf-8";
            using var html = typeof(VideoPage).Assembly.GetManifestResourceStream("Aether.Core.VideoPage.html")!;
            response.ContentLength64 = html.Length;
            await html.CopyToAsync(response.OutputStream, token);
        }
        else if (request.HttpMethod == "GET" && path.StartsWith("/video/files/", StringComparison.Ordinal))
        {
            string? fileName;
            lock (service.Gate) fileName = current is { } item && item.Id == path[13..] ? item.FileName : null;
            if (fileName is null) { response.StatusCode = 404; return; }
            try
            {
                await using var file = new FileStream(VideoPath(fileName), FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                response.ContentType = Path.GetExtension(fileName).Equals(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm" : "video/mp4";
                response.ContentLength64 = file.Length;
                await file.CopyToAsync(response.OutputStream, token);
            }
            catch (FileNotFoundException) { response.StatusCode = 404; }
            catch (DirectoryNotFoundException) { response.StatusCode = 404; }
        }
        else if (request.HttpMethod == "POST" && path == "/video/report") await ReceiveReportAsync(request, response, token);
        else response.StatusCode = 404;
    }

    private sealed record PageReport(string? Id, bool? Success, string? Reason);

    private async Task ReceiveReportAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        if (request.Headers["Origin"] is { } origin && origin != new Uri(service.Address).GetLeftPart(UriPartial.Authority))
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
            lock (service.Gate)
                if (current?.Id == result.Id)
                    reported?.TrySetResult(result.Success.Value
                        ? (PlaybackState.Completed, null)
                        : (PlaybackState.Failed, result.Reason ?? "视频页播放失败"));
            response.StatusCode = 204;
        }
        catch (JsonException) { response.StatusCode = 400; }
    }

}
