using Microsoft.Extensions.Logging;

namespace Aether.Core;

public sealed class WelcomeAnimation(WebService service, TimeProvider timeProvider, ILogger logger)
{
    private Settings? settings;
    private readonly PlaybackSource source = new(service, timeProvider, logger, "欢迎动画", true);
    public const string PreviousPending = PlaybackSource.PreviousPending;
    internal bool Enabled { get { lock (service.Gate) return settings?.EntryAnimationEnabled.Value == true; } }
    internal void Apply(Settings snapshot) { lock (service.Gate) settings = snapshot; }
    public void TestPlay(long uid) => Enqueue(uid, null, timeProvider.GetTimestamp(), CancellationToken.None, true);
    public void StopTestPlay(string id) => source.StopTestPlay(id);

    internal void Enqueue(long uid, string? nickname, long receivedAt, CancellationToken roomStopped, bool isTest = false)
    {
        lock (service.Gate)
        {
            if (settings is not { Error: null } || (!isTest && !settings.EntryAnimationEnabled.Value)
                || !settings.WelcomeAnimationVideos.TryGetValue(uid, out var video)) return;
            source.Enqueue(uid, nickname, new PlaybackContent.Video(video.Value), isTest ? "试播" : "动画名单", receivedAt, roomStopped, isTest);
        }
    }
}
