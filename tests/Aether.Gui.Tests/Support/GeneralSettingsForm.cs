using Aether.Core;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests.Support;

/// <summary>The general settings page on its own, and its page list, as the streamer sees them.</summary>
internal static class GeneralSettingsForm
{
    public static readonly string[] Pages = ["Video", "Lottery", "GiftBoard", "DesktopPet"];

    /// <summary>The general settings page in a window of this size, with the web service in this state.</summary>
    public static Task Open(int width, int height, WebServiceStatus status, Func<GeneralSettingsPage, GuiHarness, Task> check) =>
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
                await check(page, harness);
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;
        }, TestContext.Current.CancellationToken);

    /// <summary>The main window's general settings page.</summary>
    public static GeneralSettingsPage Of(MainWindow window) => window.GetVisualDescendants().OfType<GeneralSettingsPage>().Single();

    /// <summary>A page's item in the list; <paramref name="name"/> is one of <see cref="Pages"/>.</summary>
    public static ListBoxItem Item(GeneralSettingsPage page, string name) => page.FindControl<ListBoxItem>(name + "Item")!;

    /// <summary>A page's detail beside the list; <paramref name="name"/> is one of <see cref="Pages"/>.</summary>
    public static WebPageDetail Detail(GeneralSettingsPage page, string name) => page.FindControl<WebPageDetail>(name + "Detail")!;

    /// <summary>A control in a page's detail, found by name: a detail not picked was never shown, so its content is not in the visual tree yet.</summary>
    public static T DetailControl<T>(GeneralSettingsPage page, string name, string control) where T : Control => Detail(page, name).FindControl<T>(control)!;

    /// <summary>Picks a page in the list; <paramref name="name"/> is one of <see cref="Pages"/>.</summary>
    public static void Pick(GeneralSettingsPage page, string name)
    {
        page.FindControl<ListBox>("PageList")!.SelectedItem = Item(page, name);
        GuiHarness.Flush();
    }
}
