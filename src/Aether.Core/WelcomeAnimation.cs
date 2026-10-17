using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>Selects mapped viewers and owns the ten-minute interval; the video page handles delivery.</summary>
public sealed class WelcomeAnimation(WebService service, TimeProvider timeProvider, ILogger logger)
{
    private Settings? settings;
    public const string RecentlyPlayed = "10 分钟内播过";

    internal void Apply(Settings snapshot) { lock (service.Gate) settings = snapshot; }

    public void TestPlay(long uid) => Enqueue(uid, null, timeProvider.GetTimestamp(), null, CancellationToken.None);

    internal void Enqueue(long uid, string? nickname, long receivedAt, Dictionary<long, long>? lastPlayed, CancellationToken roomStopped)
    {
        lock (service.Gate)
        {
            if (settings is not { Error: null, WelcomeAnimationEnabled.Value: true }
                || !settings.WelcomeAnimationVideos.TryGetValue(uid, out var video)) return;
            service.Video.Enqueue(video.Value, "欢迎动画", receivedAt,
                () => roomStopped.IsCancellationRequested ? "直播间连接结束"
                    : lastPlayed?.TryGetValue(uid, out var last) == true && timeProvider.GetElapsedTime(last) < TimeSpan.FromMinutes(10)
                        ? RecentlyPlayed : null,
                playback =>
                {
                    if (playback.State == PlaybackState.Playing && lastPlayed is not null) lastPlayed[uid] = timeProvider.GetTimestamp();
                    var update = new WelcomeAnimationPlayback(playback.Id, uid, playback.FileName, lastPlayed is null,
                        playback.State, playback.Reason, nickname);
                    logger.Log(update.State is PlaybackState.Failed or PlaybackState.NoReport ? LogLevel.Warning : LogLevel.Information,
                        "[欢迎动画 {PlaybackId}] 观众 {Uid}，{FileName}，试播={IsTest}：{Progress} {Reason}",
                        update.Id, uid, update.FileName, update.IsTest, update.Progress, update.Reason);
                    service.Publish(update);
                });
        }
    }
}
