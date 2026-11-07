using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>One playback source's pending viewers, test play and result reporting, in the web service region.</summary>
public sealed class PlaybackSource
{
    private readonly WebService service;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly string name;
    private readonly bool onePerViewer;
    private readonly HashSet<long> pendingViewers = [];
    private string? playingTest;
    public const string Entry = "进场动画", Gift = "送礼动画", Welcome = "欢迎动画";
    public const string PreviousPending = "上一个还没播完";

    internal PlaybackSource(WebService service, TimeProvider timeProvider, ILogger logger, string name, bool onePerViewer)
    {
        this.service = service;
        this.timeProvider = timeProvider;
        this.logger = logger;
        this.name = name;
        this.onePerViewer = onePerViewer;
    }

    internal void StopTestPlay(string id)
    {
        if (playingTest == id) service.Video.End(id, PlaybackState.Stopped);
    }

    internal void TestPlay(PlaybackContent.Animation content) => Enqueue(0, null, content, content.Nickname, timeProvider.GetTimestamp(), CancellationToken.None, true);

    internal void Enqueue(long uid, string? nickname, PlaybackContent content, string triggerReason, long receivedAt, CancellationToken roomStopped, bool isTest = false)
    {
        if (!isTest && onePerViewer && !pendingViewers.Add(uid))
        {
            Publish(new(Ids.New(), uid, content.Summary, false, PlaybackState.Skipped, PreviousPending, nickname, name, triggerReason));
            return;
        }
        service.Video.Enqueue(content, name, receivedAt,
            () => roomStopped.IsCancellationRequested ? "直播间连接结束" : null,
            playback =>
            {
                if (!isTest && playback.State is not (PlaybackState.Queued or PlaybackState.Playing)) pendingViewers.Remove(uid);
                if (isTest && playback.State == PlaybackState.Playing) playingTest = playback.Id;
                else if (playingTest == playback.Id) playingTest = null;
                Publish(new(playback.Id, uid, content.Summary, isTest, playback.State, playback.Reason, nickname, name, triggerReason));
            });
    }

    private void Publish(SourcePlayback update)
    {
        logger.Log(update.State is PlaybackState.Failed or PlaybackState.NoReport ? LogLevel.Warning : LogLevel.Information,
            "[{Source} {PlaybackId}] 观众 {Uid}，{Content}，{TriggerReason}，试播={IsTest}：{Progress} {Reason}",
            name, update.Id, update.Uid, update.ContentSummary, update.TriggerReason, update.IsTest, update.Progress, update.Reason);
        service.Publish(update);
    }
}
