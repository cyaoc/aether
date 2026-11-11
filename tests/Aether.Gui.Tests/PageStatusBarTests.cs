using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;

namespace Aether.Gui.Tests;

public sealed class PageStatusBarTests
{
    private static readonly WebPageTexts Sample = new("样板页", "Icon.Info", "样板", "480 × 640", "样板页：还没有推流软件连着", "样板页显示不了", "在直播姬里添加浏览器素材。");

    // The sample page has no connections of its own while the video and lottery pages have nine each.
    [Theory]
    [InlineData(true, 2, null, "样板页：已有 2 个推流软件连着", "success", null)]
    [InlineData(true, 0, null, "样板页：还没有推流软件连着", "warning", "在直播姬里添加浏览器素材。")]
    [InlineData(false, 0, "端口 17890 被别的程序占用", "网页服务没打开：端口 17890 被别的程序占用，样板页显示不了", "error", null)]
    [InlineData(false, 0, null, "网页服务没打开，样板页显示不了", "muted", null)]
    public Task Status_bar_tells_the_state_of_the_page_it_is_given_in_that_pages_words(bool running, int connections, string? error,
        string text, string tone, string? steps) => HeadlessApp.Session.Dispatch(() =>
    {
        var bar = new PageStatusBar { DataContext = new WebPageStatus(new WebServiceStatus(running, 17890, 9, 9, 0, error), connections, Sample) };
        var window = new Window { Content = bar };
        window.Show();
        try
        {
            GuiHarness.Flush();
            var title = GuiHarness.FindVisual<TextBlock>(bar, "PageStatusText");
            var card = GuiHarness.FindVisual<Border>(bar, "PageStatusCard");
            var help = GuiHarness.FindVisual<TextBlock>(bar, "PageSetupSteps");
            Assert.Equal(text, title.Text);
            Assert.Equal(tone == "success", title.Classes.Contains("success"));
            Assert.Equal(tone == "muted", title.Classes.Contains("muted"));
            Assert.Equal(tone == "warning", card.Classes.Contains("warning"));
            Assert.Equal(tone == "error", card.Classes.Contains("error"));
            Assert.Equal(steps is not null, help.IsEffectivelyVisible);
            Assert.Equal(steps ?? "", help.Text ?? "");
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
