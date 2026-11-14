namespace Aether.Core;

public abstract record WebServiceUpdate;

public sealed record WebServiceStatus(bool Running, int Port, int VideoConnections, int LotteryConnections, int GiftBoardConnections,
    string? Error = null) : WebServiceUpdate
{
    public int DesktopPetConnections { get; init; }

    /// <summary>What a browser source is given; ADR 0006 binds the page to localhost only.</summary>
    public string Origin => $"http://localhost:{Port}";
    public string VideoAddress => Origin + "/video";
    public string LotteryAddress => Origin + "/lottery";
    public string GiftBoardAddress => Origin + "/gift-board";
    public string DesktopPetAddress => Origin + "/desktop-pet";
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

/// <summary>The 桌宠页 played a 试演 out, cut it short with a show of what happened or went away; or none was played, and
/// <paramref name="Refused"/> says why.</summary>
public sealed record DesktopPetTestEnded(string? Refused = null) : WebServiceUpdate;

/// <summary>Where the 捣蛋鬼 is, as the 桌宠页 reports it: not come, on its way once her show is over, or on the page.</summary>
public enum PranksterVisit { Away, OnTheWay, OnStage }

/// <summary>Whether the streamer can 叫它来: not while <paramref name="Blocked"/> says why, nor while the 捣蛋鬼 is coming or on
/// the page.</summary>
public sealed record PranksterSummon(string? Blocked, PranksterVisit Visit)
{
    public bool CanSummon => Blocked is null && Visit == PranksterVisit.Away;
}

public sealed record VideoPlayback(string Id, string ContentSummary, string Source, PlaybackState State, string? Reason = null) : WebServiceUpdate;
