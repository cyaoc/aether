using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>Selects mapped viewers and keeps one pending entry per viewer; the video page handles delivery.</summary>
public sealed class WelcomeAnimation(WebService service, TimeProvider timeProvider, ILogger logger)
{
    private Settings? settings;
    private readonly HashSet<long> pendingViewers = [];
    private string? playingTest;
    public const string StillPlaying = "上一个还没播完";

    internal void Apply(Settings snapshot) { lock (service.Gate) settings = snapshot; }

    public void TestPlay(long uid) => Enqueue(uid, null, timeProvider.GetTimestamp(), CancellationToken.None, isTest: true);

    public void StopTestPlay(string id)
    {
        lock (service.Gate) if (playingTest == id) service.Video.Stop(id);
    }

    internal void Enqueue(long uid, string? nickname, long receivedAt, CancellationToken roomStopped, bool isTest = false)
    {
        lock (service.Gate)
        {
            if (settings is not { Error: null, WelcomeAnimationEnabled.Value: true }
                || !settings.WelcomeAnimationVideos.TryGetValue(uid, out var video)) return;
            if (!isTest && !pendingViewers.Add(uid))
            {
                Publish(new(Guid.NewGuid().ToString("N"), uid, video.Value, false, PlaybackState.Skipped, StillPlaying, nickname));
                return;
            }
            service.Video.Enqueue(video.Value, "欢迎动画", receivedAt,
                () => roomStopped.IsCancellationRequested ? "直播间连接结束" : null,
                playback =>
                {
                    lock (service.Gate)
                    {
                        if (!isTest && playback.State is not (PlaybackState.Queued or PlaybackState.Playing)) pendingViewers.Remove(uid);
                        if (isTest && playback.State == PlaybackState.Playing) playingTest = playback.Id;
                        else if (playingTest == playback.Id) playingTest = null;
                    }
                    Publish(new(playback.Id, uid, playback.FileName, isTest, playback.State, playback.Reason, nickname));
                });
        }
    }

    private void Publish(WelcomeAnimationPlayback update)
    {
        logger.Log(update.State is PlaybackState.Failed or PlaybackState.NoReport ? LogLevel.Warning : LogLevel.Information,
            "[欢迎动画 {PlaybackId}] 观众 {Uid}，{FileName}，试播={IsTest}：{Progress} {Reason}",
            update.Id, update.Uid, update.FileName, update.IsTest, update.Progress, update.Reason);
        service.Publish(update);
    }
}
