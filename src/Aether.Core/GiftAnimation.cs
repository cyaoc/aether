using Microsoft.Extensions.Logging;
namespace Aether.Core;

public sealed class GiftAnimation(WebService service, TimeProvider timeProvider, ILogger logger)
{
    private readonly PlaybackSource source = new(service, timeProvider, logger, PlaybackSource.Gift, false);
    public void TestPlay(EntryTier tier, string nickname) => source.TestPlay(PlaybackContent.General(tier, nickname));
    public void StopTestPlay(string id) => source.StopTestPlay(id);
    internal void Enqueue(GiftRoundEnded round, CancellationToken roomStopped)
    {
        lock (service.Gate)
        {
            if (service.Video.Settings is not { GiftAnimationEnabled.Value: true } settings) return;
            if (EntryTier.FromSpend(round.Spend, settings.GiftAnimationThresholds.Select(field => field.Value).ToArray()) is not { } tier) return;
            source.Enqueue(round.Uid, round.Nickname, PlaybackContent.General(tier, round.Nickname),
                $"这一轮投入 {round.Spend / 100m:0.##} 电池", round.EndedTimestamp, roomStopped);
        }
    }
}
