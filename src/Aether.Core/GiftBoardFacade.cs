using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The 礼物榜 region's facade (ADR 0017): the GUI runs the connected room's 期 through these commands and shows what
/// <see cref="GetUpdatesAsync"/> reports; a room connection connects the board and has it take gifts.</summary>
public sealed class GiftBoardFacade
{
    private readonly Region<GiftBoardController> region;
    private readonly Func<long, Task<string>> faceOf;
    private readonly ILogger logger;

    /// <param name="faceOf">Asks B站 for a viewer's avatar by uid.</param>
    internal GiftBoardFacade(GiftBoardController state, Func<long, Task<string>> faceOf, PendingSteps steps, ILogger logger)
    {
        region = new(state, steps, logger);
        this.faceOf = faceOf;
        this.logger = logger;
    }

    /// <summary>The current state first, then one per change.</summary>
    public IAsyncEnumerable<GiftBoardState> GetUpdatesAsync(CancellationToken token = default) =>
        region.SubscribeAsync(s => s.Subscribers, s => s.State, token);

    public Task StartNewPeriodAsync(long room) => region.Run(s => s.StartNewPeriod(room));

    public Task EndPeriodAsync(long room) => region.Run(s => s.EndPeriod(room));

    /// <summary>Ends the room's running 期 because the streamer is stopping; one not running is left as it is.</summary>
    public Task EndPeriodOnLeavingAsync(long room) => region.Run(s => s.EndPeriodOnLeaving(room));

    public Task ContinuePeriodAsync(long room) => region.Run(s => s.ContinuePeriod(room));

    public Task SetOptionsAsync(long room, GiftBoardOptions options) => region.Run(s => s.SetOptions(room, options));

    /// <summary>Reads the room's latest 期 without holding up the room connection.</summary>
    internal void Connect(long room) => region.Post(s => FillFaces(room, s.Connect(room)));

    internal void EndRoom(long room) => region.Post(s => s.EndRoom(room));

    /// <summary>Counts a message's gifts, and returns the 期 each belongs to for its 收礼记录 row.</summary>
    internal async Task<Func<Gift, long?>> TakeAsync(long room, IEnumerable<Gift> gifts)
    {
        var (periodOf, faceless) = await region.Run(s => s.Take(room, gifts));
        FillFaces(room, faceless);
        return periodOf;
    }

    internal void Stop() => region.Stop();

    // Asks B站 for each avatar outside the region, after the gifts are counted, so the board waits for neither, and puts it on
    // in a step of its own. shortcut: asked once per message or connection, not again on failure; retry if avatars still go
    // missing.
    private void FillFaces(long room, IEnumerable<long> uids)
    {
        foreach (var uid in uids)
            _ = Task.Run(async () =>
            {
                try
                {
                    var face = await faceOf(uid);
                    region.Post(s => s.SetFace(room, uid, face));
                }
                catch (Exception error)
                { logger.LogWarning(error, "礼物榜读取观众 {Uid} 的头像失败，先显示昵称首字：{Error}", uid, error.Message); }
            });
    }
}
