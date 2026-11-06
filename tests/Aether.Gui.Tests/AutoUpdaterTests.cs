using Aether.Gui.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Aether.Gui.Tests.Support.FakeGitHub;

namespace Aether.Gui.Tests;

public sealed class AutoUpdaterTests : IDisposable
{
    private const string MacPackage = "aether-gui-v0.5.0-osx-arm64.tar.gz";
    private static readonly Dictionary<string, string> V040 = new()
    {
        ["Aether.Gui"] = "gui 0.4.0", ["Aether.Core.dll"] = "core 0.4.0", ["Pages/video/page.js"] = "video", ["LICENSE"] = "license"
    };
    private static readonly Dictionary<string, string> V050 = new()
    {
        ["Aether.Gui"] = "gui 0.5.0", ["Aether.Core.dll"] = "core 0.5.0", ["Pages/video/page.js"] = "video",
        ["Pages/gift-board/page.js"] = "gift board", ["LICENSE"] = "license"
    };

    // Not under the temp directory: on macOS that is a symlink, and a process's path is reported without it.
    private readonly string programDirectory = Path.Combine(AppContext.BaseDirectory, "auto-update-" + Guid.NewGuid().ToString("N"));
    private readonly FakeGitHub gitHub = new();
    private readonly FakeTimeProvider clock = new();

    public AutoUpdaterTests() => Write(V040);

    public void Dispose()
    {
        if (Directory.Exists(programDirectory)) Directory.Delete(programDirectory, recursive: true);
    }

    [Theory]
    [InlineData("v0.4.0", "0.4.0", "osx-arm64")]
    [InlineData("v0.3.9", "0.4.0", "osx-arm64")]
    [InlineData("v0.5.0", "0.0.0-dev", "osx-arm64")]
    [InlineData("v0.5.0", "0.4.0", null)]
    [InlineData("v0.5.0", "0.4.0", "osx-x64")]
    [InlineData("v99999999999.0.0", "0.4.0", "osx-arm64")]
    public async Task Check_finds_nothing_unless_a_newer_release_has_this_programs_package(string tag, string version, string? rid)
    {
        gitHub.Tag = tag;
        gitHub.Add($"aether-gui-{tag}-osx-arm64.tar.gz", Package(MacPackage, V050));
        Assert.Null(await Updater(version, rid).CheckAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(gitHub.Requests, request => request.Contains("/download/"));
        if (version.Contains('-') || rid is null) Assert.Empty(gitHub.Requests);
    }

    [Theory]
    [InlineData("win-x64", true, "aether-gui-v0.5.0-win-x64.zip")]
    [InlineData("osx-arm64", false, "aether-gui-v0.5.0-osx-arm64-framework-dependent.tar.gz")]
    public async Task Check_picks_the_package_of_the_same_kind(string rid, bool selfContained, string expected)
    {
        foreach (var name in new[] { "aether-gui-v0.5.0-win-x64.zip", "aether-gui-v0.5.0-win-x64-framework-dependent.zip",
            "aether-gui-v0.5.0-osx-arm64.tar.gz", "aether-gui-v0.5.0-osx-arm64-framework-dependent.tar.gz", "aether-cli-v0.5.0-win-x64.zip" })
            gitHub.Add(name, Package(name, V050));
        var update = await Updater(rid: rid, selfContained: selfContained).CheckAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(update);
        Assert.Equal("v0.5.0", update.Tag);
        Assert.Equal(expected, update.Package.Name);
        Assert.Null(update.Delta);
    }

    [Fact]
    public async Task Check_picks_the_delta_package_from_this_version()
    {
        const string delta = "aether-gui-v0.5.0-osx-arm64-from-v0.4.0.tar.gz";
        gitHub.Add(MacPackage, Package(MacPackage, V050));
        gitHub.Add("aether-gui-v0.5.0-osx-arm64-from-v0.3.0.tar.gz", Delta(delta, "v0.3.0", V040, V050));
        gitHub.Add(delta, Delta(delta, "v0.4.0", V040, V050));
        var update = await Updater().CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MacPackage, update!.Package.Name);
        Assert.Equal(delta, update.Delta!.Name);
    }

