using System.Threading.Channels;

namespace Aether.Core;

/// <summary>The video page's queue, in the web service region (ADR 0017): serial playback by file and source, what plays now and
/// what a page reported. <see cref="VideoPage"/> plays it outside the region; viewer selection belongs to the caller.</summary>
internal sealed class VideoQueue(WebService service, ChannelWriter<VideoQueue.Playback> queue, TimeProvider timeProvider)
{
    private Playback? current;
    private TaskCompletionSource<(PlaybackState State, string? Reason)>? reported;

    public sealed record Playback(string Id, PlaybackContent Content, string Source, long ReceivedAt,
        Func<string?>? SkipReason, Action<VideoPlayback>? Updated);

    public void Enqueue(string fileName, string source) => Enqueue(new PlaybackContent.Video(fileName), source, timeProvider.GetTimestamp(), null, null);

    public void Enqueue(PlaybackContent content, string source, long receivedAt, Func<string?>? skipReason, Action<VideoPlayback>? updated)
    {
        if (content is PlaybackContent.Video video && Settings.ValidateVideoFileName(video.FileName) is { } error) throw new ArgumentException(error, nameof(content));
        var item = new Playback(Ids.New(), content, source, receivedAt, skipReason, updated);
        if (!service.Status.Running) Report(item, PlaybackState.Skipped, service.Status.Error ?? "网页服务未运行");
        else if (service.Status.VideoConnections == 0) Report(item, PlaybackState.Skipped, "没有推流软件连着");
        else { Report(item, PlaybackState.Queued); queue.TryWrite(item); }
    }

    /// <summary>The item's turn: false when it is skipped; otherwise it plays until <paramref name="report"/> brings what a page
    /// reported.</summary>
    public bool Play(Playback item, bool exists, TaskCompletionSource<(PlaybackState State, string? Reason)> report)
    {
        // The source's reason first: an entry from an ended room connection reports that, not its queue wait.
        var reason = item.SkipReason?.Invoke()
            ?? (timeProvider.GetElapsedTime(item.ReceivedAt) > TimeSpan.FromSeconds(30) ? "排队超过 30 秒"
            : service.Status.VideoConnections == 0 ? "没有推流软件连着"
            : !exists ? "视频文件不存在" : null);
        if (reason is not null)
        {
            Report(item, PlaybackState.Skipped, reason);
            return false;
        }
        current = item;
        reported = report;
        return true;
    }

    public void Played()
    {
        current = null;
        reported = null;
    }

    /// <summary>A page reported, or a test play was stopped: only the item playing now ends.</summary>
    public void End(string id, PlaybackState state, string? reason = null)
    {
        if (current?.Id == id) reported?.TrySetResult((state, reason));
    }

    public void Disconnected() => reported?.TrySetResult((PlaybackState.NoReport, "推流软件已全部断开"));

    /// <summary>The only file a page may fetch: the video playing now, by its playback id.</summary>
    public string? PlayingFile(string id) =>
        current is { } item && item.Id == id && item.Content is PlaybackContent.Video video ? video.FileName : null;

    public void Report(Playback item, PlaybackState state, string? reason = null)
    {
        var update = new VideoPlayback(item.Id, item.Content.Summary, item.Source, state, reason);
        service.Publish(update);
        item.Updated?.Invoke(update);
    }
}
