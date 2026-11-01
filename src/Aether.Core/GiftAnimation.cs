using Microsoft.Extensions.Logging;
namespace Aether.Core;

public sealed class GiftAnimation(WebService service, TimeProvider timeProvider, ILogger logger)
{
    private Settings? settings;
    private readonly PlaybackSource source = new(service, timeProvider, logger, "送礼动画", false);
    internal void Apply(Settings snapshot) { lock (service.Gate) settings = snapshot; }
    public void TestPlay(EntryTier tier, string nickname) => source.TestPlay(new PlaybackContent.Animation(AnimationId.General(tier), nickname));
    public void StopTestPlay(string id) => source.StopTestPlay(id);
    internal void Enqueue(GiftRoundEnded round, CancellationToken roomStopped)
    {
        lock (service.Gate)
        {
            if (settings is not { Error: null, GiftAnimationEnabled.Value: true }) return;
            if (EntryTier.FromSpend(round.Spend, settings.GiftAnimationThresholds.Select(field => field.Value).ToArray()) is not { } tier) return;
            source.Enqueue(round.Uid, round.Nickname, new PlaybackContent.Animation(AnimationId.General(tier), round.Nickname),
                $"一轮投入 {round.Spend / 100m:0.##} 电池", round.EndedTimestamp, roomStopped);
        }
    }
}
