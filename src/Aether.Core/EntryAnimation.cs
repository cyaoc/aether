using Aether.Core.Entries;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

public sealed class EntryAnimation(WebService service, WelcomeAnimation welcome, TimeProvider timeProvider, ILogger logger)
{
    private Settings? settings;
    private readonly PlaybackSource source = new(service, timeProvider, logger, "进场动画", true);
    internal void Apply(Settings snapshot) { lock (service.Gate) settings = snapshot; }
    public void TestPlay(EntryTier tier, string nickname) => source.TestPlay(new PlaybackContent.Animation(AnimationId.General(tier), nickname));
    public void StopTestPlay(string id) => source.StopTestPlay(id);

    internal void Enqueue(Entry entry, long receivedAt, CancellationToken roomStopped)
    {
        lock (service.Gate)
        {
            if (settings is not { Error: null, EntryAnimationEnabled.Value: true }) return;
            EntryTier? tier;
            string reason;
            if (settings.EntryAnimationViewers.TryGetValue(entry.Uid, out var choice))
            {
                if (choice.Value.Video is not null) { welcome.Enqueue(entry.Uid, entry.Nickname, receivedAt, roomStopped); return; }
                tier = choice.Value.Tier;
                reason = "指定观众";
            }
            else
            {
                if (settings.EntryAnimationFanClubLevel.Value == 0 || entry.FanClubLevel is not { } level || level < settings.EntryAnimationFanClubLevel.Value) return;
                tier = EntryTier.FromLevel((int)Math.Min(level, int.MaxValue));
                reason = $"粉丝团 {entry.FanClubLevel} 级";
            }
            if (tier is { } selected)
                source.Enqueue(entry.Uid, entry.Nickname, new PlaybackContent.Animation(Character(entry, selected), entry.Nickname), reason, receivedAt, roomStopped);
        }
    }

    private static string Character(Entry entry, EntryTier tier) => AnimationId.General(tier);
}
