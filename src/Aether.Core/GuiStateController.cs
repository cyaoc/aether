using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>The GUI state region's state (ADR 0017): the one copy of <see cref="GuiState"/>, read from the database by the first
/// step that needs it.</summary>
internal sealed class GuiStateController(Func<GuiStateStore> store, ILogger logger)
{
    private GuiState? state;

    public Subscribers<GuiState> Subscribers { get; } = new();

    public GuiState State => state ??= Load();

    /// <summary>A reconnect remembers its room again: the latest room is left as it is, without a write.</summary>
    public void RememberRoom(string room)
    {
        if (State.RecentRooms.FirstOrDefault() != room) Replace(State with { RecentRooms = [room, .. State.RecentRooms] });
    }

    public void ForgetRoom(string room) => Replace(State with { RecentRooms = [.. State.RecentRooms.Where(value => value != room)] });

    public void SetKeepRecentDanmaku(bool keep) => Replace(State with { KeepRecentDanmaku = keep });

    /// <summary>Written as a whole, and reported even when saving it fails, as the window did when it kept its own copy; the
    /// failure goes to the command.</summary>
    public void Replace(GuiState next)
    {
        state = next;
        Subscribers.Publish(next);
        store().Save(next);
    }

    private GuiState Load()
    {
        try { return store().Load(); }
        catch (Exception error)
        {
            logger.LogWarning(error, "读取 GUI 状态失败，使用默认值");
            return GuiState.Default;
        }
    }
}
