using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class ConnectionSettingsTests
{
    [Theory]
    [InlineData("log:\n  level: @bad", "第 2 行")]
    [InlineData("log:\n  retention_days: 0", "log.retention_days")]
    public Task Invalid_settings_allow_window_startup_but_connect_displays_core_error(string yaml, string expected) =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            Directory.CreateDirectory(harness.DataDirectory);
            var path = Aether.Core.Settings.FilePath(harness.DataDirectory);
            File.WriteAllText(path, yaml);
            using (App.CreateHost(harness.DataDirectory)) { }
            Assert.Contains("读取设置失败", File.ReadAllText(
                Assert.Single(Directory.GetFiles(Path.Combine(harness.DataDirectory, "logs"), "*.log"))));
            // The harness view model's client fails any HTTP request, so a missed refusal cannot reach B站.
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            try
            {
                Assert.True(window.IsVisible);
                Assert.Empty(viewModel.Message);
                viewModel.RoomNumber = "6";
                await viewModel.ConnectCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(RoomConnectionState.Disconnected, viewModel.ConnectionState);
                Assert.Contains(path, viewModel.Message);
                Assert.Contains(expected, viewModel.Message);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsVisible && text.Text == viewModel.Message);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }, TestContext.Current.CancellationToken);
}
