using Aether.Gui.Tests.Support;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using static Aether.Gui.Tests.Support.GuiHarness;

namespace Aether.Gui.Tests;

public sealed class LoginPageTests
{
    [Fact]
    public Task Account_reported_logged_out_while_reconnecting_does_not_start_another_room_connection() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var account = new TaskCompletionSource<HttpResponseMessage>();
        harness.NavResponse = token => account.Task.WaitAsync(token);
        harness.LoginResponse = (_, token) => new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);
        var requested = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var roomRequests = 0;
        harness.RoomResponse = async (_, token) =>
        {
            if (Interlocked.Increment(ref roomRequests) == 1)
            {
                requested.SetResult();
                await release.Task;
            }
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var reconnect = viewModel.ReconnectCommand.ExecuteAsync(null);
            account.SetResult(GuiHarness.Json("""{"code":-101}"""));
            await WaitForAsync(() => viewModel.AccountStatus == AccountStatus.LoggedOut);
            release.SetResult();
            await Task.WhenAll(reconnect, viewModel.ReadAccountCommand.ExecutionTask!).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("扫码登录 B站", VisibleText(window));
            Assert.Equal(1, roomRequests);
            Assert.False(viewModel.HasRoomConnection);
        }
        finally { release.TrySetResult(); await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public Task Watch_login_keeps_sidebar_covers_selected_page_and_disconnect_ends_connection(int pageIndex) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.NavResponse = _ => throw new HttpRequestException("offline");
        harness.RoomResponse = (_, _) => Task.FromResult(GuiHarness.Json("""{"code":0,"data":{"room_id":6}}"""));
        harness.LoginResponse = (uri, _) => Task.FromResult(LoginResponse(uri));
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            Assert.Contains("账号信息读取失败", VisibleText(window));
            var navigation = window.FindControl<ListBox>("Navigation")!;
            navigation.SelectedIndex = pageIndex;
            harness.NavResponse = _ => Task.FromResult(GuiHarness.Json("""{"code":-101}"""));
            viewModel.RoomNumber = "6";
            viewModel.ConnectCommand.Execute(null);
            await WaitForAsync(() => viewModel.HasQrCode);
            Assert.Contains("登录已失效，请重新扫码", VisibleText(window));
            Assert.Contains("等待扫码", VisibleText(window));
            Assert.True(navigation.IsEffectivelyVisible);
            Assert.False(window.FindControl<Panel>("PageHost")!.IsEffectivelyVisible);
            navigation.SelectedIndex = 3;
            Assert.Contains("登录已失效，请重新扫码", VisibleText(window));
            var disconnect = Assert.Single(window.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "断开") && x.IsEffectivelyVisible);
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(disconnect)).Invoke();
            await viewModel.ConnectCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(viewModel.HasRoomConnection);
            Assert.False(viewModel.HasQrCode);
            Assert.True(window.FindControl<Panel>("PageHost")!.IsEffectivelyVisible);
            Assert.Equal(3, navigation.SelectedIndex);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Login_failure_shows_reason_and_retry_requests_a_new_code() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.LoginResponse = (_, _) => Task.FromResult(GuiHarness.Json("""{"code":-352,"message":"扫码被拒绝"}"""));
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitForAsync(() => VisibleText(window).Any(text => text?.Contains("扫码被拒绝") == true));
            Assert.Null(window.FindControl<Image>("LoginQrImage")!.Source);
            var retry = window.FindControl<Button>("RetryLoginButton")!;
            Assert.True(retry.IsEffectivelyVisible);
            harness.LoginResponse = (uri, _) => Task.FromResult(LoginResponse(uri));
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(retry)).Invoke();
            await WaitForAsync(() => window.FindControl<Image>("LoginQrImage")!.Source is not null);
            Assert.DoesNotContain(VisibleText(window), text => text?.Contains("扫码被拒绝") == true);
            Assert.False(window.FindControl<ListBox>("Navigation")!.IsEffectivelyVisible);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Closing_during_login_cancels_and_waits_before_closing_window() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        harness.LoginResponse = async (_, token) =>
        {
            started.TrySetResult();
            using var registration = token.Register(() => cancelled.TrySetResult());
            await release.Task;
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException();
        };
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(window.IsVisible);
            Assert.False(window.IsEnabled);
            release.SetResult();
            await WaitForAsync(() => !window.IsVisible);
            Assert.Empty(harness.RejectedRequests);
        }
        finally { release.TrySetResult(); await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Saved_credential_reported_as_logged_out_switches_to_login_page() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var response = new TaskCompletionSource<HttpResponseMessage>();
        harness.NavResponse = token => response.Task.WaitAsync(token);
        harness.LoginResponse = (uri, _) => Task.FromResult(LoginResponse(uri));
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            Assert.Contains("读取中", VisibleText(window));
            Assert.True(window.FindControl<ListBox>("Navigation")!.IsEffectivelyVisible);
            response.SetResult(GuiHarness.Json("""{"code":-101}"""));
            await WaitForAsync(() => window.FindControl<Image>("LoginQrImage")?.Source is not null);
            Assert.Contains("扫码登录 B站", VisibleText(window));
            Assert.False(window.FindControl<ListBox>("Navigation")!.IsEffectivelyVisible);
            Assert.Equal(1, harness.NavRequests);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Login_displays_and_replaces_qr_then_reads_account_and_enters_main_window(bool dark) => HeadlessApp.Session.Dispatch(async () =>
    {
        var time = new FakeTimeProvider();
        using var harness = new GuiHarness(time);
        var generated = 0;
        harness.LoginResponse = (uri, _) => Task.FromResult(uri.AbsolutePath switch
        {
            "/x/passport-login/web/qrcode/generate" => GuiHarness.Json($$$"""{"code":0,"data":{"url":"https://example.test/{{{++generated}}}","qrcode_key":"key"}}"""),
            "/x/passport-login/web/qrcode/poll" => generated == 1
                ? GuiHarness.Json("""{"code":0,"data":{"code":86038}}""") : LoggedInResponse(),
            _ => LoginResponse(uri)
        });
        harness.NavResponse = _ => Task.FromResult(GuiHarness.AccountResponse("扫码账号"));
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory)
        {
            DataContext = viewModel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            await WaitForAsync(() => window.FindControl<Image>("LoginQrImage")?.Source is not null);
            var qr = window.FindControl<Image>("LoginQrImage")!;
            Assert.True(qr.IsEffectivelyVisible);
            var white = qr.GetVisualAncestors().OfType<Border>().First();
            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(white.Background).Color);
            var original = qr.Source;
            time.Advance(TimeSpan.FromSeconds(2));
            await WaitForAsync(() => generated == 2 && !ReferenceEquals(original, qr.Source));
            Assert.NotNull(qr.Source);
            Assert.Equal(0, harness.NavRequests);
            time.Advance(TimeSpan.FromSeconds(2));
            await WaitForAsync(() => VisibleText(window).Contains("扫码账号"));
            Assert.False(qr.IsEffectivelyVisible);
            Assert.True(window.FindControl<ListBox>("Navigation")!.IsEffectivelyVisible);
            Assert.Equal(1, harness.NavRequests);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Startup_without_credential_shows_login_without_requesting_account() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.LoginResponse = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            Assert.Contains("扫码登录 B站", VisibleText(window));
            Assert.False(window.FindControl<ListBox>("Navigation")!.IsEffectivelyVisible);
            Assert.False(viewModel.ConnectCommand.CanExecute(null));
            Assert.Equal(0, harness.NavRequests);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static HttpResponseMessage LoginResponse(Uri uri) => uri.AbsolutePath switch
    {
        "/x/frontend/finger/spi" => GuiHarness.Json("""{"code":0,"data":{"b_3":"test-buvid"}}"""),
        "/x/passport-login/web/qrcode/generate" => GuiHarness.Json("""{"code":0,"data":{"url":"https://example.test/scan","qrcode_key":"key"}}"""),
        "/x/passport-login/web/qrcode/poll" => GuiHarness.Json("""{"code":0,"data":{"code":86101}}"""),
        _ => throw new InvalidOperationException($"Unexpected login request: {uri}")
    };

    private static HttpResponseMessage LoggedInResponse()
    {
        var response = GuiHarness.Json("""{"code":0,"data":{"code":0,"refresh_token":"test-token"}}""");
        response.Headers.TryAddWithoutValidation("Set-Cookie", new[]
        {
            "SESSDATA=test-session; Path=/; Domain=.bilibili.com",
            "bili_jct=test-csrf; Path=/; Domain=.bilibili.com",
            "DedeUserID=123; Path=/; Domain=.bilibili.com"
        });
        return response;
    }
}
