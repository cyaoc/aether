using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class SettingsReconnectTests
{
    [Theory]
    [InlineData(RoomConnectionState.Disconnected, 1, false)]
    [InlineData(RoomConnectionState.Connected, 6, false)]
    [InlineData(RoomConnectionState.Connected, 2, true)]
    [InlineData(RoomConnectionState.Connected, 1, true)]
    [InlineData(RoomConnectionState.Reconnecting, 5, true)]
    public Task Only_room_settings_saved_during_a_connection_show_a_notice(RoomConnectionState state, int index, bool notice) =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
            window.Show();
            try
            {
                viewModel.ConnectionState = state;
                window.FindControl<ListBox>("Navigation")!.SelectedIndex = index;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
                switch (index)
                {
                    case 1:
                        page.FindControl<TextBox>("DailyKeywordInput")!.Text = "查今天";
                        page.FindControl<TextBox>("MonthlyKeywordInput")!.Text = "查本月";
                        GuiHarness.Flush();
                        break;
                    case 2: page.FindControl<ToggleSwitch>("WelcomeInput")!.IsChecked = true; break;
                    case 5: page.FindControl<NumericUpDown>("SendIntervalInput")!.Text = "7"; break;
                    case 6: page.FindControl<NumericUpDown>("RetentionInput")!.Text = "7"; break;
                }
                page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(notice, page.FindControl<Border>("ReconnectNotice")!.IsVisible);
                viewModel.ConnectionState = RoomConnectionState.Disconnected;
                Assert.All(window.GetVisualDescendants().OfType<SettingsPage>(),
                    other => Assert.False(other.FindControl<Border>("ReconnectNotice")!.IsVisible));
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
            return true;
        }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(RoomConnectionState.Connecting)]
    [InlineData(RoomConnectionState.Connected)]
    [InlineData(RoomConnectionState.Reconnecting)]
    public Task Saving_then_reconnecting_waits_for_old_connection_and_keeps_room_and_danmaku(RoomConnectionState state) =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            var firstStarted = new TaskCompletionSource();
            var firstCancelled = new TaskCompletionSource();
            var releaseFirst = new TaskCompletionSource();
            var secondStarted = new TaskCompletionSource();
            var requests = new List<Uri>();
            harness.RoomResponse = async (uri, token) =>
            {
                requests.Add(uri);
                if (requests.Count == 1)
                {
                    firstStarted.SetResult();
                    using var registration = token.Register(() => firstCancelled.TrySetResult());
                    await releaseFirst.Task;
                    token.ThrowIfCancellationRequested();
                }
                secondStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("A canceled request must not finish.");
            };
            var viewModel = harness.CreateViewModel();
            viewModel.RoomNumber = "6";
            var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
            window.Show();
            try
            {
                viewModel.ConnectCommand.Execute(null);
                await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                viewModel.ConnectionState = state;
                harness.Receive(viewModel, 3);
                viewModel.Apply(new SendQueueUpdate(1, DateTimeOffset.Now, SendQueueItemKind.Welcome,
                    SendQueueItemState.Queued, 42, "观众") { Text = "欢迎" });
                window.FindControl<ListBox>("Navigation")!.SelectedIndex = 1;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
                page.FindControl<ToggleSwitch>("BlindBoxInput")!.IsChecked = false;
                page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(page.FindControl<Border>("ReconnectNotice")!.IsVisible);
                var reconnect = page.FindControl<Button>("ReconnectButton")!;
                Assert.True(reconnect.IsEnabled);
                Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(reconnect)).Invoke();
                await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(secondStarted.Task.IsCompleted);
                Assert.Single(requests);
                releaseFirst.TrySetResult();
                await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(requests[0], requests[1]);
                Assert.Equal("6", viewModel.RoomNumber);
                Assert.Equal(3, viewModel.Danmaku.Count);
                Assert.Empty(viewModel.PendingSends);
                Assert.Equal("连接结束，没发出", Assert.Single(viewModel.RecentWelcomes).Status);
                Assert.Equal(1, viewModel.WelcomeCount);
                viewModel.Apply(new SendQueueUpdate(2, DateTimeOffset.Now, SendQueueItemKind.Welcome,
                    SendQueueItemState.Queued, 42, "观众") { Text = "又见面了" });
                Assert.Equal(2, viewModel.WelcomeCount);
                Assert.Equal(2, viewModel.RecentWelcomes.Count);
                Assert.Equal("排队中 · 第 1 条", Assert.Single(viewModel.PendingSends).Status);
                Assert.False(page.FindControl<Border>("ReconnectNotice")!.IsVisible);
            }
            finally
            {
                releaseFirst.TrySetResult();
                await viewModel.ShutdownAsync();
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return true;
        }, TestContext.Current.CancellationToken);
}
