using Aether.Gui.Tests.Support;

namespace Aether.Gui.Tests;

public sealed class GuiSettingsTests : IDisposable
{
    private readonly GuiHarness harness = new();

    [Fact]
    public void Missing_file_keeps_recent_danmaku_by_default()
    {
        Assert.True(harness.LoadSettings().KeepRecentDanmaku);
        Assert.False(Directory.Exists(harness.DataDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("KeepRecentDanmaku: [")]
    [InlineData("KeepRecentDanmaku: maybe")]
    [InlineData("KeepRecentDanmaku: ~")]
    [InlineData("KeepRecentDanmaku:")]
    [InlineData("- false")]
    [InlineData("Unknown: 1")]
    public void Empty_broken_or_blank_file_falls_back_to_default(string yaml)
    {
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(harness.SettingsFile, yaml);
        Assert.True(harness.LoadSettings().KeepRecentDanmaku);
    }

    [Fact]
    public void Unknown_keys_do_not_discard_known_settings()
    {
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(harness.SettingsFile, "Unknown: 1\nKeepRecentDanmaku: false");
        Assert.False(harness.LoadSettings().KeepRecentDanmaku);
    }

    [Fact]
    public void Toggling_the_checkbox_writes_gui_yml_that_survives_a_restart()
    {
        harness.CreateViewModel().KeepRecentDanmaku = false;
        Assert.Equal("KeepRecentDanmaku: false", File.ReadAllText(harness.SettingsFile).Trim());
        Assert.False(harness.CreateViewModel().KeepRecentDanmaku);
    }

    public void Dispose() => harness.Dispose();
}
