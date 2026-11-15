using Aether.Core;
using Aether.Core.Tests.Support;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using static Aether.Gui.Tests.Support.DesktopPetForm;

namespace Aether.Gui.Tests;

// The 桌宠 page as canvas DP1 draws it (preview/gui-*.webp in the design package).
public sealed partial class VisualReviewTests
{
    [Theory]
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    [InlineData(1040, 600, false)]
    [InlineData(1040, 600, true)]
    public Task Screenshots_desktop_pet(int width, int height, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            harness.SaveSettings(("desktop_pet.enabled", "true"));
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory)
            {
                DataContext = viewModel, Width = width, Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await GuiHarness.AccountReadAsync(viewModel);
                await GuiHarness.WaitForAsync(() => viewModel.WebService.WebServiceStatus.Running);
                viewModel.Apply(new Connected(1768506153, 24680));
                window.Navigation.SelectedIndex = Array.IndexOf(ReviewedPages, "desktop-pet");
                var page = window.GetVisualDescendants().OfType<DesktopPetSettingsPage>().Single();
                // The design's picks: the 礼物 group, the 大礼物's lines, a 试演 of 大礼物.
                Pick(page, "ShowGroupList", "礼物");
                PickTest(page, "大礼物");
                Capture("waiting");
                using var pet = await DesktopPetEvents.OpenAsync(harness.Client, TestContext.Current.CancellationToken);
                await GuiHarness.WaitForAsync(() => viewModel.WebService.DesktopPet.Connected);
                Capture("shows");
                GuiHarness.Tab(page, 1);
                Capture("moods");
                GuiHarness.Tab(page, 3);
                Pick(page, "LineKindList", "大礼物");
                Capture("lines");
                // The design's list scrolled down to its lines.
                var kinds = page.FindControl<ListBox>("LineKindList")!;
                Pick(page, "LineKindList", "招人");
                kinds.ScrollIntoView(kinds.Items.Count - 1);
                Capture("game-lines");
                GuiHarness.Tab(page, 2);
                harness.Client.WebService.SetDesktopPetRoom(true);
                await GuiHarness.WaitForAsync(() => page.FindControl<Button>("SummonButton")!.IsEnabled);
                Capture("game");
                var keyword = Input(page, "帮它的关键字");
                keyword.Text = "抽奖";
                Capture("game-keyword");
                keyword.Text = "捣蛋";
                GuiHarness.Click(page, "SummonButton");
                await GuiHarness.WaitForAsync(() => page.FindControl<TextBlock>("SummonText")!.Text == "它在路上…");
                Capture("game-called");
                GuiHarness.Tab(page, 0);
                GuiHarness.Click(page, "TestButton");
                await pet.NextAsync(DesktopPet.Test);
                Capture("testing");
                GuiHarness.Click(page, "TestButton");
                var enabled = page.FindControl<ToggleSwitch>("EnabledInput")!;
                enabled.IsChecked = false;
                await GuiHarness.SaveAsync(page);
                await SettleAsync(enabled);
                Capture("off");
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;

            void Capture(string scene) => Save(window, $"desktop-pet-{scene}-{width}x{height}-{(dark ? "dark" : "light")}");
        }, TestContext.Current.CancellationToken);
    }
}
