using Aether.Gui.Tests.Support;

namespace Aether.Gui.Tests;

public sealed class DanmakuRetentionTests : IDisposable
{
    private readonly GuiHarness harness = new();

    [Fact]
    public void Checked_list_drops_the_oldest_beyond_one_thousand()
    {
        var viewModel = harness.CreateViewModel();
        harness.Receive(viewModel, 1001);
        Assert.Equal(1000, viewModel.Danmaku.Count);
        Assert.Equal("1", viewModel.Danmaku[0].Content);
        Assert.Equal("1000", viewModel.Danmaku[^1].Content);
    }

    [Fact]
    public void Unchecked_list_keeps_everything_and_rechecking_keeps_only_the_latest_thousand()
    {
        var viewModel = harness.CreateViewModel();
        viewModel.KeepRecentDanmaku = false;
        harness.Receive(viewModel, 1500);
        Assert.Equal(1500, viewModel.Danmaku.Count);

        viewModel.KeepRecentDanmaku = true;

        Assert.Equal(1000, viewModel.Danmaku.Count);
        Assert.Equal("500", viewModel.Danmaku[0].Content);
        Assert.Equal("1499", viewModel.Danmaku[^1].Content);
    }

    public void Dispose() => harness.Dispose();
}
