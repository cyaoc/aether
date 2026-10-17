using Aether.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Aether.Gui;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? animationUpdateStop;
    private Task animationUpdates = Task.CompletedTask;
    private Task animationReload = Task.CompletedTask;
    private Settings? animationSettings;
    private int pendingAnimationReloads;
    internal bool AnimationReloading => pendingAnimationReloads > 0;
    /// <summary>Raised when anything an animation row shows may have changed: page status, playback, failures or a reload.</summary>
    internal event Action? AnimationRowsChanged;
    public ObservableCollection<WelcomeAnimationItem> RecentAnimations { get; } = [];
    private readonly Queue<string> finishedAnimations = new();
    private readonly HashSet<string> pendingAnimations = [];
    private readonly HashSet<(long Uid, string FileName)> failedAnimationVideos = [];
    private WelcomeAnimationPlayback? playingAnimation;
    private bool testPlayPending;
    internal bool AnimationBusy => testPlayPending || pendingAnimations.Count > 0;

    /// <summary>Starts the web service when the window opens, logging mapped videos that are missing as the CLI does.</summary>
    internal Task StartWebServiceAsync(string dataDirectory) => ApplyWebSettingsAsync(Settings.Load(dataDirectory, logger));

    internal Task RefreshWebServiceAsync(Settings snapshot)
    {
        if (animationSettings is { } previous
            && previous.Error?.Message == snapshot.Error?.Message
            && previous.WelcomeAnimationEnabled.Value == snapshot.WelcomeAnimationEnabled.Value
            && previous.WebServicePort.Value == snapshot.WebServicePort.Value
            && previous.WelcomeAnimationVideos.Count == snapshot.WelcomeAnimationVideos.Count
            && previous.WelcomeAnimationVideos.All(pair => snapshot.WelcomeAnimationVideos.TryGetValue(pair.Key, out var video) && video.Value == pair.Value.Value))
            return animationReload;
        return ApplyWebSettingsAsync(snapshot);
    }

    public Task ApplyWebSettingsAsync(Settings snapshot)
    {
        if (shuttingDown) return Task.CompletedTask;
        animationSettings = snapshot;
        pendingAnimationReloads++;
        AnimationRowsChanged?.Invoke();
        if (animationUpdateStop is null)
        {
            animationUpdateStop = new();
            animationUpdates = ObserveAnimationsAsync(animationUpdateStop.Token);
        }
        var previous = animationReload;
        return animationReload = ReloadAsync();

        async Task ReloadAsync()
        {
            try
            {
                await previous;
                if (shuttingDown) return;
                await client.ApplyWebSettingsAsync(snapshot);
                failedAnimationVideos.RemoveWhere(video => !snapshot.WelcomeAnimationVideos.TryGetValue(video.Uid, out var saved) || saved.Value != video.FileName);
                Apply(client.WebService.Status);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "网页服务设置未应用");
                // Invalid file contents did not change the running service; the settings page shows that error.
                Apply(snapshot.Error is not null ? client.WebService.Status : client.WebService.Status with { Error = error.Message });
            }
            finally
            {
                pendingAnimationReloads--;
                AnimationRowsChanged?.Invoke();
            }
        }
    }

    private async Task ObserveAnimationsAsync(CancellationToken token)
    {
        try
        {
            await foreach (var update in client.WebService.GetUpdatesAsync(token)) Apply(update);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task StopAnimationsAsync()
    {
        await animationReload;
        await client.WebService.StopAsync();
        Apply(client.WebService.Status);
        animationUpdateStop?.Cancel();
        await animationUpdates;
        animationUpdateStop?.Dispose();
        animationUpdateStop = null;
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        LoginCommand.Cancel();
        if (LoginCommand.ExecutionTask is { } login) await login;
        ClearQrCode();
        IsLoginPage = false;
    }

    [RelayCommand]
    private void OpenLoginPage() => ShowLoginPage();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VideoPageText))]
    [NotifyPropertyChangedFor(nameof(VideoAddressText))]
    [NotifyPropertyChangedFor(nameof(LotteryAddressText))]
    [NotifyPropertyChangedFor(nameof(HasVideoConnections))]
    [NotifyPropertyChangedFor(nameof(HasLotteryConnections))]
    [NotifyPropertyChangedFor(nameof(HasWebServiceError))]
    [NotifyPropertyChangedFor(nameof(IsWebServiceOff))]
    [NotifyPropertyChangedFor(nameof(ShowVideoSetup))]
    [NotifyPropertyChangedFor(nameof(ShowLotterySetup))]
    [NotifyPropertyChangedFor(nameof(ServiceStatusText))]
    [NotifyPropertyChangedFor(nameof(ServiceHelpText))]
    [NotifyPropertyChangedFor(nameof(VideoConnectionsText))]
    [NotifyPropertyChangedFor(nameof(LotteryConnectionsText))]
    private WebServiceStatus webServiceStatus = client.WebService.Status;

    public bool HasVideoConnections => WebServiceStatus is { Running: true, VideoConnections: > 0 };
    public bool HasLotteryConnections => WebServiceStatus is { Running: true, LotteryConnections: > 0 };
    public bool HasWebServiceError => WebServiceStatus.Error is not null;
    public bool IsWebServiceOff => WebServiceStatus is { Running: false, Error: null };
    public bool ShowVideoSetup => WebServiceStatus is { Running: true, VideoConnections: 0 };
    public bool ShowLotterySetup => WebServiceStatus is { Running: true, LotteryConnections: 0 };
    public string VideoAddressText => WebServiceStatus.VideoAddress;
    public string LotteryAddressText => WebServiceStatus.LotteryAddress;
    public string ServiceStatusText => WebServiceStatus.Error is { } error ? "没打开：" + error : WebServiceStatus.Running ? "运行中" : "未运行";
    public string ServiceHelpText => HasWebServiceError ? "换一个端口并保存，再把直播姬里所有浏览器素材的地址改成新的。"
        : WebServiceStatus.Running ? "GUI 开着就一直运行，不用登录、不用连直播间。先开 Aether，再开直播姬。" : "";
    public string VideoConnectionsText => ConnectionsText(WebServiceStatus.VideoConnections);
    public string LotteryConnectionsText => ConnectionsText(WebServiceStatus.LotteryConnections);
    private string ConnectionsText(int count) => !WebServiceStatus.Running ? "服务没打开" : count > 0 ? $"{count} 个推流软件连着" : "还没有连着";
    public string VideoPageText => WebServiceStatus.Error is { } error ? $"网页服务没打开：{error}，试播和进场都播不了"
        : !WebServiceStatus.Running ? "网页服务没打开，试播和进场都播不了"
        : HasVideoConnections ? $"视频页：已有 {WebServiceStatus.VideoConnections} 个推流软件连着"
        : "视频页还没有推流软件连着，先在直播姬里加好再试播";

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
        else if (update is WelcomeAnimationPlayback playback)
        {
            if (playback.IsTest) testPlayPending = false;
            var existing = RecentAnimations.FirstOrDefault(item => item.Playback.Id == playback.Id);
            var item = new WelcomeAnimationItem(playback, existing?.OccurredAt ?? clock.GetLocalNow());
            if (existing is null) RecentAnimations.Insert(0, item);
            else RecentAnimations[RecentAnimations.IndexOf(existing)] = item;
            if (playback.State is PlaybackState.Queued or PlaybackState.Playing)
                pendingAnimations.Add(playback.Id);
            else
            {
                pendingAnimations.Remove(playback.Id);
                finishedAnimations.Enqueue(playback.Id);
                if (finishedAnimations.Count > FinishedItemLimit)
                {
                    var oldest = finishedAnimations.Dequeue();
                    if (RecentAnimations.FirstOrDefault(value => value.Playback.Id == oldest) is { } old) RecentAnimations.Remove(old);
                }
            }
            NumberQueuedAnimations();
            if (playback.State == PlaybackState.Playing) playingAnimation = playback;
            else if (playingAnimation?.Id == playback.Id) playingAnimation = null;
            // #125: the failure hint stays until the video is replaced, even if a later playback works.
            if (playback.State == PlaybackState.Failed) failedAnimationVideos.Add((playback.Uid, playback.FileName));
        }
        else return; // A VideoPlayback alone changes no animation row.
        AnimationRowsChanged?.Invoke();
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
        AnimationRowsChanged?.Invoke();
    }
    internal bool AnimationPlaying(long uid, string fileName) => playingAnimation is { } playing && playing.Uid == uid && playing.FileName == fileName;

    internal bool AnimationVideoSaved(long uid, string fileName) => animationSettings is { } snapshot
        && snapshot.WelcomeAnimationVideos.TryGetValue(uid, out var video) && video.Value == fileName;

    internal void TestPlay(long uid, string fileName)
    {
        if (AnimationReloading || AnimationBusy || !HasVideoConnections || shuttingDown
            || animationSettings?.WelcomeAnimationEnabled.Value != true
            || !AnimationVideoSaved(uid, fileName)) return;
        // Disable immediately, before the queued update crosses back to the UI thread.
        testPlayPending = true;
        AnimationRowsChanged?.Invoke();
        client.WelcomeAnimation.TestPlay(uid);
    }

    private void ClearAnimationHistory()
    {
        RecentAnimations.Clear();
        finishedAnimations.Clear();
    }
}

/// <param name="Position">Place among the queued items, from 1; 0 once it is no longer queued.</param>
public sealed record WelcomeAnimationItem(WelcomeAnimationPlayback Playback, DateTimeOffset OccurredAt, int Position = 0)
{
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
    /// <summary>A viewer played within the interval: the queue working as intended, so it stays quiet.</summary>
    public bool RecentlyPlayed => Playback is { State: PlaybackState.Skipped, Reason: WelcomeAnimation.RecentlyPlayed };
    public bool Completed => Playback.State == PlaybackState.Completed;
    /// <summary>Not played for a reason the streamer may want to act on.</summary>
    public bool Unplayed => Playback.State == PlaybackState.NoReport || Playback.State == PlaybackState.Skipped && !RecentlyPlayed;
}
