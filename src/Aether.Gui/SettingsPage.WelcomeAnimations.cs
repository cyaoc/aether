using System.Globalization;
using Aether.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Aether.Gui;

public partial class SettingsPage
{
    private readonly Func<Task<string?>>? pickAnimationVideo;
    private readonly Dictionary<Button, IDisposable> copyFeedback = [];
    private IEnumerable<WelcomeAnimationRow> Animations => AnimationRows.Children.Cast<WelcomeAnimationRow>();
    private string AnimationDirectory => Settings.VideoDirectory(dataDirectory);

    private async void CopyPageAddress(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(button.Tag as string ?? "");
            if (copyFeedback.Remove(button, out var pending)) pending.Dispose();
            button.Classes.Set("copied", true);
            copyFeedback[button] = DispatcherTimer.RunOnce(() => { button.Classes.Set("copied", false); copyFeedback.Remove(button); }, TimeSpan.FromSeconds(2));
        }
        catch (Exception error) { Message.Text = $"复制地址失败：{error.Message}"; }
    }

    private void LoadAnimations()
    {
        AnimationRows.Children.Clear();
        foreach (var (uid, video) in settings.WelcomeAnimationVideos)
            InsertAnimation(uid.ToString(CultureInfo.InvariantCulture), video.Value, settings.CanEditWelcomeAnimationVideos && video.CanEdit);
        AddAnimationButton.IsEnabled = settings.CanEditWelcomeAnimationVideos;
        AnimationVideosReadOnly.IsVisible = !settings.CanEditWelcomeAnimationVideos;
    }

    private WelcomeAnimationRow InsertAnimation(string uid, string fileName, bool canEdit, bool isNew = false)
    {
        var row = new WelcomeAnimationRow(uid, fileName, canEdit, isNew);
        AnimationRows.Children.Add(row);
        row.Changed += Validate;
        row.RemoveRequested += () => { AnimationRows.Children.Remove(row); Validate(); };
        row.ChooseRequested += async () => await ChooseAnimationAsync(row);
        row.TestRequested += () =>
        {
            RefreshAnimations();
            if (row.CanTest && row.Uid is { } uid) viewModel?.WebService.TestOrStopAnimation(uid, row.FileName);
        };
        return row;
    }

    /// <summary>The row holds the video the settings file maps its UID to, with no new file picked.</summary>
    private bool MatchesFile(WelcomeAnimationRow row) => row.SourcePath is null && row.Uid is { } uid
        && settings.WelcomeAnimationVideos.TryGetValue(uid, out var video) && video.Value == row.FileName;

    private bool AnimationDraft => Animations.Count() != settings.WelcomeAnimationVideos.Count || !Animations.All(MatchesFile);

    private bool RefreshAnimations()
    {
        var invalid = false;
        var uids = new HashSet<long>();
        var first = true;
        foreach (var row in Animations)
        {
            row.ShowDivider(!first);
            first = false;
            var saved = MatchesFile(row) && (viewModel is null || viewModel.WebService.AnimationVideoSaved(row.Uid!.Value, row.FileName));
            invalid |= row.Refresh(uids, AnimationDirectory, saved, settings.WelcomeAnimationEnabled.Value && viewModel?.WebService.HasVideoConnections == true,
                viewModel?.WebService.AnimationBusy == true || viewModel?.WebService.AnimationReloading == true,
                row.SourcePath is null && row.Uid is { } playingUid ? viewModel?.WebService.PlayingAnimation(playingUid, row.FileName) : null,
                row.SourcePath is null && row.Uid is { } failedUid && viewModel?.WebService.AnimationFailed(failedUid, row.FileName) == true);
        }
        var count = AnimationRows.Children.Count;
        AnimationCount.Text = count > 0 ? $"{count} 位" : "";
        AnimationRowsBorder.IsVisible = count > 0;
        EmptyAnimations.IsVisible = count == 0;
        return invalid;
    }

    private async Task ChooseAnimationAsync(WelcomeAnimationRow? row = null)
    {
        if (!settings.CanEditWelcomeAnimationVideos || row?.CanEdit == false) return;
        try
        {
            var path = pickAnimationVideo is not null ? await pickAnimationVideo() : await PickVideoAsync();
            if (path is null) return;
            if (Settings.ValidateVideoFileName(Path.GetFileName(path)) is { } reason)
            {
                Message.Text = reason;
                return;
            }
            row ??= InsertAnimation("", Path.GetFileName(path), true, isNew: true);
            row.SelectVideo(path);
            Validate();
            // After the picker hands focus back, or that would count as leaving the new row.
            Dispatcher.UIThread.Post(row.FocusInput, DispatcherPriority.Loaded);
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

    /// <param name="snapshot">The settings the form was filled from; runs off the UI thread, so it does not read the field.</param>
    private string SaveAnimationFiles(Dictionary<string, string> changes, Dictionary<long, (string FileName, string? Source)> rows, Settings snapshot)
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
            Settings.SaveWelcomeAnimations(dataDirectory, changes, videos, snapshot);
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
        // The mapping is committed, so backups go whatever happens next; each cleanup failure is reported, not fatal.
        var warnings = new List<string>();
        foreach (var (_, backup) in backups)
            try { File.Delete(backup); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { warnings.Add($"旧视频备份未删除：{cleanup.Message}"); }
        var saved = Settings.Load(dataDirectory);
        if (saved.Error is not null) warnings.Add("设置文件随后发生变化，未清理旧视频");
        else
        {
            var used = saved.WelcomeAnimationVideos.Values.Select(video => video.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in snapshot.WelcomeAnimationVideos.Values.Select(video => video.Value).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(AnimationDirectory, name);
                if (used.Contains(name) || sources.Contains(Path.GetFullPath(path))) continue;
                try { File.Delete(path); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { warnings.Add($"旧视频未删除：{cleanup.Message}"); }
            }
        }
        return warnings.Count == 0 ? "已保存。" : $"已保存；{string.Join("；", warnings)}";
    }
}
