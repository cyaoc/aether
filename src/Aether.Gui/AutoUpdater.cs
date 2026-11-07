using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aether.Core;
using Microsoft.Extensions.Logging;

namespace Aether.Gui;

/// <summary>A release asset as GitHub lists it; Sha256 is the hex of its digest.</summary>
public sealed record ReleaseAsset(string Name, long Size, string Sha256, Uri Url);

/// <summary>A newer release with this program's package; Delta is its delta package from this version, when it has one.</summary>
public sealed record ReleaseUpdate(string Tag, ReleaseAsset Package, ReleaseAsset? Delta);

/// <summary>Where an update is on the splash screen: downloading by bytes, then installing by files.</summary>
public sealed record UpdateProgress(string Tag, bool Downloading, double Percent, long Downloaded = 0, long Total = 0)
{
    private const double Megabyte = 1024 * 1024;
    public string Text => Downloading ? $"正在下载新版本 {Tag}" : $"正在安装 {Tag}，马上就好";
    public string Size => string.Create(CultureInfo.InvariantCulture, $"{Downloaded / Megabyte:0.0} / {Total / Megabyte:0.0} MB");
}

/// <summary>ADR 0015: the GUI checks GitHub for a newer release when it opens, and swaps the program directory's files for that package's.</summary>
public sealed partial class AutoUpdater(HttpClient http, Uri latestRelease, string programDirectory, string version,
    string? runtimeIdentifier, bool selfContained, TimeProvider clock, ILogger logger)
{
    private static readonly JsonSerializerOptions SnakeCaseJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly Lock gate = new();
    private CancellationTokenSource? download;

    // Release downloads from GitHub are slow or blocked in China. Only downloads go through it: the check that gives the digest stays on GitHub.
    private const string DownloadProxy = "https://gh-proxy.org/";
    private const string UpdateFolder = ".update";
    private string UpdateDirectory => Path.Combine(programDirectory, UpdateFolder);
    // Held only while files are moved in (FileLock).
    private static string InstallLock(string programDirectory) => Path.Combine(programDirectory, UpdateFolder, "install.lock");

    /// <summary>This program, as release.yml published it; a dotnet build has no runtime identifier and never updates.</summary>
    public static AutoUpdater ForThisProgram(ILogger<AutoUpdater> logger, TimeProvider clock)
    {
        var assembly = typeof(AutoUpdater).Assembly;
        string? Metadata(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(item => item.Key == key)?.Value;
        // The download has no time limit: the streamer can skip it instead.
        return new(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, new Uri("https://api.github.com/repos/cyaoc/aether/releases/latest"),
            AppContext.BaseDirectory, MainWindowViewModel.AppVersion[1..],
            Metadata("RuntimeIdentifier"), bool.TryParse(Metadata("SelfContained"), out var value) && value, clock, logger);
    }

    /// <summary>The newer release to install, or null: none, not a release build, or the check failed or took over 3 seconds.</summary>
    public async Task<ReleaseUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            // A prerelease version such as the dev build's 0.0.0-dev does not parse.
            if (runtimeIdentifier is not { Length: > 0 } rid || !Version.TryParse(version, out var current) || AnotherGuiRuns()) return null;
            using var request = Request(latestRelease);
            using var response = await http.SendAsync(request, linked.Token);
            response.EnsureSuccessStatusCode();
            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(SnakeCaseJson, linked.Token);
            if (release?.TagName is not { } tag || !TagPattern().IsMatch(tag) || !Version.TryParse(tag[1..], out var latest) || latest <= current) return null;
            var name = $"aether-gui-{tag}-{rid}{(selfContained ? "" : "-framework-dependent")}";
            var extension = rid.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz";
            if (Asset(name + extension) is not { } package) return null;
            logger.LogWarning("发现新版本 {Tag}", tag);
            return new(tag, package, Asset($"{name}-from-v{version}{extension}"));

            ReleaseAsset? Asset(string assetName)
            {
                var asset = release.Assets?.FirstOrDefault(item => item.Name == assetName);
                return asset is { Size: > 0, Digest: { } digest, BrowserDownloadUrl: { IsAbsoluteUri: true, Scheme: "https" } url }
                    && DigestPattern().Match(digest) is { Success: true } sha256
                    ? new(assetName, asset.Size, sha256.Groups[1].Value.ToLowerInvariant(), url) : null;
            }
        }
        // ADR 0015: a check that cannot find out counts as no newer version, so it never keeps the GUI from opening.
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "检查新版本失败");
            return null;
        }
    }

    /// <summary>Downloads, checks and installs the update, from its delta package when there is one; false when it was skipped,
    /// cancelled or failed, with the program directory as it was.</summary>
    public async Task<bool> UpdateAsync(ReleaseUpdate update, IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        var tag = update.Tag;
        var staging = Path.Combine(UpdateDirectory, tag);
        try
        {
            var prepared = false;
            if (update.Delta is { } delta)
            {
                var archive = await Task.Run(() => DownloadAsync(tag, delta, progress, cancellationToken));
                prepared = await Task.Run(() => PrepareDelta(tag, archive, staging, progress));
                if (!prepared)
                {
                    logger.LogWarning("程序目录里的文件和差异包的清单对不上，改下载完整的发布包");
                    Directory.Delete(staging, recursive: true);
                }
            }
            if (!prepared)
            {
                var archive = await Task.Run(() => DownloadAsync(tag, update.Package, progress, cancellationToken));
                await Task.Run(() => Unpack(tag, archive, staging, progress));
            }
            // A GUI opened from here would run a mix of old and new files. Taking the lock before looking for one means a GUI
            // opening meanwhile either shows up in that look, or finds the lock held and quits (Program.Main).
            using (new FileStream(InstallLock(programDirectory), new FileStreamOptions
                { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.DeleteOnClose }))
            {
                if (AnotherGuiRuns()) throw new IOException("程序目录里又开了一个 GUI，这次不更新");
                await Task.Run(() => Install(tag, staging, progress));
            }
            return true;
        }
        catch (Exception error)
        {
            // Whatever went wrong, this version opens as usual and the next start tries again.
            if (error is OperationCanceledException) logger.LogInformation("没有装新版本 {Tag}：下载已取消", update.Tag);
            else logger.LogWarning(error, "没有装新版本 {Tag}", update.Tag);
            TryDelete(() => Directory.Delete(UpdateDirectory, recursive: true));
            return false;
        }
    }

    /// <summary>Whether a GUI is moving an update into <paramref name="programDirectory"/> right now. One opened meanwhile quits instead
    /// of running a mix of old and new files; the installing one then opens the new version.</summary>
    public static bool IsInstalling(string programDirectory)
    {
        try
        {
            using var _ = new FileStream(InstallLock(programDirectory), FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException error) when (FileLock.IsHeld(error)) { return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Whether this program's own assembly differs from the one now in <paramref name="programDirectory"/>: it was loaded
    /// before an update finished moving in, and what it loads from here on would be the new version's.</summary>
    public static bool IsOutdated(string programDirectory)
    {
        try
        {
            return AssemblyName.GetAssemblyName(Path.Combine(programDirectory, "Aether.Gui.dll")).Version != typeof(AutoUpdater).Assembly.GetName().Version;
        }
        catch (Exception error) when (error is IOException or BadImageFormatException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Stops the download under way; once it has finished, there is nothing left to skip.</summary>
    public void Skip()
    {
        lock (gate) download?.Cancel();
    }

    /// <summary>Deletes what the last update left: the replaced files (*.old) and .update/. A file the old process still holds stays until next time.</summary>
    public void CleanUp()
    {
        // Another GUI here may be installing, and these are what it moves in and puts back if that fails.
        if (AnotherGuiRuns()) return;
        TryDelete(() => Directory.Delete(UpdateDirectory, recursive: true));
        TryDelete(() =>
        {
            var directories = Directory.EnumerateDirectories(programDirectory).Where(directory => !Reserved(Path.GetFileName(directory)));
            foreach (var old in Directory.EnumerateFiles(programDirectory, "*.old")
                .Concat(directories.SelectMany(directory => Directory.EnumerateFiles(directory, "*.old", SearchOption.AllDirectories))))
                TryDelete(() => File.Delete(old));
        });
    }

    /// <summary>Starts the installed version from the program directory, without arguments, as this process exits.</summary>
    public void Restart()
    {
        var program = Path.Combine(programDirectory, OperatingSystem.IsWindows() ? "Aether.Gui.exe" : "Aether.Gui");
        try
        {
            // Opened from Finder, the GUI runs in a Terminal session that ends with this process, taking the terminal away from
            // a child started the usual way: this one gets /dev/null and ignores the session's hang-up.
            using var started = Process.Start(OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "nohup \"$0\" >/dev/null 2>&1 </dev/null &", program } }
                : new ProcessStartInfo(program));
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            logger.LogError(error, "新版本没能启动");
        }
    }

    private async Task<string> DownloadAsync(string tag, ReleaseAsset asset, IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(UpdateDirectory);
        var path = Path.Combine(UpdateDirectory, asset.Name);
        using var skip = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (gate) download = skip;
        try
        {
            try { await FromAsync(new Uri(DownloadProxy + asset.Url.AbsoluteUri)); }
            // The size and digest still come from GitHub, so a proxy that sends something else fails here too.
            catch (Exception error) when (!skip.IsCancellationRequested)
            {
                logger.LogWarning(error, "从 gh-proxy.org 下载 {Asset} 失败，改直连 GitHub", asset.Name);
                await FromAsync(asset.Url);
            }
        }
        finally
        {
            lock (gate) download = null;
        }
        // Checking the digest belongs to the download's last moment; installing starts from 0.
        progress.Report(new(tag, Downloading: false, 0));
        return path;

        async Task FromAsync(Uri url)
        {
            progress.Report(new(tag, Downloading: true, 0, 0, asset.Size));
            using var request = Request(url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, skip.Token);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(skip.Token);
            await using var file = File.Create(path);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long downloaded = 0;
            for (int read; (read = await source.ReadAsync(buffer, skip.Token)) > 0;)
            {
                if ((downloaded += read) > asset.Size) throw new InvalidDataException($"{asset.Name} 比 GitHub 说的 {asset.Size} 字节大");
                sha256.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), skip.Token);
                progress.Report(new(tag, Downloading: true, 100.0 * downloaded / asset.Size, downloaded, asset.Size));
            }
            if (Convert.ToHexStringLower(sha256.GetHashAndReset()) != asset.Sha256) throw new InvalidDataException($"{asset.Name} 的 sha256 和 GitHub 说的不一样");
        }
    }

    /// <summary>Unpacks the package's files from inside its aether-gui-… directory: the first half of installing.</summary>
    private static void Unpack(string tag, string archive, string staging, IProgress<UpdateProgress> progress)
    {
        var count = Entries(archive).Count();
        var done = 0;
        foreach (var (name, extract) in Entries(archive))
        {
            extract(Target(staging, name.Split('/', 2) is [_, var path] ? path : throw new InvalidDataException($"{name} 不在发布包的目录里")));
            progress.Report(new(tag, Downloading: false, 50.0 * ++done / count));
        }
    }

    /// <summary>Unpacks the delta package and checks each file in its manifest: the delta's copy, or else the program directory's,
    /// for the first half of installing. False when one is missing or differs, so the whole package is needed.</summary>
    private bool PrepareDelta(string tag, string archive, string staging, IProgress<UpdateProgress> progress)
    {
        foreach (var (name, extract) in Entries(archive)) extract(Target(staging, name));
        var manifestPath = Path.Combine(staging, "manifest.json");
        var manifest = JsonSerializer.Deserialize<DeltaManifest>(File.ReadAllBytes(manifestPath), SnakeCaseJson)?.Files
            ?? throw new InvalidDataException("差异包的清单里没有文件");
        // The manifest describes the program; it is not one of its files.
        File.Delete(manifestPath);
        var done = 0;
        foreach (var (path, sha256) in manifest)
        {
            var file = Target(staging, path);
            if (!File.Exists(file)) file = Target(programDirectory, path);
            if (!File.Exists(file) || !string.Equals(Sha256(file), sha256, StringComparison.OrdinalIgnoreCase)) return false;
            progress.Report(new(tag, Downloading: false, 50.0 * ++done / manifest.Count));
        }
        return true;
    }

    /// <summary>Moves each unpacked file into the program directory, renaming the file it replaces to .old: a running exe or dll
    /// cannot be overwritten on Windows but can be renamed. A failure undoes every step.</summary>
    private void Install(string tag, string staging, IProgress<UpdateProgress> progress)
    {
        var files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        List<string> created = [], renamed = [], added = [];
        try
        {
            foreach (var source in files)
            {
                var target = Target(programDirectory, Path.GetRelativePath(staging, source));
                CreateDirectory(Path.GetDirectoryName(target)!, created);
                if (File.Exists(target))
                {
                    File.Move(target, target + ".old", overwrite: true);
                    renamed.Add(target);
                }
                File.Move(source, target);
                added.Add(target);
                progress.Report(new(tag, Downloading: false, 50 + 50.0 * added.Count / files.Length));
            }
        }
        catch
        {
            foreach (var target in Enumerable.Reverse(added)) Undo(() => File.Delete(target));
            foreach (var target in Enumerable.Reverse(renamed)) Undo(() => File.Move(target + ".old", target));
            foreach (var directory in Enumerable.Reverse(created)) Undo(() => Directory.Delete(directory));
            throw;
        }

        void Undo(Action step)
        {
            try { step(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                logger.LogError(error, "安装新版本失败后没能还原程序目录");
            }
        }
    }

    private static void CreateDirectory(string directory, List<string> created)
    {
        if (Directory.Exists(directory)) return;
        CreateDirectory(Path.GetDirectoryName(directory)!, created);
        Directory.CreateDirectory(directory);
        created.Add(directory);
    }

    /// <summary>Each file in a .zip or .tar.gz, by its path in the archive, with the way to write it out to a new file.</summary>
    private static IEnumerable<(string Name, Action<string> Extract)> Entries(string archive)
    {
        if (archive.EndsWith(".zip", StringComparison.Ordinal))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries.Where(entry => !entry.FullName.EndsWith('/')))
                yield return (entry.FullName, path => entry.ExtractToFile(InDirectory(path)));
            yield break;
        }
        using var tar = new TarReader(new GZipStream(File.OpenRead(archive), CompressionMode.Decompress));
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                // Keeps the mode, so Aether.Gui stays executable.
                yield return (entry.Name, path => entry.ExtractToFile(InDirectory(path), overwrite: false));
            else if (entry.EntryType is not (TarEntryType.Directory or TarEntryType.GlobalExtendedAttributes))
                throw new InvalidDataException($"{entry.Name} 不是普通文件");
        }
    }

    private static string InDirectory(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>Where a path from a package goes under <paramref name="root"/>; one that would leave it, or reach data/ or .update/, is refused.</summary>
    private static string Target(string root, string path)
    {
        var target = Path.GetFullPath(Path.Combine(root, path));
        var first = Path.GetRelativePath(root, target).Split(Path.DirectorySeparatorChar)[0];
        if (Path.IsPathRooted(path) || Path.IsPathRooted(first) || first is "." or ".." || Reserved(first))
            throw new InvalidDataException($"发布包里有不能安装的路径：{path}");
        return target;
    }

    private static bool Reserved(string name) =>
        name.Equals("data", StringComparison.OrdinalIgnoreCase) || name.Equals(UpdateFolder, StringComparison.OrdinalIgnoreCase);

    private static string Sha256(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }

    private static void TryDelete(Action delete)
    {
        try { delete(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    // ADR 0003 lets several GUIs run, and replacing the files would have another one load the new version's later.
    private bool AnotherGuiRuns()
    {
        // Both sides go through GetFullPath so Windows expands 8.3 short names alike, as WebService does.
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(programDirectory));
        Process[] processes;
        try { processes = Process.GetProcessesByName("Aether.Gui"); }
        // Not knowing counts as one running: nothing gets cleaned up or installed under it.
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or PlatformNotSupportedException) { return true; }
        var found = false;
        foreach (var process in processes)
            using (process)
                try
                {
                    // Windows can list a process for a moment after it has exited.
                    found |= process.Id != Environment.ProcessId && !process.HasExited && process.MainModule?.FileName is { } file &&
                        string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), directory, StringComparison.OrdinalIgnoreCase);
                }
                // A process that has exited or belongs to another user is not one of ours.
                catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
        return found;
    }

    private HttpRequestMessage Request(Uri uri) => new(HttpMethod.Get, uri) { Headers = { UserAgent = { new ProductInfoHeaderValue("Aether", version) } } };

    [GeneratedRegex(@"^v\d+\.\d+\.\d+$")]
    private static partial Regex TagPattern();

    [GeneratedRegex("^sha256:([0-9a-fA-F]{64})$")]
    private static partial Regex DigestPattern();

    private sealed record GitHubRelease(string? TagName, GitHubAsset[]? Assets);
    private sealed record GitHubAsset(string? Name, long Size, string? Digest, Uri? BrowserDownloadUrl);
    private sealed record DeltaManifest(Dictionary<string, string>? Files);
}