    [Fact]
    public async Task Check_gives_up_after_3_seconds()
    {
        var never = new TaskCompletionSource();
        gitHub.BeforeRelease = () => never.Task;
        gitHub.Add(MacPackage, Package(MacPackage, V050));
        var check = Updater().CheckAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMilliseconds(2999));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(check.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(await check.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Check_skips_while_another_gui_runs_from_the_program_directory()
    {
        // macOS kills a copied system program, so only Windows has one to run under the GUI's name.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs a copy of PING.EXE.");
        gitHub.Add("aether-gui-v0.5.0-win-x64.zip", Package("aether-gui-v0.5.0-win-x64.zip", V050));
        var copy = Path.Combine(programDirectory, "Aether.Gui.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), copy);
        using var other = System.Diagnostics.Process.Start(copy, "-n 30 127.0.0.1");
        try
        {
            Assert.Null(await Updater(rid: "win-x64").CheckAsync(TestContext.Current.CancellationToken));
            Assert.Empty(gitHub.Requests);
        }
        finally { other.Kill(); await other.WaitForExitAsync(TestContext.Current.CancellationToken); }
        Assert.NotNull(await Updater(rid: "win-x64").CheckAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("osx-arm64", "aether-gui-v0.5.0-osx-arm64.tar.gz")]
    [InlineData("win-x64", "aether-gui-v0.5.0-win-x64.zip")]
    public async Task Package_replaces_files_adds_new_ones_and_keeps_the_rest(string rid, string package)
    {
        Write(new Dictionary<string, string> { ["mine.txt"] = "user file", ["data/aether.db"] = "db", ["data/aether.yml"] = "settings" });
        gitHub.Add(package, Package(package, V050));
        Assert.True(await UpdateAsync(Updater(rid: rid)));
        var installed = Snapshot();
        foreach (var (path, text) in V050) Assert.Equal(text, installed[path]);
        Assert.Equal("user file", installed["mine.txt"]);
        Assert.Equal("db", installed["data/aether.db"]);
        Assert.Equal("settings", installed["data/aether.yml"]);
        foreach (var (path, text) in V040) Assert.Equal(text, installed[path + ".old"]);
        Assert.DoesNotContain("mine.txt.old", installed.Keys);
        Assert.Equal(1, gitHub.Downloads(package));
    }

    [Fact]
    public async Task Installed_program_stays_executable()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no executable bit.");
        gitHub.Add(MacPackage, Package(MacPackage, V050));
        Assert.True(await UpdateAsync(Updater()));
        if (OperatingSystem.IsWindows()) return;
        Assert.True(File.GetUnixFileMode(Path.Combine(programDirectory, "Aether.Gui")).HasFlag(UnixFileMode.UserExecute));
        Assert.False(File.GetUnixFileMode(Path.Combine(programDirectory, "Aether.Core.dll")).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public async Task Wrong_digest_fails_and_leaves_nothing_behind()
    {
        var before = Snapshot();
        gitHub.Add(MacPackage, Package(MacPackage, V050), digest: "sha256:" + new string('0', 64));
        Assert.False(await UpdateAsync(Updater()));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Failed_install_puts_the_program_directory_back()
    {
        // The new file's place is taken by a directory, after a new directory and the files sorted before it have moved in.
        Directory.CreateDirectory(Path.Combine(programDirectory, "Pages/gift-board/page.js"));
        var before = Snapshot();
        gitHub.Add(MacPackage, Package(MacPackage, new Dictionary<string, string>(V050) { ["Fonts/new.ttf"] = "font" }));
        Assert.False(await UpdateAsync(Updater()));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Progress_counts_bytes_then_files_in_two_halves()
    {
        gitHub.Add(MacPackage, Package(MacPackage, V050));
        var reports = new List<UpdateProgress>();
        Assert.True(await UpdateAsync(Updater(), reports.Add));
        var download = reports.TakeWhile(report => report.Downloading).ToArray();
        var install = reports.Skip(download.Length).ToArray();
        var size = gitHub.Size(MacPackage);
        Assert.Equal(new UpdateProgress("v0.5.0", true, 0, 0, size), download[0]);
        Assert.Equal(new UpdateProgress("v0.5.0", true, 100, size, size), download[^1]);
        Assert.Contains(download, report => report.Downloaded == HeldStream.FirstChunk);
        Assert.All(install, report => Assert.False(report.Downloading));
        Assert.Equal(0, install[0].Percent);
        // Five files to unpack, then five to move in.
        Assert.Equal([0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100], install.Select(report => report.Percent));
        Assert.Equal("正在下载新版本 v0.5.0", download[0].Text);
        Assert.Equal("正在安装 v0.5.0，马上就好", install[0].Text);
    }

    [Fact]
    public async Task Skip_cancels_the_download_and_deletes_it()
    {
        var before = Snapshot();
        var held = new TaskCompletionSource();
        gitHub.DownloadHeld = held.Task;
        gitHub.Add(MacPackage, Package(MacPackage, V050));
        var updater = Updater();
        Assert.False(await UpdateAsync(updater, report => { if (report.Downloaded == HeldStream.FirstChunk) updater.Skip(); }));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Skip_after_the_download_finished_does_not_count()
    {
        gitHub.Add(MacPackage, Package(MacPackage, V050));
        var updater = Updater();
        Assert.True(await UpdateAsync(updater, report => { if (!report.Downloading) updater.Skip(); }));
        Assert.Equal("gui 0.5.0", File.ReadAllText(Path.Combine(programDirectory, "Aether.Gui")));
    }

    [Theory]
    [InlineData("osx-arm64", ".tar.gz")]
    [InlineData("win-x64", ".zip")]
    public async Task Delta_package_from_this_version_installs_the_same_program(string rid, string extension)
    {
        var (package, delta) = AddRelease(rid, extension);
        var reports = new List<UpdateProgress>();
        Assert.True(await UpdateAsync(Updater(rid: rid), reports.Add));
        Assert.Equal(V050, ProgramFiles());
        Assert.Equal((0, 1), (gitHub.Downloads(package), gitHub.Downloads(delta)));
        Assert.Equal(gitHub.Size(delta), reports.First(report => report.Downloading).Total);
        // Only the changed files were moved in.
        Assert.Equal(["Aether.Core.dll.old", "Aether.Gui.old"], Snapshot().Keys.Where(path => path.EndsWith(".old")).Order());
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("deleted")]
    public async Task Program_file_not_matching_the_manifest_downloads_the_whole_package(string damage)
    {
        var video = Path.Combine(programDirectory, "Pages/video/page.js");
        if (damage == "changed") File.WriteAllText(video, "edited");
        else File.Delete(video);
        var (package, delta) = AddRelease("osx-arm64", ".tar.gz");
        var updater = Updater();
        var reports = new List<UpdateProgress>();
        Assert.True(await UpdateAsync(updater, reports.Add));
        Assert.Equal(V050, ProgramFiles());
        Assert.Equal((1, 1), (gitHub.Downloads(package), gitHub.Downloads(delta)));
        // Back to downloading, from 0 and sized by the whole package, after half of installing went to checking.
        var again = reports.FindIndex(1, report => report.Downloading && report.Downloaded == 0);
        Assert.Equal(new UpdateProgress("v0.5.0", true, 0, 0, gitHub.Size(package)), reports[again]);
        Assert.All(reports[..again].Where(report => !report.Downloading), report => Assert.InRange(report.Percent, 0, 50));
    }

    [Fact]
    public async Task Program_older_than_the_delta_downloads_the_whole_package()
    {
        Write(new Dictionary<string, string> { ["Aether.Gui"] = "gui 0.3.0" });
        var (package, delta) = AddRelease("osx-arm64", ".tar.gz");
        Assert.True(await UpdateAsync(Updater(version: "0.3.0")));
        Assert.Equal(V050, ProgramFiles());
        Assert.Equal((1, 0), (gitHub.Downloads(package), gitHub.Downloads(delta)));
    }

    [Fact]
    public async Task Wrong_delta_digest_fails_without_the_whole_package()
    {
        var before = Snapshot();
        var (package, delta) = AddRelease("osx-arm64", ".tar.gz");
        gitHub.Add(delta, Delta(delta, "v0.4.0", V040, V050), digest: "sha256:" + new string('0', 64));
        Assert.False(await UpdateAsync(Updater()));
        Assert.Equal(before, Snapshot());
        Assert.Equal(0, gitHub.Downloads(package));
    }

    [Fact]
    public void Clean_up_deletes_replaced_files_and_the_download_folder_but_not_data()
    {
        Write(new Dictionary<string, string>
        {
            ["Aether.Gui.old"] = "", ["Pages/video/page.js.old"] = "", ["Pages/lottery/page.js.old"] = "", [".update/v0.5.0/Aether.Gui"] = "",
            [".update/aether-gui-v0.5.0-osx-arm64.tar.gz"] = "", ["data/aether.db"] = "db", ["data/backup.old"] = "mine"
        });
        // A file the old process still holds: open on Windows, in a read-only directory elsewhere.
        var held = Path.Combine(programDirectory, "Pages/lottery/page.js.old");
        var open = OperatingSystem.IsWindows() ? new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.GetDirectoryName(held)!, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try { Updater().CleanUp(); }
        finally
        {
            open?.Dispose();
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.GetDirectoryName(held)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Assert.Equal(["Pages/lottery/page.js.old", "data/aether.db", "data/backup.old"],
            Snapshot().Where(entry => entry.Value != "/").Select(entry => entry.Key).Where(path => !V040.ContainsKey(path)).Order(StringComparer.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(programDirectory, ".update")));
    }

    [Fact]
    public void Clean_up_without_a_program_directory_does_nothing()
    {
        Directory.Delete(programDirectory, recursive: true);
        Updater().CleanUp();
        Assert.False(Directory.Exists(programDirectory));
    }

    // ADR 0015 rests on these two: a running exe and a loaded dll can be renamed on Windows.
    [Fact]
    public async Task Windows_renames_a_running_program()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to overwrite a running program.");
        var copy = Path.Combine(programDirectory, "PING.EXE");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), copy);
        using var running = System.Diagnostics.Process.Start(copy, "-n 30 127.0.0.1");
        try
        {
            File.Move(copy, copy + ".old");
            Assert.False(running.HasExited);
        }
        finally { running.Kill(); await running.WaitForExitAsync(TestContext.Current.CancellationToken); }
    }

    [Fact]
    public void Windows_renames_a_loaded_dll()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to overwrite a loaded dll.");
        var copy = Path.Combine(programDirectory, "Aether.Core.dll");
        File.Copy(typeof(Aether.Core.Room).Assembly.Location, copy, overwrite: true);
        System.Reflection.Assembly.LoadFile(copy);
        File.Move(copy, copy + ".old");
        Assert.True(File.Exists(copy + ".old"));
    }

    /// <summary>Adds v0.5.0's package and its delta package from v0.4.0.</summary>
    private (string Package, string Delta) AddRelease(string rid, string extension)
    {
        var package = $"aether-gui-v0.5.0-{rid}{extension}";
        var delta = $"aether-gui-v0.5.0-{rid}-from-v0.4.0{extension}";
        gitHub.Add(package, Package(package, V050));
        gitHub.Add(delta, Delta(delta, "v0.4.0", V040, V050));
        return (package, delta);
    }

    /// <summary>The program's own files, leaving out what the update leaves for the next start to delete.</summary>
    private Dictionary<string, string> ProgramFiles() => Snapshot()
        .Where(entry => entry.Value != "/" && !entry.Key.EndsWith(".old") && !entry.Key.StartsWith(".update/")).ToDictionary();

    private async Task<bool> UpdateAsync(AutoUpdater updater, Action<UpdateProgress>? report = null) =>
        await updater.CheckAsync(TestContext.Current.CancellationToken) is { } update &&
        await updater.UpdateAsync(update, new Reports(report ?? (_ => { })), TestContext.Current.CancellationToken);

    /// <summary>Unlike Progress&lt;T&gt;, reports each step before the updater moves on, in order.</summary>
    private sealed class Reports(Action<UpdateProgress> report) : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value) { lock (this) report(value); }
    }

    /// <summary>Every file and directory in the program directory, with each file's text.</summary>
    private Dictionary<string, string> Snapshot() =>
        Directory.EnumerateFileSystemEntries(programDirectory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(programDirectory, path).Replace('\\', '/'), path => File.Exists(path) ? File.ReadAllText(path) : "/");

    private AutoUpdater Updater(string version = "0.4.0", string? rid = "osx-arm64", bool selfContained = true) =>
        new(new HttpClient(gitHub), new Uri(LatestRelease), programDirectory, version, rid, selfContained, clock, NullLogger.Instance);

    private void Write(IReadOnlyDictionary<string, string> files)
    {
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(programDirectory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
    }
}
