namespace Aether.Core;

internal sealed record Gift(long Uid, string Nickname, long Spend, BlindBox? BlindBox);

/// <summary>A viewer's paid gifts after three seconds of silence; amounts are in gold coins.</summary>
public sealed record GiftRoundEnded(long RoomId, long Uid, string Nickname, long Spend,
    DateTimeOffset EndedAt, long EndedTimestamp) : WatchUpdate;
