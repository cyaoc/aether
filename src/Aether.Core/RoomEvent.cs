namespace Aether.Core;

internal abstract record RoomEvent : DanmakuProtocol.DecodedEvent;

/// <param name="AccountUid">The account the room is watched with, whose danmaku are the bot's own.</param>
internal sealed record TransportConnected(int DanmakuLengthLimit, long AccountUid) : RoomEvent;

internal sealed record TransportDisconnected : RoomEvent;

internal sealed record RoomConnectionEnded : RoomEvent;
