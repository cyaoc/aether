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

    internal Task RefreshAnimationPageAsync(Settings snapshot)
    {
        if (animationSettings is { } previous
            && previous.Error?.Message == snapshot.Error?.Message
            && previous.WelcomeAnimationEnabled.Value == snapshot.WelcomeAnimationEnabled.Value
            && previous.WelcomeAnimationPort.Value == snapshot.WelcomeAnimationPort.Value
            && previous.WelcomeAnimationVideos.Count == snapshot.WelcomeAnimationVideos.Count
            && previous.WelcomeAnimationVideos.All(pair => snapshot.WelcomeAnimationVideos.TryGetValue(pair.Key, out var video) && video.Value == pair.Value.Value))
            return animationReload;
        return ReloadAnimationPageAsync(snapshot);
    }

    public Task ReloadAnimationPageAsync(Settings snapshot)
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
                await client.AnimationPage.StopAsync();
                if (shuttingDown) return;
                client.AnimationPage.Start(snapshot);
                failedAnimationVideos.RemoveWhere(video => !snapshot.WelcomeAnimationVideos.TryGetValue(video.Uid, out var saved) || saved.Value != video.FileName);
                Apply(client.AnimationPage.Status);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "动画页启动失败");
                Apply(new AnimationPageStatus(false, snapshot.WelcomeAnimationPort.Value, 0, error.Message));
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
            await foreach (var update in client.AnimationPage.GetUpdatesAsync(token)) Apply(update);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task StopAnimationsAsync()
    {
        await animationReload;
        await client.AnimationPage.StopAsync();
        Apply(client.AnimationPage.Status);
        animationUpdateStop?.Cancel();
        await animationUpdates;
        animationUpdateStop?.Dispose();
        animationUpdateStop = null;
    }

    [RelayCommand]
    private async Task OpenAnimationSettingsAsync()
    {
        LoginCommand.Cancel();
        if (LoginCommand.ExecutionTask is { } login) await login;
        ClearQrCode();
        IsLoginPage = false;
    }

    [RelayCommand]
    private void OpenLoginPage() => ShowLoginPage();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnimationPageText))]
    [NotifyPropertyChangedFor(nameof(AnimationAddressText))]
    [NotifyPropertyChangedFor(nameof(HasAnimationPages))]
    [NotifyPropertyChangedFor(nameof(HasAnimationError))]
    [NotifyPropertyChangedFor(nameof(ShowAnimationSetup))]
    [NotifyPropertyChangedFor(nameof(IsAnimationPageOff))]
    [NotifyPropertyChangedFor(nameof(AnimationPageHelp))]
    private AnimationPageStatus animationPageStatus = new(false, 17890, 0);

    public bool HasAnimationPages => AnimationPageStatus.Running && AnimationPageStatus.ConnectedPages > 0;
    public bool HasAnimationError => AnimationPageStatus.Error is not null;
    public bool ShowAnimationSetup => AnimationPageStatus is { Running: true, ConnectedPages: 0 };
    public bool IsAnimationPageOff => AnimationPageStatus is { Running: false, Error: null };
    public string AnimationAddressText => $"http://localhost:{AnimationPageStatus.Port}/";
    public string AnimationPageText => AnimationPageStatus.Error is { } error ? "动画页没打开：" + error
        : !AnimationPageStatus.Running ? "动画页没开，打开开关并保存后启动"
        : HasAnimationPages ? $"已有 {AnimationPageStatus.ConnectedPages} 个推流软件连着动画页"
        : "还没有推流软件连着动画页";
    public string AnimationPageHelp => HasAnimationError ? "换一个端口并保存，再把直播姬里浏览器素材的地址改成新的。"
        : ShowAnimationSetup ? "在直播姬里添加浏览器素材，地址填下面这个，尺寸 1920 × 1080。已经加过的，右键素材 → 刷新。" : "";

    internal void Apply(AnimationPageUpdate update)
    {
        if (update is AnimationPageStatus status)
        {
            AnimationPageStatus = status;
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
            if (playback.State is WelcomeAnimationState.Queued or WelcomeAnimationState.Playing)
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
            if (playback.State == WelcomeAnimationState.Playing) playingAnimation = playback;
            else if (playingAnimation?.Id == playback.Id) playingAnimation = null;
            // #125: the failure hint stays until the video is replaced, even if a later playback works.
            if (playback.State == WelcomeAnimationState.Failed) failedAnimationVideos.Add((playback.Uid, playback.FileName));
        }
        AnimationRowsChanged?.Invoke();
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
        if (AnimationReloading || AnimationBusy || !HasAnimationPages || shuttingDown
            || !AnimationVideoSaved(uid, fileName)) return;
        // Disable immediately, before the queued update crosses back to the UI thread.
        testPlayPending = true;
        AnimationRowsChanged?.Invoke();
        client.AnimationPage.TestPlay(uid);
    }

    private void ClearAnimationHistory()
    {
        RecentAnimations.Clear();
        finishedAnimations.Clear();
    }
}

public sealed record WelcomeAnimationItem(WelcomeAnimationPlayback Playback, DateTimeOffset OccurredAt)
{
    public string Name => Playback.IsTest ? "试播" : Playback.Nickname ?? $"UID {Playback.Uid}";
    public string Detail => Playback.IsTest || Playback.Nickname is null ? "" : $"UID {Playback.Uid}";
    public string Status => Playback is { State: WelcomeAnimationState.Failed or WelcomeAnimationState.Skipped, Reason: { } reason }
        ? $"{Playback.Progress}：{reason}" : Playback.Progress;
    public bool Playing => Playback.State == WelcomeAnimationState.Playing;
    public bool Failed => Playback.State == WelcomeAnimationState.Failed;
    /// <summary>A viewer played within the interval: the queue working as intended, so it stays quiet.</summary>
    public bool RecentlyPlayed => Playback is { State: WelcomeAnimationState.Skipped, Reason: AnimationPage.RecentlyPlayed };
    public bool Completed => Playback.State == WelcomeAnimationState.Completed;
    /// <summary>Not played for a reason the streamer may want to act on.</summary>
    public bool Unplayed => Playback.State == WelcomeAnimationState.NoReport || Playback.State == WelcomeAnimationState.Skipped && !RecentlyPlayed;
}
