namespace Aether.Core;

public abstract record AnimationPageUpdate;

public sealed record AnimationPageStatus(bool Running, int Port, int ConnectedPages, string? Error = null) : AnimationPageUpdate
{
    /// <summary>What a browser source is given; ADR 0006 binds the page to localhost only.</summary>
    public string Address => $"http://localhost:{Port}/";
}

public enum WelcomeAnimationState { Queued, Playing, Completed, Failed, Skipped, NoReport }

/// <param name="Nickname">The viewer's nickname when they entered; a test play has none.</param>
public sealed record WelcomeAnimationPlayback(string Id, long Uid, string FileName, bool IsTest,
    WelcomeAnimationState State, string? Reason = null, string? Nickname = null) : AnimationPageUpdate
{
    /// <summary>The state as logs and shells name it.</summary>
    public string Progress => State switch
    {
        WelcomeAnimationState.Queued => "排队中", WelcomeAnimationState.Playing => "播放中",
        WelcomeAnimationState.Completed => "已播放", WelcomeAnimationState.Failed => "播放失败",
        WelcomeAnimationState.NoReport => "没收到播放回报", _ => "没播"
    };
}
