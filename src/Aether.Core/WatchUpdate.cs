namespace Aether.Core;

public abstract record WatchUpdate;

public sealed record WatchQrCode(string Content) : WatchUpdate;

public sealed record Connecting : WatchUpdate;

public sealed record Connected : WatchUpdate;

public sealed record Reconnecting : WatchUpdate;

public sealed record Danmaku(DateTimeOffset ReceivedAt, string Nickname, string Content) : WatchUpdate;
