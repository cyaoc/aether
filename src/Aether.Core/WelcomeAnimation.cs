using Microsoft.Extensions.Logging;

namespace Aether.Core;

internal sealed class WelcomeAnimation(WebService service, TimeProvider timeProvider, ILogger logger)
{
    private readonly PlaybackSource source = new(service, timeProvider, logger, PlaybackSource.Welcome, true);
    public void TestPlay(long uid) => Enqueue(uid, null, timeProvider.GetTimestamp(), CancellationToken.None, true);
    public void StopTestPlay(string id) => source.StopTestPlay(id);

    internal void Enqueue(long uid, string? nickname, long receivedAt, CancellationToken roomStopped, bool isTest = false)
    {
        // The service's latest settings, not the room connection's: docs/reference.md applies turning it off on save.
        if (service.Settings is not { } settings || (!isTest && !settings.EntryAnimationEnabled.Value)
            || !settings.EntryAnimationViewers.TryGetValue(uid, out var choice) || choice.Value.Video is not { } video) return;
        source.Enqueue(uid, nickname, new PlaybackContent.Video(video), isTest ? "试播" : "动画名单", receivedAt, roomStopped, isTest);
    }
}
