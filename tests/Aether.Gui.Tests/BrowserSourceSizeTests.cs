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
    private static readonly string[] Sizes = ["1920 × 1080", "横屏 1920 × 1080 · 竖屏 1080 × 1920", "480 × 1000，可以等比放大", "480 × 640"];

    [Fact]
    public Task Each_pages_detail_writes_its_size_under_a_note_of_one_browser_source_per_page() =>
        GeneralSettingsForm.Open(1040, 600, new WebServiceStatus(true, 17890, 1, 1, 0), (page, _) =>
        {
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "每个页面在直播姬里各加一个浏览器素材");
            Assert.Equal(Sizes, GeneralSettingsForm.Pages.Select(name => GuiHarness.FindPageDetailControl<TextBlock>(page, name, "PageSize").Text));
        });

    [Fact]
    public Task A_page_nobody_is_connected_to_is_told_to_take_its_address_and_size_from_its_own_detail() =>
        GeneralSettingsForm.Open(1040, 600, new WebServiceStatus(true, 17890, 0, 0, 0), (page, _) =>
        {
            foreach (var name in GeneralSettingsForm.Pages)
                Assert.Equal("还没有推流软件连着。在直播姬里添加浏览器素材，地址和宽高照上面填；已经加过的，右键素材 → 刷新。",
                    GuiHarness.FindPageDetailControl<Border>(page, name, "SetupHelp").GetVisualDescendants().OfType<TextBlock>().Single().Text);
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
                    Assert.Equal(index == 8 ? [Sizes[0]] : [], VisibleSizes(window));
                }
            }
            var general = window.GetVisualDescendants().OfType<GeneralSettingsPage>().Single();
            for (var i = 0; i < Sizes.Length; i++)
            {
                GeneralSettingsForm.Pick(general, GeneralSettingsForm.Pages[i]);
                Assert.Equal([Sizes[i]], VisibleSizes(window));
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static string?[] VisibleSizes(Window window) => GuiHarness.VisibleText(window).Where(text => text is not null && Size.IsMatch(text)).ToArray();
}
