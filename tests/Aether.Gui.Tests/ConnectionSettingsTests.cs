using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            var path = Path.Combine(harness.DataDirectory, "aether.yml");
            File.WriteAllText(path, yaml);
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddAetherFileLogging(harness.DataDirectory, Settings.Load(harness.DataDirectory));
            using var host = builder.Build();
            host.Services.GetRequiredService<ILogger<ConnectionSettingsTests>>().LogInformation("GUI startup");
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
