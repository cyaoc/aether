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
    private static readonly string[] SetupHelps = ["VideoSetupHelp", "LotterySetupHelp"];

    [Fact]
    public Task Page_list_writes_each_pages_size_in_one_column_that_lines_up() => OnGeneralSettings(new WebServiceStatus(true, 17890, 1, 1), page =>
    {
        Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "每个页面在直播姬里各加一个浏览器素材，宽高照每行写的填");
        var video = page.FindControl<ContentControl>("VideoSize")!;
        var lottery = page.FindControl<ContentControl>("LotterySize")!;
        Assert.Equal("1920 × 1080", video.Content);
        Assert.Equal("横屏 1920 × 1080 · 竖屏 1080 × 1920", lottery.Content);
        double Left(Control control) => control.TranslatePoint(default, page)!.Value.X;
        Assert.Equal(Left(video), Left(lottery), 0.5);
        Assert.Equal(Left(page.FindControl<Button>("CopyVideoAddressButton")!), Left(page.FindControl<Button>("CopyLotteryAddressButton")!), 0.5);
    });

    [Fact]
    public Task A_page_nobody_is_connected_to_is_told_to_take_its_address_and_size_from_its_own_row() => OnGeneralSettings(new WebServiceStatus(true, 17890, 0, 0), page =>
    {
        foreach (var name in SetupHelps)
            Assert.Equal("还没有推流软件连着。在直播姬里添加浏览器素材，地址和宽高照这一行填；已经加过的，右键素材 → 刷新。",
                GuiHarness.FindVisual<Grid>(page, name).GetVisualDescendants().OfType<TextBlock>().Single().Text);
    });

    [Fact]
    public Task Status_bars_send_the_streamer_to_the_general_settings_for_the_address_and_size() => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        var window = new Window { Width = 1040, Height = 600 };
        window.Show();
        try
        {
            foreach (SettingsPage page in new SettingsPage[] { new AnimationSettingsPage(harness.DataDirectory), new GiftAnimationSettingsPage(harness.DataDirectory) })
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
    public Task No_page_but_the_general_settings_page_list_writes_a_size() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = 1200, Height = 720 };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 0, 0));
            for (var index = 0; index < window.Navigation.ItemCount; index++)
            {
                window.Navigation.SelectedIndex = index;
                for (var tab = 0; tab < (index == 2 ? 2 : 1); tab++)
                {
                    if (index == 2) window.GetVisualDescendants().OfType<WelcomePanel>().Single().Select(tab);
                    GuiHarness.Flush();
                    var sizes = GuiHarness.VisibleText(window).Where(text => text is not null && Size.IsMatch(text)).ToArray();
                    Assert.Equal(index == 6 ? ["1920 × 1080", "横屏 1920 × 1080 · 竖屏 1080 × 1920"] : [], sizes);
                }
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    /// <summary>The general settings page at the minimum window width with the web service in this state.</summary>
    private static Task OnGeneralSettings(WebServiceStatus status, Action<GeneralSettingsPage> check) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        var viewModel = harness.CreateViewModel();
        var page = new GeneralSettingsPage(harness.DataDirectory) { DataContext = viewModel };
        var window = new Window { Content = page, Width = 1040, Height = 600 };
        window.Show();
        try
        {
            viewModel.WebService.Apply(status);
            GuiHarness.Flush();
            check(page);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
