using System.Globalization;
using Aether.Core;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class SettingsPage
{
    private readonly Func<Task<string?>>? pickAnimationVideo;
    private IDisposable? copyFeedback;
    private IEnumerable<WelcomeAnimationRow> Animations => AnimationRows.Children.Cast<WelcomeAnimationRow>();
    private string AnimationDirectory => Path.Combine(dataDirectory, "welcome-animations");

    private async Task CopyAnimationAddressAsync()
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(AnimationAddress.Text ?? "");
            copyFeedback?.Dispose();
            CopyAnimationAddressButton.Content = "已复制";
            copyFeedback = DispatcherTimer.RunOnce(() => CopyAnimationAddressButton.Content = "复制", TimeSpan.FromSeconds(2));
        }
        catch (Exception error)
        {
            Message.Text = $"复制地址失败：{error.Message}";
        }
    }

    private void LoadAnimations()
    {
        AnimationRows.Children.Clear();
        foreach (var (uid, video) in settings.WelcomeAnimationVideos)
            InsertAnimation(uid.ToString(CultureInfo.InvariantCulture), video.Value, settings.CanEditWelcomeAnimationVideos && video.CanEdit);
        AddAnimationButton.IsEnabled = settings.CanEditWelcomeAnimationVideos;
        AnimationsReadOnly.IsVisible = !settings.CanEditWelcomeAnimationVideos;
    }

    private WelcomeAnimationRow InsertAnimation(string uid, string fileName, bool canEdit)
    {
        var row = new WelcomeAnimationRow(uid, fileName, canEdit);
        AnimationRows.Children.Add(row);
        row.Changed += Validate;
        row.RemoveRequested += () => { AnimationRows.Children.Remove(row); Validate(); };
        row.ChooseRequested += async () => await ChooseAnimationAsync(row);
        row.TestRequested += () =>
        {
            RefreshAnimations();
            if (row.FindControl<Button>("TestButton")!.IsEnabled && row.Uid is { } uid) viewModel?.TestAnimation(uid, row.FileName);
        };
        return row;
    }

    private bool AnimationDraft => Animations.Count() != settings.WelcomeAnimationVideos.Count
        || Animations.Any(row => row.SourcePath is not null || row.Uid is not { } uid
            || !settings.WelcomeAnimationVideos.TryGetValue(uid, out var video) || video.Value != row.FileName);

    private bool RefreshAnimations()
    {
        var invalid = false;
        var uids = new HashSet<long>();
        foreach (var row in Animations)
        {
            var saved = row.SourcePath is null && row.Uid is { } uid
                && settings.WelcomeAnimationVideos.TryGetValue(uid, out var video) && video.Value == row.FileName
                && (viewModel is null || viewModel.AnimationVideoSaved(uid, row.FileName));
            invalid |= row.Refresh(uids, AnimationDirectory, saved, viewModel?.HasAnimationPages == true,
                viewModel?.AnimationBusy == true || viewModel?.AnimationReloading == true,
                saved && row.Uid is { } playingUid && viewModel?.AnimationPlaying(playingUid, row.FileName) == true,
                row.SourcePath is null && row.Uid is { } failedUid && viewModel?.AnimationFailed(failedUid, row.FileName) == true);
        }
        AnimationCount.Text = $"{AnimationRows.Children.Count} 位";
        EmptyAnimations.IsVisible = AnimationRows.Children.Count == 0;
        return invalid;
    }

    private async Task ChooseAnimationAsync(WelcomeAnimationRow? row = null)
    {
        if (!settings.CanEditWelcomeAnimationVideos || row?.CanEdit == false) return;
        try
        {
            var path = pickAnimationVideo is not null ? await pickAnimationVideo() : await PickVideoAsync();
            if (path is null) return;
            if (Settings.Validate("welcome_animation_videos.1", Path.GetFileName(path)) is { } reason)
            {
                Message.Text = reason;
                return;
            }
            row ??= InsertAnimation("", Path.GetFileName(path), true);
            row.SelectVideo(path);
            Validate();
            row.FindControl<TextBox>("UidInput")!.Focus();
            row.BringIntoView();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Message.Text = $"选择视频失败：{error.Message}";
        }
    }

    private async Task<string?> PickVideoAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return null;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择欢迎动画视频", AllowMultiple = false,
            FileTypeFilter = [new("MP4 / WebM 视频") { Patterns = ["*.mp4", "*.webm"], MimeTypes = ["video/mp4", "video/webm"] }]
        });
        using var file = files.FirstOrDefault();
        return file?.TryGetLocalPath();
    }

    private string SaveAnimationFiles(Dictionary<string, string> changes, Dictionary<long, (string FileName, string? Source)> rows)
    {
        var created = new List<string>();
        var backups = new List<(string Destination, string Backup)>();
        var sources = rows.Values.Where(row => row.Source is not null).Select(row => Path.GetFullPath(row.Source!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var videos = rows.ToDictionary(pair => pair.Key, pair => pair.Value.FileName);
        var current = Settings.Load(dataDirectory);
        if (current.Error is { } error) throw error;
        var reserved = current.WelcomeAnimationVideos.Values.Select(video => video.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(AnimationDirectory)) reserved.UnionWith(Directory.EnumerateFileSystemEntries(AnimationDirectory).Select(Path.GetFileName)!);
        try
        {
            foreach (var (uid, row) in rows.Where(pair => pair.Value.Source is not null))
            {
                Directory.CreateDirectory(AnimationDirectory);
                var name = row.FileName;
                var ownFile = current.WelcomeAnimationVideos.TryGetValue(uid, out var own)
                    && string.Equals(own.Value, name, StringComparison.OrdinalIgnoreCase)
                    && !current.WelcomeAnimationVideos.Any(pair => pair.Key != uid && string.Equals(pair.Value.Value, name, StringComparison.OrdinalIgnoreCase));
                // A managed copy can itself have been selected as another row's source; it must stay untouched too.
                if (ownFile && sources.Contains(Path.GetFullPath(Path.Combine(AnimationDirectory, own!.Value)))
                    && !string.Equals(Path.GetFullPath(row.Source!), Path.GetFullPath(Path.Combine(AnimationDirectory, own.Value)), StringComparison.OrdinalIgnoreCase))
                    ownFile = false;
                if (ownFile) name = own!.Value;
                else
                    for (var suffix = 2; !reserved.Add(name); suffix++)
                        name = $"{Path.GetFileNameWithoutExtension(row.FileName)} ({suffix}){Path.GetExtension(row.FileName)}";
                var destination = Path.Combine(AnimationDirectory, name);
                if (ownFile && string.Equals(Path.GetFullPath(row.Source!), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    continue;
                var staged = ownFile ? Path.Combine(AnimationDirectory, ".import-" + Guid.NewGuid()) : destination;
                using (var source = File.OpenRead(row.Source!))
                using (var target = new FileStream(staged, FileMode.CreateNew, FileAccess.Write))
                {
                    created.Add(staged);
                    source.CopyTo(target);
                }
                if (ownFile)
                {
                    if (File.Exists(destination))
                    {
                        var backup = Path.Combine(AnimationDirectory, ".backup-" + Guid.NewGuid());
                        File.Move(destination, backup);
                        backups.Add((destination, backup));
                    }
                    File.Move(staged, destination);
                    created.Remove(staged);
                    created.Add(destination);
                }
                videos[uid] = name;
            }
            Settings.SaveWelcomeAnimations(dataDirectory, changes, videos, settings);
        }
        catch (Exception failure)
        {
            var errors = new List<string>();
            foreach (var path in created)
                try { File.Delete(path); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { errors.Add(cleanup.Message); }
            foreach (var (destination, backup) in backups)
                try { File.Move(backup, destination); }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException) { errors.Add($"旧视频保留在 {backup}：{restore.Message}"); }
            if (errors.Count > 0) throw new IOException($"{failure.Message}；恢复文件时出错：{string.Join("；", errors)}", failure);
            throw;
        }
        var saved = Settings.Load(dataDirectory);
        if (saved.Error is not null) return "已保存；设置文件随后发生变化，未清理旧视频。";
        foreach (var (_, backup) in backups)
            try { File.Delete(backup); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { return $"已保存；旧视频备份未删除：{cleanup.Message}"; }
        var used = saved.WelcomeAnimationVideos.Values.Select(video => video.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in settings.WelcomeAnimationVideos.Values.Select(video => video.Value).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(AnimationDirectory, name);
            if (used.Contains(name) || sources.Contains(Path.GetFullPath(path))) continue;
            try { File.Delete(path); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                return $"已保存；旧视频未删除：{cleanup.Message}";
            }
        }
        return "已保存。";
    }
}
