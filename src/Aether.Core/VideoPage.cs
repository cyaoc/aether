using System.Net;
using System.Text.Json;
using System.Threading.Channels;

namespace Aether.Core;

/// <summary>The video page, outside the web service region: it plays <see cref="VideoQueue"/> one item at a time, serves the file
/// playing and takes each page's report, handing the region a step whenever it touches the queue.</summary>
internal sealed class VideoPage(WebServiceFacade service, Region<WebService> region, ChannelReader<VideoQueue.Playback> queue,
    string dataDirectory, TimeProvider timeProvider) : WebPage("video")
{
    private const string FileRoutePrefix = "/video/files/";

    private string VideoPath(string fileName) => Settings.VideoFile(dataDirectory, fileName);

    internal override async Task RunAsync(CancellationToken token)
    {
        try
        {
            await foreach (var item in queue.ReadAllAsync(token)) await PlayAsync(item, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            while (queue.TryRead(out var item)) Report(item, PlaybackState.Skipped, "视频页已停止");
        }
    }

    private async Task PlayAsync(VideoQueue.Playback item, CancellationToken token)
    {
        var exists = item.Content is not PlaybackContent.Video file || File.Exists(VideoPath(file.FileName));
        var report = new TaskCompletionSource<(PlaybackState State, string? Reason)>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!await region.Run(s => s.Video.Play(item, exists, report))) return;
        // Once pushed, the room ending does not cut the video short: pages outlive room connections (ADR 0006).
        try
        {
            var volume = service.Settings!.EntryAnimationVolume.Value;
            object payload = item.Content is PlaybackContent.Animation animation
                ? new { id = item.Id, animation = animation.Identifier, nickname = animation.Nickname, volume }
                : new { id = item.Id, volume };
            var sent = await service.BroadcastAsync(this, new(null, payload), token);
            if (sent == 0) { Report(item, PlaybackState.Skipped, "没有推流软件连着"); return; }
            Report(item, PlaybackState.Playing);
            try
            {
                var (state, reason) = await report.Task.WaitAsync(TimeSpan.FromMinutes(2), timeProvider, token);
                if (state == PlaybackState.Stopped)
                    await service.BroadcastAsync(this, new("stop", new { id = item.Id }), token);
                Report(item, state, reason);
            }
            catch (TimeoutException) { Report(item, PlaybackState.NoReport, "等了 2 分钟"); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Report(item, PlaybackState.Skipped, "视频页已停止"); }
        finally { region.Post(s => s.Video.Played()); }
    }

    private void Report(VideoQueue.Playback item, PlaybackState state, string? reason = null) =>
        region.Post(s => s.Video.Report(item, state, reason));

    internal override async Task<bool> HandleAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        var path = request.Url!.AbsolutePath;
        if (request.HttpMethod == "GET" && path.StartsWith(FileRoutePrefix, StringComparison.Ordinal))
        {
            var id = path[FileRoutePrefix.Length..];
            if (await region.Run(s => s.Video.PlayingFile(id)) is not { } fileName) return false;
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
        else return false;
        return true;
    }

    private sealed record PageReport(string? Id, bool? Success, string? Reason);

    private async Task ReceiveReportAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        if (request.Headers["Origin"] is { } origin && origin != new Uri(service.Status.Origin).GetLeftPart(UriPartial.Authority))
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
            var result = JsonSerializer.Deserialize<PageReport>(body.AsSpan(0, length), WebServiceFacade.Json);
            if (result is not { Id: { } id, Success: { } success } || result.Reason?.Length > 300)
            { response.StatusCode = 400; return; }
            var (state, reason) = success ? (PlaybackState.Completed, null) : (PlaybackState.Failed, result.Reason ?? "视频页播放失败");
            region.Post(s => s.Video.End(id, state, reason));
            response.StatusCode = 204;
        }
        catch (JsonException) { response.StatusCode = 400; }
    }
}
