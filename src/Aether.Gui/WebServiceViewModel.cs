using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Aether.Gui;

public partial class WebServiceViewModel(AetherClient client, ILogger logger, TimeProvider clock,
    CancellationToken shutdown) : ObservableObject
{
    private readonly LatestRun animationUpdates = new();
    private readonly LatestRun animationReload = new();
    private Settings? animationSettings;
    internal bool AnimationReloading => !animationReload.Current.IsCompleted;
    /// <summary>Raised when anything an animation row shows may have changed: page status, playback, failures or a reload.</summary>
    internal event Action? AnimationRowsChanged;
    public RecentItems<PlaybackItem> RecentAnimations { get; } = [];
    public IReadOnlyList<PlaybackItem> RecentEntryAnimations => RecentAnimations.Where(item => item.Playback.Source != PlaybackSource.Gift).ToArray();
    public IReadOnlyList<PlaybackItem> RecentGiftAnimations => RecentAnimations.Where(item => item.Playback.Source == PlaybackSource.Gift).ToArray();
    internal SourcePlayback? PlayingForSource(string source) => playingAnimation?.Source == source ? playingAnimation : null;
    private readonly HashSet<string> pendingAnimations = [];
    private readonly HashSet<(long Uid, string FileName)> failedAnimationVideos = [];
    private SourcePlayback? playingAnimation;
    private bool testPlayPending;
    internal bool AnimationBusy => testPlayPending || pendingAnimations.Count > 0;

    internal Task RefreshWebServiceAsync(Settings snapshot)
    {
        if (animationSettings is { } previous
            && previous.Error?.Message == snapshot.Error?.Message
            && previous.EntryAnimationEnabled.Value == snapshot.EntryAnimationEnabled.Value
            && previous.WebServicePort.Value == snapshot.WebServicePort.Value
            && previous.EntryAnimationFanClubLevel.Value == snapshot.EntryAnimationFanClubLevel.Value
            && previous.EntryAnimationVolume.Value == snapshot.EntryAnimationVolume.Value
            && previous.GiftAnimationEnabled.Value == snapshot.GiftAnimationEnabled.Value
            && previous.GiftAnimationThresholds.Select(value => value.Value).SequenceEqual(snapshot.GiftAnimationThresholds.Select(value => value.Value))
            && previous.EntryAnimationViewers.Count == snapshot.EntryAnimationViewers.Count
            && previous.EntryAnimationViewers.All(pair => snapshot.EntryAnimationViewers.TryGetValue(pair.Key, out var choice) && choice.Value == pair.Value.Value))
            return animationReload.Current;
        return ApplyWebSettingsAsync(snapshot);
    }

    public async Task ApplyWebSettingsAsync(Settings snapshot)
    {
        if (shutdown.IsCancellationRequested) return;
        animationSettings = snapshot;
        if (animationUpdates.Current.IsCompleted) _ = animationUpdates.Follow(client.WebService.GetUpdatesAsync, Apply,
            error => logger.LogError(error, "网页服务状态订阅中断"));
        var reload = animationReload.Start(ReloadAsync);
        NotifyAnimationRows();
        await reload;
        NotifyAnimationRows();

        async Task ReloadAsync(CancellationToken _)
        {
            try
            {
                if (shutdown.IsCancellationRequested) return;
                await client.WebService.ApplySettingsAsync(snapshot);
                failedAnimationVideos.RemoveWhere(video => !snapshot.WelcomeAnimationVideos.TryGetValue(video.Uid, out var saved) || saved.Value != video.FileName);
                Apply(client.WebService.Status);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "网页服务设置未应用");
                // Invalid file contents did not change the running service; the settings page shows that error.
                Apply(snapshot.Error is not null ? client.WebService.Status : client.WebService.Status with { Error = error.Message });
            }
        }
    }

    internal async Task StopAsync()
    {
        await animationReload.StopAsync();
        await client.WebService.StopAsync();
        Apply(client.WebService.Status);
        await animationUpdates.StopAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Video), nameof(Lottery), nameof(GiftBoard), nameof(HasVideoConnections))]
    [NotifyPropertyChangedFor(nameof(HasWebServiceError), nameof(IsWebServiceOff), nameof(ServiceStatusText), nameof(ServiceHelpText))]
    private WebServiceStatus webServiceStatus = client.WebService.Status;

    public WebPageStatus Video => new(WebServiceStatus, WebServiceStatus.VideoConnections, WebPageTexts.Video) { Address = WebServiceStatus.VideoAddress };
    public WebPageStatus Lottery => new(WebServiceStatus, WebServiceStatus.LotteryConnections, WebPageTexts.Lottery) { Address = WebServiceStatus.LotteryAddress };
    public WebPageStatus GiftBoard => new(WebServiceStatus, WebServiceStatus.GiftBoardConnections, WebPageTexts.GiftBoard) { Address = WebServiceStatus.GiftBoardAddress };
    public bool HasVideoConnections => Video.Connected;
    public bool HasWebServiceError => WebServiceStatus.Error is not null;
    public bool IsWebServiceOff => WebServiceStatus is { Running: false, Error: null };
    public string ServiceStatusText => WebServiceStatus.Error is { } error ? "没打开：" + error : WebServiceStatus.Running ? "运行中" : "未运行";
    public string ServiceHelpText => HasWebServiceError ? "换一个端口并保存，再把直播姬里所有浏览器素材的地址改成新的。"
        : WebServiceStatus.Running ? "GUI 开着就一直运行，不用登录、不用连直播间。先开 Aether，再开直播姬。" : "";

    internal void Apply(WebServiceUpdate update)
    {
        if (update is WebServiceStatus status)
        {
            WebServiceStatus = status;
            if (!status.Running)
            {
                pendingAnimations.Clear();
                playingAnimation = null;
                testPlayPending = false;
            }
        }
        else if (update is SourcePlayback playback)
        {
            if (playback.IsTest) testPlayPending = false;
            var existing = RecentAnimations.FirstOrDefault(item => item.Playback.Id == playback.Id);
            var item = new PlaybackItem(playback, existing?.OccurredAt ?? clock.GetLocalNow());
            if (existing is null) RecentAnimations.Insert(0, item);
            else RecentAnimations[RecentAnimations.IndexOf(existing)] = item;
            if (playback.State is PlaybackState.Queued or PlaybackState.Playing)
                pendingAnimations.Add(playback.Id);
            else
            {
                pendingAnimations.Remove(playback.Id);
                RecentAnimations.Finish(item);
            }
            NumberQueuedAnimations();
            if (playback.State == PlaybackState.Playing) playingAnimation = playback;
            else if (playingAnimation?.Id == playback.Id) playingAnimation = null;
            // #125: the failure hint stays until the video is replaced, even if a later playback works.
            if (playback.State == PlaybackState.Failed) failedAnimationVideos.Add((playback.Uid, playback.ContentSummary));
        }
        else return; // A VideoPlayback alone changes no animation row.
        NotifyAnimationRows();
    }

    /// <summary>Core plays in arrival order and the list is newest first, so queued items count up from the bottom.</summary>
    private void NumberQueuedAnimations()
    {
        var position = 0;
        for (var index = RecentAnimations.Count - 1; index >= 0; index--)
        {
            if (RecentAnimations[index] is not { Playback.State: PlaybackState.Queued } queued) continue;
            position++;
            if (queued.Position != position) RecentAnimations[index] = queued with { Position = position };
        }
    }

    internal bool AnimationFailed(long uid, string fileName) => failedAnimationVideos.Contains((uid, fileName));
    internal void ForgetAnimationFailures(IReadOnlyCollection<long> replaced)
    {
        failedAnimationVideos.RemoveWhere(video => replaced.Contains(video.Uid));
        NotifyAnimationRows();
    }
    internal SourcePlayback? PlayingAnimation(long uid, string fileName) =>
        playingAnimation is { } playing && playing.Uid == uid && playing.ContentSummary == fileName ? playing : null;

    internal bool AnimationVideoSaved(long uid, string fileName) => animationSettings is { } snapshot
        && snapshot.WelcomeAnimationVideos.TryGetValue(uid, out var video) && video.Value == fileName;

    internal void TestOrStopAnimation(long uid, string fileName)
    {
        if (PlayingAnimation(uid, fileName) is { IsTest: true } playing)
        {
            client.WebService.StopTestPlayAsync(playing.Id).RaiseFailure();
            return;
        }
        if (AnimationReloading || AnimationBusy || !HasVideoConnections || shutdown.IsCancellationRequested
            || !AnimationVideoSaved(uid, fileName)) return;
        PendTestPlay();
        client.WebService.TestPlayWelcomeAsync(uid).RaiseFailure();
    }

    internal void TestAnimation(string source, EntryTier tier, string nickname)
    {
        if (AnimationReloading || AnimationBusy || !HasVideoConnections || shutdown.IsCancellationRequested) return;
        PendTestPlay();
        Func<EntryTier, string, Task> play = source == PlaybackSource.Gift ? client.WebService.TestPlayGiftAsync : client.WebService.TestPlayEntryAsync;
        play(tier, nickname).RaiseFailure();
    }

    internal void StopAnimation(string id) => client.WebService.StopTestPlayAsync(id).RaiseFailure();

    // Disable immediately, before the queued update crosses back to the UI thread.
    private void PendTestPlay()
    {
        testPlayPending = true;
        NotifyAnimationRows();
    }

    private void NotifyAnimationRows()
    {
        OnPropertyChanged(nameof(RecentEntryAnimations));
        OnPropertyChanged(nameof(RecentGiftAnimations));
        AnimationRowsChanged?.Invoke();
    }

    internal void ClearAnimationHistory()
    {
        RecentAnimations.Clear();
    }
}

