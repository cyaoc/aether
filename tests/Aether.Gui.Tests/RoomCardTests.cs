using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Gui.Tests;

public sealed class RoomCardTests
{
    [Fact]
    public Task Card_shows_room_and_elapsed_minutes_without_resetting_on_reconnect() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var time = new FakeTimeProvider();
        var viewModel = harness.CreateViewModel(time);
        viewModel.RoomNumber = "7734200";
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            Assert.True(window.FindControl<StackPanel>("RoomInputPanel")!.IsVisible);
            Assert.False(window.FindControl<StackPanel>("RoomStatusPanel")!.IsVisible);
            viewModel.Apply(new Connecting());
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.FindControl<StackPanel>("RoomInputPanel")!.IsVisible);
            Assert.Contains("7734200", CardText());
            Assert.Contains("连接中", CardText());
            time.Advance(TimeSpan.FromMinutes(3));
            viewModel.Apply(new Connected(7734200, 24680));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("已连接 · 0 分钟", CardText());
            time.Advance(TimeSpan.FromSeconds(59));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("已连接 · 0 分钟", CardText());
            time.Advance(TimeSpan.FromSeconds(1));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("已连接 · 1 分钟", CardText());
            viewModel.Apply(new Reconnecting());
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("重连中", CardText());
            time.Advance(TimeSpan.FromMinutes(2));
            viewModel.Apply(new Connected(7734200, 24680));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("已连接 · 3 分钟", CardText());
            viewModel.ConnectionState = RoomConnectionState.Disconnected;
            viewModel.Apply(new Connected(7734200, 24680));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("已连接 · 0 分钟", CardText());
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;

        IEnumerable<string?> CardText() => window.FindControl<StackPanel>("RoomStatusPanel")!
            .GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => x.Text);
    }, TestContext.Current.CancellationToken);
}
