using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class GiftBoardSettingsTests
{
    [Fact]
    public Task The_navigation_lists_the_gift_board_after_the_gift_animation_and_shows_its_saved_switch() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        await h.SaveCredentialAsync();
        var vm = h.CreateViewModel();
        var window = new MainWindow(h.DataDirectory) { DataContext = vm };
        window.Show();
        try
        {
            var status = window.FindControl<TextBlock>("GiftBoardStatus")!;
            var gift = (ListBoxItem)window.Navigation.Items[3]!;
            Assert.Equal("送礼动画", ((Grid)gift.Content!).Children.OfType<TextBlock>().First().Text);
            Assert.Equal("礼物榜", ((Grid)((ListBoxItem)window.Navigation.Items[4]!).Content!).Children.OfType<TextBlock>().First().Text);
            Assert.Equal("关", status.Text);

            window.Navigation.SelectedIndex = 4;
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            Assert.IsType<GiftBoardSettingsPage>(page);
            page.FindControl<ToggleSwitch>("EnabledInput")!.IsChecked = true;
            GuiHarness.Flush();
            Assert.Equal("关", status.Text);
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.PendingSave;
            Assert.Equal("开", status.Text);
        }
        finally { await vm.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
