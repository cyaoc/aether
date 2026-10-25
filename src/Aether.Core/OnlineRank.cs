namespace Aether.Core;

public sealed record OnlineRank(int Count, IReadOnlyList<OnlineRankViewer> Viewers);

public sealed record OnlineRankViewer(long Uid, string Nickname);
