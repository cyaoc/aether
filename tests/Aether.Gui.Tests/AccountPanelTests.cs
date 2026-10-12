using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class AccountPanelTests
{
    [Fact]
    public Task Failed_account_read_can_be_retried_and_logout_requires_confirmation() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.NavResponse = _ => throw new HttpRequestException("offline");
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitForAsync(() => VisibleText(window).Contains("账号信息读取失败"));
            Assert.True(viewModel.ConnectCommand.CanExecute(null));
            harness.NavResponse = _ => Task.FromResult(GuiHarness.AccountResponse());
            var retry = window.FindControl<Button>("RetryAccountButton")!;
            Assert.True(retry.IsEffectivelyVisible);
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(retry)).Invoke();
            await WaitForAsync(() => VisibleText(window).Contains("测试账号"));
            Assert.Equal(2, harness.NavRequests);

            window.FindControl<Button>("AccountMenuButton")!.Flyout!.ShowAt(window.FindControl<Button>("AccountMenuButton")!);
            Dispatcher.UIThread.RunJobs();
            var menu = Assert.IsType<MenuFlyout>(window.FindControl<Button>("AccountMenuButton")!.Flyout);
            Assert.Single(menu.Items.OfType<MenuItem>()).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), x =>
                x.Text == "退出后需要重新扫码登录；正在连接的直播间会断开。");
            Assert.Empty(harness.RejectedRequests);
            Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "取消"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(harness.RejectedRequests);
            Assert.Contains("测试账号", VisibleText(window));
            await viewModel.ReadAccountCommand.ExecuteAsync(null);
            Assert.Equal("测试账号", viewModel.AccountStatus.Text);

            menu.ShowAt(window.FindControl<Button>("AccountMenuButton")!);
            Assert.Single(menu.Items.OfType<MenuItem>()).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            dialog = Assert.Single(window.OwnedWindows);
            Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "退出登录"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => VisibleText(window).Contains("未登录"));
            Assert.Equal("https://passport.bilibili.com/login/exit/v2", Assert.Single(harness.RejectedRequests).AbsoluteUri);
            harness.RejectedRequests.Clear(); // The real logout attempted HTTP; the fake still rejected it and Core removed the local credential.
            await viewModel.ReadAccountCommand.ExecuteAsync(null);
            Assert.Null(viewModel.AccountStatus.Account);
            Assert.Equal("未登录", viewModel.AccountStatus.Text);
            Assert.Equal(3, harness.NavRequests);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Successful_qr_login_rereads_account_in_initial_connection_and_reconnect(bool reconnecting) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            Assert.Equal("未登录", viewModel.AccountStatus.Text);
            if (reconnecting)
            {
                viewModel.Apply(new Connected());
                viewModel.Apply(new Reconnecting());
            }
            await harness.SaveCredentialAsync();
            harness.NavResponse = _ => Task.FromResult(GuiHarness.AccountResponse("扫码后的账号"));
            viewModel.Apply(new WatchQrCode("https://example.test/login"));
            Assert.True(viewModel.HasQrCode);
            viewModel.Apply(reconnecting ? new Connected() : new Connecting());
            Assert.False(viewModel.HasQrCode);
            await WaitForAsync(() => VisibleText(window).Contains("扫码后的账号"));
            viewModel.Apply(new Connected());
            viewModel.Apply(new Reconnecting());
            viewModel.Apply(new Connected());
            Assert.Equal(1, harness.NavRequests);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Logout_cancels_pending_account_read_and_does_not_restore_stale_account() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.NavResponse = _ => response.Task;
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitForAsync(() => harness.NavRequests == 1);
            var logout = viewModel.LogoutCommand.ExecuteAsync(null);
            Assert.False(viewModel.ConnectCommand.CanExecute(null));
            response.SetResult(GuiHarness.AccountResponse("过期结果"));
            await logout;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("未登录", VisibleText(window));
            Assert.Null(viewModel.AccountStatus.Account);
            Assert.Equal("https://passport.bilibili.com/login/exit/v2", Assert.Single(harness.RejectedRequests).AbsoluteUri);
            harness.RejectedRequests.Clear();
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Unfinished_logout_rereads_account_instead_of_staying_on_loading() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.NavResponse = token => response.Task.WaitAsync(token);
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitForAsync(() => harness.NavRequests == 1);
            harness.NavResponse = _ => Task.FromResult(GuiHarness.AccountResponse());
            var logout = viewModel.LogoutCommand.ExecuteAsync(null);
            viewModel.LogoutCommand.Cancel(); // Stops logout after it cancelled the read, before Core deletes anything.
            await logout;
            await WaitForAsync(() => VisibleText(window).Contains("测试账号"));
            Assert.Equal(2, harness.NavRequests);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Startup_reads_account_in_background_and_displays_nickname() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.NavResponse = token => response.Task.WaitAsync(token);
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            Assert.True(window.IsVisible);
            Assert.Contains("读取中", VisibleText(window));
            Assert.True(viewModel.ConnectCommand.CanExecute(null));
            response.SetResult(GuiHarness.AccountResponse());
            await WaitForAsync(() => VisibleText(window).Contains("测试账号"));
            Assert.Equal(1, harness.NavRequests);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static IEnumerable<string?> VisibleText(MainWindow window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => x.Text);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
