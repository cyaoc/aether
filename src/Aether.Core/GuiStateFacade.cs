namespace Aether.Core;

/// <summary>The GUI state region's facade (ADR 0017): the GUI changes what it keeps between runs through these commands and shows
/// what <see cref="GetUpdatesAsync"/> reports.</summary>
public sealed class GuiStateFacade
{
    private readonly Region<GuiStateController> region;

    internal GuiStateFacade(Regions regions, Func<GuiStateStore> store) =>
        region = regions.Create(_ => new GuiStateController(store, regions.Logger));

    /// <summary>The current state first, then one per change.</summary>
    public IAsyncEnumerable<GuiState> GetUpdatesAsync(CancellationToken token = default) =>
        region.SubscribeAsync(s => s.Subscribers, s => s.State, token);

    /// <summary>Puts a room just connected first, as typed.</summary>
    public Task RememberRoomAsync(string room) => region.Run(s => s.RememberRoom(room));

    public Task ForgetRoomAsync(string room) => region.Run(s => s.ForgetRoom(room));

    public Task SetKeepRecentDanmakuAsync(bool keep) => region.Run(s => s.SetKeepRecentDanmaku(keep));

    /// <summary>What gui.yml held (ADR 0016), over whatever was saved.</summary>
    public Task ReplaceAsync(GuiState state) => region.Run(s => s.Replace(state));

    internal void Stop() => region.Stop();
}
