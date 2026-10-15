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
    public ObservableCollection<WelcomeAnimationItem> RecentAnimations { get; } = [];
    private readonly Queue<string> finishedAnimations = new();
    private readonly HashSet<string> pendingAnimations = [];
    private readonly HashSet<(long Uid, string FileName)> failedAnimationVideos = [];
    private WelcomeAnimationPlayback? playingAnimation;
    private bool testPlayPending;
    [ObservableProperty] private bool animationBusy;

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
        OnPropertyChanged(nameof(AnimationReloading));
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
                OnPropertyChanged(nameof(AnimationReloading));
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
    private AnimationPageStatus animationPageStatus = new(false, 17890, 0);

    public bool HasAnimationPages => AnimationPageStatus.Running && AnimationPageStatus.ConnectedPages > 0;
    public bool HasAnimationError => AnimationPageStatus.Error is not null;
    public bool ShowAnimationSetup => !HasAnimationPages && !HasAnimationError;
    public string AnimationAddressText => $"http://localhost:{AnimationPageStatus.Port}/";
    public string AnimationPageText => AnimationPageStatus.Error is { } error ? "✕ " + error
        : HasAnimationPages ? $"✓ 已有 {AnimationPageStatus.ConnectedPages} 个推流软件连着动画页"
        : "⚠ 还没有推流软件连着动画页";

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
                if (finishedAnimations.Count > FinishedSendLimit)
                {
                    var oldest = finishedAnimations.Dequeue();
                    if (RecentAnimations.FirstOrDefault(value => value.Playback.Id == oldest) is { } old) RecentAnimations.Remove(old);
                }
            }
            if (playback.State == WelcomeAnimationState.Playing) playingAnimation = playback;
            else if (playingAnimation?.Id == playback.Id) playingAnimation = null;
            if (playback.State == WelcomeAnimationState.Failed) failedAnimationVideos.Add((playback.Uid, playback.FileName));
        }
        AnimationBusy = testPlayPending || pendingAnimations.Count > 0;
        OnPropertyChanged(nameof(RecentAnimations));
    }

    internal bool AnimationFailed(long uid, string fileName) => failedAnimationVideos.Contains((uid, fileName));
    internal void ForgetAnimationFailures(IReadOnlyCollection<long> replaced)
    {
        failedAnimationVideos.RemoveWhere(video => replaced.Contains(video.Uid));
        OnPropertyChanged(nameof(RecentAnimations));
    }
    internal bool AnimationPlaying(long uid, string fileName) => playingAnimation is { } playing && playing.Uid == uid && playing.FileName == fileName;

    internal bool AnimationVideoSaved(long uid, string fileName) => animationSettings is { } snapshot
        && snapshot.WelcomeAnimationVideos.TryGetValue(uid, out var video) && video.Value == fileName;

    internal void TestAnimation(long uid, string fileName)
    {
        if (AnimationReloading || AnimationBusy || !HasAnimationPages || shuttingDown
            || !AnimationVideoSaved(uid, fileName)) return;
        // Disable immediately, before the queued update crosses back to the UI thread.
        testPlayPending = AnimationBusy = true;
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
    public string Status => Playback.State switch
    {
        WelcomeAnimationState.Queued => "排队中",
        WelcomeAnimationState.Playing => "播放中",
        WelcomeAnimationState.Completed => "已播放",
        WelcomeAnimationState.Failed => "播放失败" + Detail,
        WelcomeAnimationState.NoReport => "没收到播放回报",
        _ => "没播" + Detail
    };
    private string Detail => Playback.Reason is { } reason ? " · " + reason : "";
    public bool Completed => Playback.State == WelcomeAnimationState.Completed;
    public bool Unplayed => Playback.State is WelcomeAnimationState.Failed or WelcomeAnimationState.NoReport or WelcomeAnimationState.Skipped;
}
