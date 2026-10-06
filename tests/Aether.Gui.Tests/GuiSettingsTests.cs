using Aether.Gui.Tests.Support;

namespace Aether.Gui.Tests;

public sealed class GuiSettingsTests : IDisposable
{
    private readonly GuiHarness h = new();

    [Fact]
    public void Missing_file_keeps_recent_danmaku_by_default()
    {
        Assert.True(h.LoadSettings().KeepRecentDanmaku);
        Assert.False(Directory.Exists(h.DataDirectory));
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
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(h.SettingsFile, yaml);
        Assert.True(h.LoadSettings().KeepRecentDanmaku);
    }

    [Fact]
    public void Unknown_keys_do_not_discard_known_settings()
    {
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(h.SettingsFile, "Unknown: 1\nKeepRecentDanmaku: false");
        Assert.False(h.LoadSettings().KeepRecentDanmaku);
    }

    [Fact]
    public void Toggling_the_checkbox_writes_gui_yml_that_survives_a_restart()
    {
        h.CreateViewModel().KeepRecentDanmaku = false;
        Assert.Equal("KeepRecentDanmaku: false", File.ReadAllText(h.SettingsFile).Trim());
        Assert.False(h.CreateViewModel().KeepRecentDanmaku);
    }

    public void Dispose() => h.Dispose();
}
