using Aether.Core;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests.Support;

/// <summary>The general settings page on its own, and its page list, as the streamer sees them.</summary>
internal static class GeneralSettingsForm
{
    public static readonly string[] Pages = ["Video", "Lottery", "GiftBoard", "DesktopPet"];

    /// <summary>The general settings page in a window of this size, with the web service in this state.</summary>
    public static Task Open(int width, int height, WebServiceStatus status, Action<GeneralSettingsPage, GuiHarness> check) =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            var viewModel = harness.CreateViewModel();
            var page = new GeneralSettingsPage(harness.DataDirectory) { DataContext = viewModel };
            var window = new Window { Content = page, Width = width, Height = height };
            window.Show();
            try
            {
                viewModel.WebService.Apply(status);
                GuiHarness.Flush();
                check(page, harness);
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;
        }, TestContext.Current.CancellationToken);

    /// <summary>Switches the main window's general settings to its 更新和日志 tab, where the update switch and the log settings are.</summary>
    public static void ShowUpdateAndLogs(MainWindow window) => GuiHarness.Tab(window.GetVisualDescendants().OfType<GeneralSettingsPage>().Single(), 1);

    /// <summary>Picks a page in the list; <paramref name="name"/> is one of <see cref="Pages"/>.</summary>
    public static void Pick(GeneralSettingsPage page, string name)
    {
        page.FindControl<ListBox>("PageList")!.SelectedItem = page.FindControl<ListBoxItem>(name + "Item");
        GuiHarness.Flush();
    }
}
