using System.Text.RegularExpressions;
using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class BrowserSourceSizeTests
{
    private static readonly Regex Size = new(@"\d{3,4}\s*[×x]\s*\d{3,4}");
    private static readonly Dictionary<string, string> Sizes = new()
    {
        ["Video"] = "1920 × 1080", ["Lottery"] = "横屏 1920 × 1080 · 竖屏 1080 × 1920", ["GiftBoard"] = "480 × 1000，可以等比放大", ["DesktopPet"] = "480 × 640",
    };

    private const string Steps = "每个页面在直播姬里各加一个浏览器素材，地址和宽高照右边填；加过了还没连着，右键素材 → 刷新";

    [Fact]
    public Task Each_pages_detail_writes_its_size_under_the_setup_steps_on_one_line() =>
        GeneralSettingsForm.Open(1040, 600, new WebServiceStatus(true, 17890, 1, 1, 0), (page, _) =>
        {
            var steps = page.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == Steps);
            Assert.False(Assert.Single(steps.TextLayout.TextLines).HasCollapsed);
            foreach (var (name, size) in Sizes)
                Assert.Equal(size, GeneralSettingsForm.DetailControl<TextBlock>(page, name, "PageSize").Text);
            return Task.CompletedTask;
        });

    [Fact]
    public Task The_setup_steps_are_written_once_whichever_page_nobody_is_connected_to() =>
        GeneralSettingsForm.Open(1040, 600, new WebServiceStatus(true, 17890, 0, 0, 0), (page, _) =>
        {
            foreach (var name in GeneralSettingsForm.Pages)
            {
                GeneralSettingsForm.Pick(page, name);
                Assert.DoesNotContain(GeneralSettingsForm.Detail(page, name).GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("推流软件") == true);
                Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == Steps);
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Status_bars_send_the_streamer_to_the_general_settings_for_the_address_and_size() => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        // The 桌宠 page shows its status bar only while she is on.
        harness.SaveSettings(("desktop_pet.enabled", "true"));
        var window = new Window { Width = 1040, Height = 600 };
        window.Show();
        try
        {
            foreach (Control page in new Control[]
            {
                new AnimationSettingsPage(harness.DataDirectory), new GiftAnimationSettingsPage(harness.DataDirectory), new GiftBoardPanel(),
                new DesktopPetSettingsPage(harness.DataDirectory)
            })
            {
                window.Content = page;
                GuiHarness.Flush();
                Assert.Equal("地址和宽高在通用设置", GuiHarness.FindVisual<Button>(page, "OpenGeneralSettingsButton").GetVisualDescendants().OfType<TextBlock>().First().Text);
            }
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Lottery_themes_only_say_both_lotteries_share_them() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var picker = new LotteryThemePicker { DataContext = viewModel };
        var window = new Window { Content = picker, Width = 1040, Height = 600 };
        window.Show();
        try
        {
            GuiHarness.Flush();
            var texts = picker.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToArray();
            Assert.Contains("两种抽奖共用", texts);
            Assert.DoesNotContain(texts, Size.IsMatch);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Only_the_general_settings_writes_a_size_and_only_the_picked_pages() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = 1200, Height = 720 };
        window.Show();
        try
        {
            await GuiHarness.AccountReadAsync(viewModel);
            viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 0, 0, 0));
            for (var index = 0; index < window.Navigation.ItemCount; index++)
            {
                window.Navigation.SelectedIndex = index;
                for (var tab = 0; tab < (index == 2 ? 2 : 1); tab++)
                {
                    if (index == 2) window.GetVisualDescendants().OfType<WelcomePanel>().Single().Select(tab);
                    GuiHarness.Flush();
                    Assert.Equal(index == 8 ? [Sizes["Video"]] : [], VisibleSizes(window));
                }
            }
            var general = GeneralSettingsForm.Of(window);
            foreach (var (name, size) in Sizes)
            {
                GeneralSettingsForm.Pick(general, name);
                Assert.Equal([size], VisibleSizes(window));
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static string?[] VisibleSizes(Window window) => GuiHarness.VisibleText(window).Where(text => text is not null && Size.IsMatch(text)).ToArray();
}
