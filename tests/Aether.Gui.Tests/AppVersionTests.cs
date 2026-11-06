using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static Aether.Gui.Tests.Support.GuiHarness;

namespace Aether.Gui.Tests;

public sealed class AppVersionTests
{
    [Fact]
    public void App_version_is_the_assembly_version_without_source_revision()
    {
        Assert.Matches(@"^v\d+\.\d+\.\d+", MainWindowViewModel.AppVersion);
        Assert.DoesNotContain('+', MainWindowViewModel.AppVersion);
    }

    [Fact]
    public Task Login_page_shows_the_version() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        harness.HoldLoginRequests();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await WaitForAsync(() => viewModel.IsLoggedOut);
            Assert.Single(VersionTexts(window));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Main_window_shows_the_version_on_every_page() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await GuiHarness.AccountReadAsync(viewModel);
            Assert.False(viewModel.IsLoggedOut);
            for (var page = 0; page < window.Navigation.ItemCount; page++)
            {
                window.Navigation.SelectedIndex = page;
                Dispatcher.UIThread.RunJobs();
                Assert.Single(VersionTexts(window));
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public void Gui_opened_while_an_update_installs_quits_before_showing_anything()
    {
        // Program.Main looks in the program directory, which for tests is their own output directory.
        var update = Path.Combine(AppContext.BaseDirectory, ".update");
        Directory.CreateDirectory(update);
        try
        {
            using (new FileStream(Path.Combine(update, "install.lock"), FileMode.Create, FileAccess.Write, FileShare.None))
                Assert.Equal(0, Program.Main([]));
        }
        finally { Directory.Delete(update, recursive: true); }
    }

    private static IEnumerable<TextBlock> VersionTexts(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible && x.Text == MainWindowViewModel.AppVersion);
}
