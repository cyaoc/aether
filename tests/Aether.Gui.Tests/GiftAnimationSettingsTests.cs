using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class GiftAnimationSettingsTests
{
    [Fact]
    public Task Invalid_thresholds_disable_save_and_valid_settings_apply_without_reconnect() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var page = new GiftAnimationSettingsPage(h.DataDirectory) { DataContext = vm };
        var window = new Window { Content = page, Width = 1200, Height = 720 };
        window.Show();
        try
        {
            var inputs = page.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            inputs[1].Text = "50";
            GuiHarness.Flush();
            Assert.False(page.CanSave);
            Assert.True(page.FindControl<TextBlock>("ThresholdError")!.IsVisible);
            inputs[1].Text = "600";
            GuiHarness.Flush();
            Assert.True(page.CanSave);
            vm.ConnectionState = RoomConnectionState.Connected;
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.PendingSave;
            Assert.Equal(600, Settings.Load(h.DataDirectory).GiftAnimationThresholds[1].Value);
            Assert.False(page.ShowReconnectNotice);
            Assert.Equal("保存后，下一次送礼生效", page.TimingHint);
            vm.WebService.Apply(new WebServiceStatus(true, 17890, 1, 0));
            Assert.True(GuiHarness.FindVisual<Button>(page, "PlayButton").IsEnabled);
        }
        finally { await vm.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
    }, TestContext.Current.CancellationToken);
}
