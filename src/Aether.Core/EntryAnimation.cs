using Aether.Core.Entries;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

internal sealed class EntryAnimation(WebService service, WelcomeAnimation welcome, TimeProvider timeProvider, ILogger logger)
{
    private readonly PlaybackSource source = new(service, timeProvider, logger, PlaybackSource.Entry, true);
    public void TestPlay(EntryTier tier, string nickname) => source.TestPlay(PlaybackContent.General(tier, nickname));
    public void StopTestPlay(string id) => source.StopTestPlay(id);

    internal void Enqueue(Entry entry, long receivedAt, CancellationToken roomStopped)
    {
        if (service.Settings is not { EntryAnimationEnabled.Value: true } settings) return;
        if (settings.EntryAnimationViewers.TryGetValue(entry.Uid, out var listed))
        {
            if (listed.Value.Video is not null) welcome.Enqueue(entry.Uid, entry.Nickname, receivedAt, roomStopped);
            else if (listed.Value.Tier is { } tier) Play(tier, "指定观众");
        }
        else if (settings.EntryAnimationFanClubLevel.Value > 0 && entry.FanClubLevel >= settings.EntryAnimationFanClubLevel.Value
            && EntryTier.FromLevel(entry.FanClubLevel.Value) is { } tier)
            Play(tier, $"粉丝团 {entry.FanClubLevel} 级");

        void Play(EntryTier tier, string reason) =>
            source.Enqueue(entry.Uid, entry.Nickname, PlaybackContent.General(tier, entry.Nickname), reason, receivedAt, roomStopped);
    }
}
