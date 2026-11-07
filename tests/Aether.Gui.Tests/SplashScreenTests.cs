using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Microsoft.Extensions.Time.Testing;
using static Aether.Gui.Tests.Support.GuiHarness;

namespace Aether.Gui.Tests;

public sealed class SplashScreenTests
{
    [Fact]
    public Task Fast_account_read_keeps_splash_for_600ms_then_fades_for_150ms() => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var splash = window.FindControl<Panel>("SplashScreen");
            Assert.NotNull(splash);
            var transition = Assert.IsType<DoubleTransition>(Assert.Single(splash.Transitions!));
            Assert.Equal(TimeSpan.FromMilliseconds(150), transition.Duration);
            Assert.True(splash.IsEffectivelyVisible);
            await GuiHarness.AccountReadAsync(viewModel);
            Assert.Equal("测试账号", viewModel.AccountStatus.Text);
            clock.Advance(TimeSpan.FromMilliseconds(599));
            Flush();
            Assert.True(splash.IsEffectivelyVisible);
            Assert.Equal(1, splash.Opacity);
            await BeginFadeAsync(clock, viewModel, milliseconds: 1);
            await WaitForAsync(() => { Flush(); return splash.Opacity is > 0 and < 1; });
            Assert.True(splash.IsEffectivelyVisible);
            clock.Advance(TimeSpan.FromMilliseconds(149));
            Flush();
            Assert.True(splash.IsEffectivelyVisible);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WaitForAsync(() => !splash.IsEffectivelyVisible);
            Assert.Contains("测试账号", VisibleText(window));
            Assert.False(window.LoginPage.IsEffectivelyVisible);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("no-credential")]
    [InlineData("logged-in")]
    [InlineData("logged-out")]
    [InlineData("offline")]
    public Task Startup_reveals_only_the_resolved_page(string outcome) => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        if (outcome != "no-credential") await harness.SaveCredentialAsync();
        var response = new TaskCompletionSource<HttpResponseMessage>();
        harness.NavResponse = token => response.Task.WaitAsync(token);
        harness.HoldLoginRequests();
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            Assert.True(viewModel.IsSplashVisible);
            if (outcome != "no-credential")
            {
                await WaitForAsync(() => harness.NavRequests == 1);
                clock.Advance(TimeSpan.FromSeconds(1));
                Flush();
                Assert.Equal(1, viewModel.SplashOpacity);
                Assert.True(viewModel.IsSplashVisible);
            }
            else clock.Advance(TimeSpan.FromSeconds(1));
            if (outcome == "offline") response.SetException(new HttpRequestException("offline"));
            else response.SetResult(outcome == "logged-out" ? Json("""{"code":-101}""") : AccountResponse());
            await WaitForAsync(() => viewModel.SplashOpacity == 0);
            var loggedOut = outcome is "no-credential" or "logged-out";
            Assert.Equal(loggedOut, window.LoginPage.IsEffectivelyVisible);
            Assert.Equal(!loggedOut, window.MainPage.IsEffectivelyVisible);
            if (outcome == "logged-in") Assert.Contains("测试账号", VisibleText(window));
            if (outcome == "offline")
            {
                Assert.Contains("账号信息读取失败", VisibleText(window));
                Assert.True(window.FindControl<Button>("RetryAccountButton")!.IsEffectivelyVisible);
                Assert.True(viewModel.ConnectCommand.CanExecute(null));
            }
            Assert.True(viewModel.IsSplashVisible);
            await FinishFadeAsync(clock, viewModel);
            Assert.Equal(loggedOut, viewModel.IsLoggedOut);
            Assert.Equal(outcome == "no-credential" ? 0 : 1, harness.NavRequests);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Timeout_cancels_account_read_and_allows_retry() => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var response = new TaskCompletionSource<HttpResponseMessage>();
        CancellationToken readToken = default;
        harness.NavResponse = token => { readToken = token; return response.Task.WaitAsync(token); };
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var startup = viewModel.StartAsync(harness.DataDirectory);
        try
        {
            await WaitForAsync(() => harness.NavRequests == 1);
            clock.Advance(TimeSpan.FromMilliseconds(2999));
            Flush();
            Assert.Equal(AccountStatus.Reading, viewModel.AccountStatus);
            Assert.Equal(1, viewModel.SplashOpacity);
            await BeginFadeAsync(clock, viewModel, milliseconds: 1);
            Assert.True(readToken.IsCancellationRequested);
            await GuiHarness.AccountReadAsync(viewModel);
            Assert.Equal(AccountStatus.Failed, viewModel.AccountStatus);
            await FinishFadeAsync(clock, viewModel);
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(viewModel.ConnectCommand.CanExecute(null));
            harness.NavResponse = _ => Task.FromResult(AccountResponse("重试账号"));
            await viewModel.ReadAccountCommand.ExecuteAsync(null);
            Assert.Equal("重试账号", viewModel.AccountStatus.Text);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Timeout_does_not_wait_for_a_stuck_read_and_discards_its_late_result(bool lateLoggedOut) => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var stuck = new TaskCompletionSource<HttpResponseMessage>();
        harness.NavResponse = _ => stuck.Task;
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var startup = viewModel.StartAsync(harness.DataDirectory);
        try
        {
            await WaitForAsync(() => harness.NavRequests == 1);
            await BeginFadeAsync(clock, viewModel, milliseconds: 3000);
            Assert.Equal(AccountStatus.Failed, viewModel.AccountStatus);
            await FinishFadeAsync(clock, viewModel);
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            stuck.SetResult(lateLoggedOut ? Json("""{"code":-101}""") : AccountResponse("迟到账号"));
            await GuiHarness.AccountReadAsync(viewModel);
            Flush();
            Assert.Equal(AccountStatus.Failed, viewModel.AccountStatus);
            Assert.False(viewModel.LoginCommand.IsRunning);
        }
        finally { stuck.TrySetCanceled(); await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Splash_blocks_mouse_and_keyboard_until_fade_finishes(bool loggedOut) => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        if (!loggedOut) await harness.SaveCredentialAsync();
        harness.LoginResponse = (_, _) => Task.FromResult(Json("""{"code":-352,"message":"test"}"""));
        harness.SaveRecentRooms("6");
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await GuiHarness.AccountReadAsync(viewModel);
            if (loggedOut) await viewModel.LoginCommand.ExecutionTask!;
            Flush();
            Control target = loggedOut ? window.FindControl<Button>("RetryLoginButton")! : window.RoomInput.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.True(target.IsEffectivelyVisible);
            Assert.False(target.IsEffectivelyEnabled);
            Assert.False(target.Focus());
            AssertInputStopsAtSplash();
            await BeginFadeAsync(clock, viewModel);
            Assert.True(target.IsEffectivelyEnabled);
            AssertInputStopsAtSplash();
            await FinishFadeAsync(clock, viewModel);
            Assert.True(target.Focus());

            void AssertInputStopsAtSplash()
            {
                var point = target.TranslatePoint(new Point(5, 5), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                window.KeyTextInput("123");
                Flush();
                Assert.Equal("6", viewModel.RoomNumber);
                Assert.False(viewModel.HasRoomConnection);
                Assert.Null(window.FocusManager!.GetFocusedElement());
                Assert.Empty(harness.RejectedRequests);
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("reading")]
    [InlineData("minimum")]
    [InlineData("fade")]
    public Task Closing_during_startup_does_not_wait_for_splash_timers(string stage) => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        if (stage == "reading")
            harness.NavResponse = NoResponse;
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var window = new MainWindow(harness.DataDirectory);
        window.Show();
        window.DataContext = viewModel;
        // Started here, not by OnOpened which discards the task, so a throwing splash timer fails the test.
        var startup = viewModel.StartAsync(harness.DataDirectory);
        try
        {
            await WaitForAsync(() => harness.NavRequests == 1);
            if (stage != "reading") await GuiHarness.AccountReadAsync(viewModel);
            if (stage == "fade") await BeginFadeAsync(clock, viewModel);
            Assert.True(viewModel.IsSplashVisible);
            window.Close();
            await WaitForAsync(() => !window.IsVisible);
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(viewModel.ReadAccountCommand.ExecutionTask!.IsCompleted);
            Assert.False(viewModel.WebService.WebServiceStatus.Running);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Web_service_runs_during_splash_and_after_database_result(bool newerDatabase) => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["web_service.port"] = FreePort().ToString() });
        if (newerDatabase)
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(harness.DataDirectory, "aether.db")};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999";
            command.ExecuteNonQuery();
        }
        harness.HoldLoginRequests();
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        window.Show();
        try
        {
            await WaitForAsync(() => viewModel.WebService.WebServiceStatus.Running);
            Assert.True(viewModel.IsSplashVisible);
            await AssertPagesServedAsync();
            await GuiHarness.AccountReadAsync(viewModel);
            Assert.Equal(newerDatabase ? AccountStatus.Failed : AccountStatus.LoggedOut, viewModel.AccountStatus);
            await BeginFadeAsync(clock, viewModel);
            await FinishFadeAsync(clock, viewModel);
            Assert.Equal(newerDatabase, window.MainPage.IsEffectivelyVisible);
            Assert.Equal(!newerDatabase, window.LoginPage.IsEffectivelyVisible);
            Assert.Equal(newerDatabase, viewModel.ConnectCommand.CanExecute(null));
            Assert.True(viewModel.WebService.WebServiceStatus.Running);
            await AssertPagesServedAsync();

            async Task AssertPagesServedAsync()
            {
                Assert.Contains("<video", await http.GetStringAsync(viewModel.WebService.Video.Address));
                Assert.Contains("/lottery/page.js", await http.GetStringAsync(viewModel.WebService.Lottery.Address));
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Account_read_and_update_check_start_together_and_the_splash_decides_after_both() => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.SaveSettings();
        harness.HoldLoginRequests();
        var account = new TaskCompletionSource<HttpResponseMessage>();
        harness.NavResponse = token => account.Task.WaitAsync(token);
        var check = new TaskCompletionSource();
        harness.GitHub.BeforeRelease = () => check.Task;
        harness.GitHub.Tag = "v0.4.0";
        harness.ReleaseVersion = "0.4.0";
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var startup = viewModel.StartAsync(harness.DataDirectory);
        try
        {
            await WaitForAsync(() => harness.NavRequests == 1 && harness.GitHub.Requests.Count == 1);
            account.SetResult(Json("""{"code":-101}"""));
            await GuiHarness.AccountReadAsync(viewModel);
            clock.Advance(TimeSpan.FromMilliseconds(2999));
            Flush();
            Assert.True(viewModel.IsLoggedOut);
            AssertStillDeciding(viewModel);
            check.SetResult();
            await WaitForAsync(() => viewModel.SplashOpacity == 0);
            Assert.True(viewModel.LoginCommand.IsRunning);
            Assert.True(viewModel.WebService.WebServiceStatus.Running);
            await FinishFadeAsync(clock, viewModel);
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Update_holds_the_splash_without_login_or_web_service_until_skipped() => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        harness.SaveSettings();
        harness.HoldLoginRequests();
        HoldUpdateDownload(harness);
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitForAsync(() => viewModel.UpdateProgress?.Downloaded == FakeGitHub.HeldStream.FirstChunk);
            clock.Advance(TimeSpan.FromMinutes(10));
            Flush();
            Assert.True(viewModel.IsLoggedOut);
            AssertStillDeciding(viewModel);
            Assert.Contains("正在下载新版本 v0.5.0", VisibleText(window));
            var skip = window.FindControl<Button>("SkipUpdateButton")!;
            Assert.True(skip.IsEffectivelyVisible && skip.IsEffectivelyEnabled);
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(skip)).Invoke();
            await WaitForAsync(() => viewModel.SplashOpacity == 0);
            Assert.True(viewModel.LoginCommand.IsRunning);
            Assert.True(viewModel.WebService.WebServiceStatus.Running);
            Assert.False(Directory.Exists(Path.Combine(harness.ProgramDirectory, ".update")));
            // As with no update, the fading splash shows nothing over the title, and Tab finds nothing on it.
            await WaitForAsync(() => viewModel.UpdateProgress is null);
            Flush();
            Assert.False(skip.IsEffectivelyVisible);
            Assert.DoesNotContain("正在下载新版本 v0.5.0", VisibleText(window));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Tab_stays_on_the_skip_button_while_the_splash_shows() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        HoldUpdateDownload(harness);
        var viewModel = harness.CreateViewModel(new FakeTimeProvider(), keepSplashTiming: true);
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitForAsync(() => viewModel.UpdateProgress?.Downloaded == FakeGitHub.HeldStream.FirstChunk);
            Flush();
            var skip = window.FindControl<Button>("SkipUpdateButton")!;
            for (var i = 0; i < 3; i++)
            {
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                Flush();
                Assert.Same(skip, window.FocusManager!.GetFocusedElement());
            }
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
            Flush();
            Assert.Same(skip, window.FocusManager!.GetFocusedElement());
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Installed_update_keeps_the_splash_and_asks_for_a_restart() => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        harness.SaveSettings();
        harness.HoldLoginRequests();
        AddUpdate(harness);
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var restarts = 0;
        viewModel.UpdateInstalled += () => restarts++;
        try
        {
            await viewModel.StartAsync(harness.DataDirectory).WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(1));
            Flush();
            Assert.Equal(1, restarts);
            Assert.Equal("gui 0.5.0", File.ReadAllText(Path.Combine(harness.ProgramDirectory, "Aether.Gui")));
            Assert.Equal(new UpdateProgress("v0.5.0", false, 100), viewModel.UpdateProgress);
            AssertStillDeciding(viewModel);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Closing_during_the_download_deletes_it() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.HoldLoginRequests();
        HoldUpdateDownload(harness);
        var viewModel = harness.CreateViewModel(new FakeTimeProvider(), keepSplashTiming: true);
        var restarts = 0;
        viewModel.UpdateInstalled += () => restarts++;
        var startup = viewModel.StartAsync(harness.DataDirectory);
        await WaitForAsync(() => viewModel.UpdateProgress?.Downloaded == FakeGitHub.HeldStream.FirstChunk);
        await viewModel.ShutdownAsync();
        await startup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(Directory.Exists(Path.Combine(harness.ProgramDirectory, ".update")));
        Assert.False(viewModel.LoginCommand.IsRunning);
        Assert.Equal(0, restarts);
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Startup_cleans_up_the_last_update_before_reading_the_account_and_checking() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        string[] leftovers = ["Aether.Gui.old", ".update/v0.4.0/Aether.Gui"];
        foreach (var leftover in leftovers)
        {
            var path = Path.Combine(harness.ProgramDirectory, leftover);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
        // Whether the leftovers were gone when each request went out.
        var cleaned = new System.Collections.Concurrent.ConcurrentQueue<bool>();
        bool Cleaned() => !Directory.EnumerateFileSystemEntries(harness.ProgramDirectory).Any();
        harness.NavResponse = _ => { cleaned.Enqueue(Cleaned()); return Task.FromResult(AccountResponse()); };
        harness.GitHub.BeforeRelease = () => { cleaned.Enqueue(Cleaned()); return Task.CompletedTask; };
        harness.ReleaseVersion = "0.4.0";
        var viewModel = harness.CreateViewModel(new FakeTimeProvider(), keepSplashTiming: true);
        try
        {
            _ = viewModel.StartAsync(harness.DataDirectory);
            await WaitForAsync(() => cleaned.Count == 2);
            Assert.Equal([true, true], cleaned);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Auto_update_off_does_not_check() => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        harness.SaveSettings(("auto_update.enabled", "false"));
        harness.HoldLoginRequests();
        AddUpdate(harness);
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var startup = viewModel.StartAsync(harness.DataDirectory);
        try
        {
            await BeginFadeAsync(clock, viewModel);
            await FinishFadeAsync(clock, viewModel);
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(harness.GitHub.Requests);
            Assert.True(viewModel.LoginCommand.IsRunning);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task A_settings_file_whose_values_cannot_be_used_checks_for_updates() => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(Settings.FilePath(harness.DataDirectory), "auto_update:\n  enabled: false\nweb_service:\n  port: invalid\n");
        harness.HoldLoginRequests();
        harness.GitHub.Tag = "v0.4.0";
        harness.ReleaseVersion = "0.4.0";
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        var startup = viewModel.StartAsync(harness.DataDirectory);
        try
        {
            await BeginFadeAsync(clock, viewModel);
            await FinishFadeAsync(clock, viewModel);
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal([FakeGitHub.LatestRelease], harness.GitHub.Requests);
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Old_gui_yml_with_auto_update_off_is_moved_before_the_first_check_and_fills_the_room_input() =>
        HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        harness.SaveSettings();
        File.WriteAllText(harness.LegacyGuiFile, "AutoUpdate: false\nRecentRooms: ['006', '42']");
        harness.HoldLoginRequests();
        AddUpdate(harness);
        var viewModel = harness.CreateViewModel(clock, keepSplashTiming: true);
        Assert.Equal("", viewModel.RoomNumber);
        var startup = viewModel.StartAsync(harness.DataDirectory);
        try
        {
            await BeginFadeAsync(clock, viewModel);
            await FinishFadeAsync(clock, viewModel);
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(harness.GitHub.Requests);
            Assert.Equal("006", viewModel.RoomNumber);
            Assert.Equal(["006", "42"], viewModel.RecentRooms);
            Assert.False(File.Exists(harness.LegacyGuiFile));
        }
        finally { await viewModel.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static void AssertStillDeciding(MainWindowViewModel viewModel)
    {
        Assert.True(viewModel.IsSplashVisible);
        Assert.Equal(1, viewModel.SplashOpacity);
        Assert.False(viewModel.LoginCommand.IsRunning);
        Assert.False(viewModel.WebService.WebServiceStatus.Running);
    }

    /// <summary>Makes the window's program v0.4.0, with v0.5.0 on GitHub.</summary>
    private static void AddUpdate(GuiHarness harness)
    {
        const string package = "aether-gui-v0.5.0-osx-arm64.tar.gz";
        harness.ReleaseVersion = "0.4.0";
        harness.GitHub.Add(package, FakeGitHub.Package(package, new Dictionary<string, string> { ["Aether.Gui"] = "gui 0.5.0" }));
    }

    /// <summary>Like <see cref="AddUpdate"/>, with the download stopped for good after its first bytes.</summary>
    private static void HoldUpdateDownload(GuiHarness harness)
    {
        AddUpdate(harness);
        harness.GitHub.DownloadHeld = new TaskCompletionSource().Task;
    }

    private static async Task BeginFadeAsync(FakeTimeProvider clock, MainWindowViewModel viewModel, int milliseconds = 600)
    {
        clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        await WaitForAsync(() => viewModel.SplashOpacity == 0);
    }

    private static async Task FinishFadeAsync(FakeTimeProvider clock, MainWindowViewModel viewModel)
    {
        clock.Advance(TimeSpan.FromMilliseconds(150));
        await WaitForAsync(() => !viewModel.IsSplashVisible);
    }
}
