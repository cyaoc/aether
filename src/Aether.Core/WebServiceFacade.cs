using System.Threading.Channels;
using Aether.Core.Entries;

namespace Aether.Core;

/// <summary>The web service region's facade (ADR 0017): shells start, stop, apply settings and test-play through its commands, and
/// read its snapshots or <see cref="GetUpdatesAsync"/>. <see cref="WebServer"/> runs the flows outside the region; the shell owns
/// the service lifetime.</summary>
public sealed class WebServiceFacade
{
    private readonly Region<WebService> region;
    private readonly WebServer server;

    internal WebServiceFacade(Regions regions, string dataDirectory, LotteryFacade lottery, GiftBoardFacade giftBoard)
    {
        // shortcut: pending videos are unbounded; add a capacity if playback sources can flood the queue.
        var queue = Channel.CreateUnbounded<VideoQueue.Playback>(new UnboundedChannelOptions { SingleReader = true });
        region = regions.Create(_ => new WebService(queue.Writer, regions.Time, regions.Logger, lottery.SetExamplePage));
        Lottery = new(lottery);
        server = new(region, regions.Time, web => [new VideoPage(web, queue.Reader, dataDirectory), Lottery, new GiftBoardPage(giftBoard),
            new DesktopPetPage(web, lottery, giftBoard)]);
    }

    public LotteryPage Lottery { get; }

    public WebServiceStatus Status => server.Status;

    /// <inheritdoc cref="WebService.Settings"/>
    public Settings? Settings => server.Settings;

    /// <summary>Subscribe independently of WatchAsync. The first update is the current service status.</summary>
    public IAsyncEnumerable<WebServiceUpdate> GetUpdatesAsync(CancellationToken cancellationToken = default) =>
        region.SubscribeAsync(s => s.Subscribers, s => s.Status, cancellationToken);

    /// <inheritdoc cref="WebServer.ApplySettingsAsync"/>
    public Task ApplySettingsAsync(Settings settings) => server.ApplySettingsAsync(settings);

    /// <inheritdoc cref="WebServer.StartAsync"/>
    public Task StartAsync(int port) => server.StartAsync(port);

    /// <inheritdoc cref="WebServer.StopAsync"/>
    public Task StopAsync() => server.StopAsync();

    /// <summary>Plays a file in the videos folder on the video page, after what is already queued.</summary>
    public Task PlayVideoAsync(string fileName, string source) => region.Run(s => s.Video.Enqueue(fileName, source));

    public Task TestPlayWelcomeAsync(long uid) => region.Run(s => s.WelcomeAnimation.TestPlay(uid));

    public Task TestPlayEntryAsync(EntryTier tier, string nickname) => region.Run(s => s.EntryAnimation.TestPlay(tier, nickname));

    public Task TestPlayGiftAsync(EntryTier tier, string nickname) => region.Run(s => s.GiftAnimation.TestPlay(tier, nickname));

    /// <summary>Stops the test play with this id while it plays; a late click stops nothing else.</summary>
    public Task StopTestPlayAsync(string id) => region.Run(s => s.StopTestPlay(id));

    /// <summary>Posted from the room connection, which never waits for the web service (ADR 0017).</summary>
    internal void PlayEntry(Entry entry, long receivedAt, CancellationToken roomStopped) =>
        region.Post(s => s.EntryAnimation.Enqueue(entry, receivedAt, roomStopped));

    /// <inheritdoc cref="PlayEntry"/>
    internal void PlayGiftRound(GiftRoundEnded round, CancellationToken roomStopped) => region.Post(s => s.GiftAnimation.Enqueue(round, roomStopped));

    /// <inheritdoc cref="PlayEntry"/>
    internal void SetDesktopPetRoom(bool connected) => region.Post(s => s.DesktopPet.SetRoom(connected));

    /// <inheritdoc cref="PlayEntry"/>
    internal void TellDesktopPet(string name, object data) => region.Post(s => s.DesktopPet.Tell(name, data));

    /// <summary>Closes the listener and every connection in a last step, without waiting for the serving to wind down as
    /// <see cref="StopAsync"/> does, then stops the region.</summary>
    internal void Stop()
    {
        // A block, as s.Stop() returns the serving's task and Post refuses steps that return one.
        region.Post(s => { s.Stop(); });
        region.Stop();
    }
}
