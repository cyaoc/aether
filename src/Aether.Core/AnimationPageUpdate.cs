namespace Aether.Core;

public abstract record AnimationPageUpdate;

public sealed record AnimationPageStatus(bool Running, int Port, int ConnectedPages, string? Error = null) : AnimationPageUpdate;

public enum WelcomeAnimationState { Queued, Playing, Completed, Failed, Skipped }

public sealed record WelcomeAnimationPlayback(string Id, long Uid, string FileName, bool IsTest,
    WelcomeAnimationState State, string? Reason = null) : AnimationPageUpdate;
