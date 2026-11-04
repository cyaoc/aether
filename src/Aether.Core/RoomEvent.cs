namespace Aether.Core;

internal abstract record RoomEvent : DanmakuProtocol.DecodedEvent;

/// <summary>LiveTime: when the 一场直播 under way began, as the room connection first read it; null while the room is not live.</summary>
internal sealed record TransportConnected(int DanmakuLengthLimit, long? LiveTime) : RoomEvent;

internal sealed record TransportDisconnected : RoomEvent;

internal sealed record RoomConnectionEnded : RoomEvent;
