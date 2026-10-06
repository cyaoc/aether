using Aether.Gui.Tests.Support;

namespace Aether.Gui.Tests;

public sealed class DanmakuRetentionTests : IDisposable
{
    private readonly GuiHarness h = new();

    [Fact]
    public void Checked_list_drops_the_oldest_beyond_one_thousand()
    {
        var viewModel = h.CreateViewModel();
        h.Receive(viewModel, 1001);
        Assert.Equal(1000, viewModel.Danmaku.Count);
        Assert.Equal("1", viewModel.Danmaku[0].Content);
        Assert.Equal("1000", viewModel.Danmaku[^1].Content);
    }

    [Fact]
    public void Unchecked_list_keeps_everything_and_rechecking_keeps_only_the_latest_thousand()
    {
        var viewModel = h.CreateViewModel();
        viewModel.KeepRecentDanmaku = false;
        h.Receive(viewModel, 1500);
        Assert.Equal(1500, viewModel.Danmaku.Count);

        viewModel.KeepRecentDanmaku = true;

        Assert.Equal(1000, viewModel.Danmaku.Count);
        Assert.Equal("500", viewModel.Danmaku[0].Content);
        Assert.Equal("1499", viewModel.Danmaku[^1].Content);
    }

    public void Dispose() => h.Dispose();
}
