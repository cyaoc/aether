namespace Aether.Core;

internal abstract record RoomEvent : DanmakuProtocol.DecodedEvent;

internal sealed record TransportConnected(int DanmakuLengthLimit) : RoomEvent;

internal sealed record TransportDisconnected : RoomEvent;

internal sealed record RoomConnectionEnded : RoomEvent;