/// <param name="Position">Place among the queued items, from 1; 0 once it is no longer queued.</param>
public sealed record PlaybackItem(SourcePlayback Playback, DateTimeOffset OccurredAt, int Position = 0)
{
    public string ContentDetail => Playback.Source == PlaybackSource.Welcome ? $"欢迎动画 · {Playback.ContentSummary}"
        : $"{Playback.ContentSummary} · {Playback.TriggerReason}";
    public string Name => Playback.IsTest ? "试播" : Playback.Nickname ?? $"UID {Playback.Uid}";
    /// <summary>Null when Name already says it all, so the header gets no separating space.</summary>
    public string? Detail => Playback.IsTest || Playback.Nickname is null ? null : $"UID {Playback.Uid}";
    public string Status => Playback switch
    {
        { State: PlaybackState.Queued } when Position > 0 => $"{Playback.Progress} · 第 {Position} 个",
        { State: PlaybackState.Failed or PlaybackState.Skipped or PlaybackState.NoReport, Reason: { } reason }
            => $"{Playback.Progress}：{reason}",
        _ => Playback.Progress
    };
    public bool Playing => Playback.State == PlaybackState.Playing;
    public bool Failed => Playback.State == PlaybackState.Failed;
    /// <summary>A viewer already has a pending animation, so the skipped reentry stays quiet.</summary>
    public bool PreviousPending => Playback is { State: PlaybackState.Skipped, Reason: PlaybackSource.PreviousPending };
    public bool Completed => Playback.State == PlaybackState.Completed;
    public bool Stopped => Playback.State == PlaybackState.Stopped;
    /// <summary>Not played for a reason the streamer may want to act on.</summary>
    public bool Unplayed => Playback.State == PlaybackState.NoReport || Playback.State == PlaybackState.Skipped && !PreviousPending;
}
