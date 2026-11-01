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
            var error = page.FindControl<TextBlock>("ThresholdError")!;
            inputs[1].Text = "50";
            GuiHarness.Flush();
            Assert.False(page.CanSave);
            Assert.Equal("每一档的门槛要比上一档高", error.Text);
            Assert.Equal("门槛要一档比一档高，改好后才能保存", page.DirtyText);
            Assert.True(inputs[1].Classes.Contains("invalid"));
            inputs[1].Text = "";
            GuiHarness.Flush();
            Assert.Equal("门槛要填正整数", error.Text);
            Assert.Equal("门槛要填正整数，改好后才能保存", page.DirtyText);
            inputs[1].Text = "600";
            GuiHarness.Flush();
            Assert.True(page.CanSave);
            Assert.False(error.IsVisible);
            Assert.False(inputs[1].Classes.Contains("invalid"));
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
