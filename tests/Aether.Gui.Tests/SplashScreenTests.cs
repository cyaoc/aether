using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Threading;
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
        var viewModel = harness.CreateViewModel(clock, splashTiming: true);
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var splash = window.FindControl<Panel>("SplashScreen");
            Assert.NotNull(splash);
            var transition = Assert.IsType<DoubleTransition>(Assert.Single(splash.Transitions!));
            Assert.Equal(TimeSpan.FromMilliseconds(150), transition.Duration);
            Assert.True(splash.IsEffectivelyVisible);
            await viewModel.ReadAccountCommand.ExecutionTask!;
            Assert.Equal("测试账号", viewModel.AccountStatus.Text);
            clock.Advance(TimeSpan.FromMilliseconds(599));
            Flush();
            Assert.True(splash.IsEffectivelyVisible);
            Assert.Equal(1, splash.Opacity);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WaitForAsync(() => viewModel.SplashOpacity == 0);
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
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
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
        harness.LoginResponse = (_, token) => new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);
        var viewModel = harness.CreateViewModel(clock, splashTiming: true);
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
            clock.Advance(TimeSpan.FromMilliseconds(150));
            await WaitForAsync(() => !viewModel.IsSplashVisible);
            Assert.Equal(loggedOut, viewModel.IsLoggedOut);
            Assert.Equal(outcome == "no-credential" ? 0 : 1, harness.NavRequests);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Timeout_cancels_account_read_discards_late_result_and_allows_retry(bool lateLoggedOut) => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var response = new TaskCompletionSource<HttpResponseMessage>();
        CancellationToken readToken = default;
        harness.NavResponse = token => { readToken = token; return response.Task.WaitAsync(token); };
        var viewModel = harness.CreateViewModel(clock, splashTiming: true);
        var startup = viewModel.StartAsync(harness.DataDirectory);
        try
        {
            await WaitForAsync(() => harness.NavRequests == 1);
            clock.Advance(TimeSpan.FromMilliseconds(2999));
            Flush();
            Assert.Equal(AccountStatus.Reading, viewModel.AccountStatus);
            Assert.Equal(1, viewModel.SplashOpacity);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WaitForAsync(() => viewModel.SplashOpacity == 0);
            Assert.True(readToken.IsCancellationRequested);
            Assert.True(viewModel.ReadAccountCommand.ExecutionTask!.IsCompleted);
            Assert.Equal(AccountStatus.Failed, viewModel.AccountStatus);
            clock.Advance(TimeSpan.FromMilliseconds(150));
            await startup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(viewModel.IsSplashVisible);
            Assert.True(viewModel.ConnectCommand.CanExecute(null));
            response.SetResult(lateLoggedOut ? Json("""{"code":-101}""") : AccountResponse("迟到账号"));
            Flush();
            Assert.Equal(AccountStatus.Failed, viewModel.AccountStatus);
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
    public Task Splash_blocks_mouse_and_keyboard_until_fade_finishes(bool loggedOut) => HeadlessApp.Session.Dispatch(async () =>
    {
        var clock = new FakeTimeProvider();
        using var harness = new GuiHarness();
        if (!loggedOut) await harness.SaveCredentialAsync();
        harness.LoginResponse = (_, _) => Task.FromResult(Json("""{"code":-352,"message":"test"}"""));
        var viewModel = harness.CreateViewModel(clock, splashTiming: true);
        viewModel.RoomNumber = "6";
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            if (loggedOut) await viewModel.LoginCommand.ExecutionTask!;
            Flush();
            Control target = loggedOut ? window.FindControl<Button>("RetryLoginButton")! : window.RoomInput.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.True(target.IsEffectivelyVisible);
            Assert.False(target.IsEffectivelyEnabled);
            Assert.False(target.Focus());
            var point = target.TranslatePoint(new Point(5, 5), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyTextInput("123");
            Flush();
            Assert.Equal("6", viewModel.RoomNumber);
            Assert.False(viewModel.HasRoomConnection);
            Assert.False(target.IsFocused);
            Assert.Empty(harness.RejectedRequests);
            clock.Advance(TimeSpan.FromMilliseconds(600));
            await WaitForAsync(() => viewModel.SplashOpacity == 0);
            Assert.False(target.IsEffectivelyEnabled);
            clock.Advance(TimeSpan.FromMilliseconds(150));
            await WaitForAsync(() => !viewModel.IsSplashVisible);
            Assert.True(target.IsEffectivelyEnabled);
            Assert.True(target.Focus());
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
            harness.NavResponse = token => new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);
        var viewModel = harness.CreateViewModel(clock, splashTiming: true);
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitForAsync(() => harness.NavRequests == 1);
            if (stage != "reading") await viewModel.ReadAccountCommand.ExecutionTask!;
            if (stage == "fade")
            {
                clock.Advance(TimeSpan.FromMilliseconds(600));
                await WaitForAsync(() => viewModel.SplashOpacity == 0);
            }
            Assert.True(viewModel.IsSplashVisible);
            window.Close();
            await WaitForAsync(() => !window.IsVisible);
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
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["web_service.port"] = port.ToString() });
        if (newerDatabase)
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(harness.DataDirectory, "aether.db")};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999";
            command.ExecuteNonQuery();
        }
        harness.LoginResponse = (_, token) => new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);
        var viewModel = harness.CreateViewModel(clock, splashTiming: true);
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        window.Show();
        try
        {
            await WaitForAsync(() => viewModel.WebService.WebServiceStatus.Running);
            Assert.True(viewModel.IsSplashVisible);
            Assert.Contains("<video", await http.GetStringAsync(viewModel.WebService.VideoAddressText));
            Assert.Contains("/lottery/page.js", await http.GetStringAsync(viewModel.WebService.LotteryAddressText));
            await viewModel.ReadAccountCommand.ExecutionTask!;
            Assert.Equal(newerDatabase ? AccountStatus.Failed : AccountStatus.LoggedOut, viewModel.AccountStatus);
            clock.Advance(TimeSpan.FromMilliseconds(600));
            await WaitForAsync(() => viewModel.SplashOpacity == 0);
            clock.Advance(TimeSpan.FromMilliseconds(150));
            await WaitForAsync(() => !viewModel.IsSplashVisible);
            Assert.Equal(newerDatabase, window.MainPage.IsEffectivelyVisible);
            Assert.True(viewModel.WebService.WebServiceStatus.Running);
            Assert.Contains("<video", await http.GetStringAsync(viewModel.WebService.VideoAddressText));
            Assert.Contains("/lottery/page.js", await http.GetStringAsync(viewModel.WebService.LotteryAddressText));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

}
