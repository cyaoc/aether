namespace Aether.Core;

public abstract record WebServiceUpdate;

public sealed record WebServiceStatus(bool Running, int Port, int VideoConnections, int LotteryConnections, string? Error = null,
    int GiftBoardConnections = 0) : WebServiceUpdate
{
    /// <summary>What a browser source is given; ADR 0006 binds the page to localhost only.</summary>
    public string Origin => $"http://localhost:{Port}";
    public string VideoAddress => Origin + "/video";
    public string LotteryAddress => Origin + "/lottery";
    public string GiftBoardAddress => Origin + "/gift-board";
}

public enum PlaybackState { Queued, Playing, Completed, Failed, Skipped, NoReport, Stopped }

/// <param name="Nickname">The viewer's nickname when they entered; a test play has none.</param>
public sealed record SourcePlayback(string Id, long Uid, string ContentSummary, bool IsTest,
    PlaybackState State, string? Reason = null, string? Nickname = null, string Source = PlaybackSource.Welcome, string TriggerReason = "动画名单") : WebServiceUpdate
{
    /// <summary>The state as logs and shells name it.</summary>
    public string Progress => State switch
    {
        PlaybackState.Queued => "排队中", PlaybackState.Playing => "播放中",
        PlaybackState.Completed => "已播放", PlaybackState.Failed => "播放失败",
        PlaybackState.Stopped => "已停止",
        PlaybackState.NoReport => "没收到播放回报", _ => "没播"
    };
}

public sealed record VideoPlayback(string Id, string ContentSummary, string Source, PlaybackState State, string? Reason = null) : WebServiceUpdate;
