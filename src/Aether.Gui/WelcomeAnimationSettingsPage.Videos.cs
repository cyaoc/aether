using System.Globalization;
using Aether.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Media;

namespace Aether.Gui;

public partial class WelcomeAnimationSettingsPage
{
    private readonly Func<Task<string?>>? pickAnimationVideo;
    internal EditableRows<WelcomeAnimationRow> Animations { get; } = new(0);
    private string AnimationDirectory => Settings.VideoDirectory(dataDirectory);

    private void LoadAnimations()
    {
        Animations.Clear();
        foreach (var (uid, video) in settings.WelcomeAnimationVideos)
            Animations.Add(new(uid.ToString(CultureInfo.InvariantCulture), video.Value, settings.CanEditWelcomeAnimationVideos && video.CanEdit));
        AddAnimationButton.IsEnabled = settings.CanEditWelcomeAnimationVideos;
        AnimationVideosReadOnly.IsVisible = !settings.CanEditWelcomeAnimationVideos;
    }

    public void TestAnimation(WelcomeAnimationRow row)
    {
        RefreshAnimations();
        if (row.CanTest && row.Uid is { } uid) viewModel?.WebService.TestOrStopAnimation(uid, row.FileName);
    }

    /// <summary>The row holds the video the settings file maps its UID to, with no new file picked.</summary>
    private bool MatchesFile(WelcomeAnimationRow row) => row.SourcePath is null && row.Uid is { } uid
        && settings.WelcomeAnimationVideos.TryGetValue(uid, out var video) && video.Value == row.FileName;

    private bool AnimationDraft => Animations.Count != settings.WelcomeAnimationVideos.Count || !Animations.All(MatchesFile);

    private bool RefreshAnimations()
    {
        var invalid = false;
        var uids = new HashSet<long>();
        foreach (var row in Animations)
        {
            var saved = MatchesFile(row) && (viewModel is null || viewModel.WebService.AnimationVideoSaved(row.Uid!.Value, row.FileName));
            var hasPages = settings.WelcomeAnimationEnabled.Value && viewModel?.WebService.HasVideoConnections == true;
            var busy = viewModel?.WebService.AnimationBusy == true || viewModel?.WebService.AnimationReloading == true;
            var playback = row.SourcePath is null && row.Uid is { } playingUid ? viewModel?.WebService.PlayingAnimation(playingUid, row.FileName) : null;
            var failed = row.SourcePath is null && row.Uid is { } failedUid && viewModel?.WebService.AnimationFailed(failedUid, row.FileName) == true;
            var playing = playback is not null;
            var canStop = playback?.IsTest == true;
            row.Error = string.IsNullOrWhiteSpace(row.UidText) ? "填一个 UID，或删掉这条"
                : row.Uid is not { } uid ? "UID 必须是正整数"
                : !uids.Add(uid) ? "和上面那条重复了" : null;
            invalid |= row.Error is not null;
            var exists = File.Exists(row.SourcePath ?? Path.Combine(AnimationDirectory, row.FileName));
            row.Missing = !exists;
            row.FileIcon = Icon(exists ? "Icon.Animation" : "Icon.Alert");
            row.FileNameDecorations = exists ? null : TextDecorations.Strikethrough;
            // Show what blocks saving or playing first, then the last playback failure, then progress.
            var (note, tone, icon) = row.VisibleError is { } error ? (error, "error", "Icon.Alert")
                : !exists ? ("找不到视频文件，点文件名重新选择", "error", "Icon.Alert")
                : failed ? ("上次播放失败：格式不支持，换成 H.264 编码的 MP4", "error", "Icon.Fail")
                : !saved ? ("保存后才能试播", null, "Icon.Info")
                : playing ? (canStop ? "正在直播姬里试播" : "正在直播姬里播放", "playing", "Icon.Wave") : ("", null, "Icon.Info");
            if (!row.CanEdit) note += (note.Length > 0 ? "\n" : "") + "这项不能逐行编辑，请在设置文件里改";
            row.RowMessage = note;
            row.NoteIcon = Icon(icon);
            row.NoteError = tone == "error";
            row.NotePlaying = tone == "playing";
            var who = row.Uid is { } owner ? $"{owner} 的" : "";
            row.FileAccessibleName = $"更换{who}视频：{row.FileName}";
            var (action, testIcon, testText) = canStop ? ("停止试播", "Icon.Stop", "停止")
                : playing ? ("正在播放", "Icon.Wave", "播放中") : ("试播", "Icon.Play", "试播");
            row.TestAccessibleName = action + who + "欢迎动画";
            row.RemoveAccessibleName = $"删除{who}欢迎动画";
            row.Playing = playing;
            row.Stoppable = canStop;
            row.TestIcon = Icon(testIcon);
            row.TestText = testText;
            row.CanTest = canStop || row.Error is null && saved && hasPages && exists && !busy;
        }
        var count = Animations.Count;
        AnimationCount.Text = count > 0 ? $"{count} 位" : "";
        AnimationRowsBorder.IsVisible = count > 0;
        EmptyAnimations.IsVisible = count == 0;
        return invalid;
    }

    public async Task ChooseAnimationAsync(WelcomeAnimationRow? row = null)
    {
        if (!settings.CanEditWelcomeAnimationVideos || row?.CanEdit == false) return;
        try
        {
            var path = pickAnimationVideo is not null ? await pickAnimationVideo() : await PickVideoAsync();
            if (path is null) return;
            if (Settings.ValidateVideoFileName(Path.GetFileName(path)) is { } reason)
            {
                Message = reason;
                return;
            }
            if (row is null)
            {
                row = new("", Path.GetFileName(path), true, isNew: true);
                row.SelectVideo(path);
                Animations.AddNew(row);
            }
            else
            {
                row.SelectVideo(path);
                row.RequestFocus();
            }
            Validate();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Message = $"选择视频失败：{error.Message}";
        }
    }

    private static Geometry Icon(string key) => (Geometry)Application.Current!.FindResource(key)!;

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
