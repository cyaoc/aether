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

    [Theory]
    [InlineData("KeepRecentDanmaku: false", new string[0])]
    [InlineData("RecentRooms:", new string[0])]
    [InlineData("RecentRooms: ~", new string[0])]
    [InlineData("RecentRooms: ['6', '006', invalid, '0', '-1', ~, '6', '42', '123', '456', '789']",
        new[] { "6", "006", "42", "123", "456" })]
    public void Recent_rooms_load_valid_unique_numbers_in_order(string yaml, string[] expected)
    {
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(harness.SettingsFile, yaml);
        Assert.Equal(expected, harness.LoadSettings().RecentRooms);
    }

    [Theory]
    [InlineData("KeepRecentDanmaku: false\nRecentRooms: 6", false, new[] { "6" })]
    [InlineData("KeepRecentDanmaku: false\nRecentRooms: {a: 1}", false, new string[0])]
    [InlineData("KeepRecentDanmaku: abc\nRecentRooms: ['6']", true, new[] { "6" })]
    [InlineData("KeepRecentDanmaku: [false]\nRecentRooms: ['6']", true, new[] { "6" })]
    public void A_wrongly_shaped_value_falls_back_alone(string yaml, bool keepRecentDanmaku, string[] recentRooms)
    {
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(harness.SettingsFile, yaml);
        var settings = harness.LoadSettings();
        Assert.Equal(keepRecentDanmaku, settings.KeepRecentDanmaku);
        Assert.Equal(recentRooms, settings.RecentRooms);
    }

    [Fact]
    public void Toggling_the_checkbox_writes_gui_yml_that_survives_a_restart()
    {
        harness.CreateViewModel().KeepRecentDanmaku = false;
        Assert.Contains("KeepRecentDanmaku: false", File.ReadAllText(harness.SettingsFile));
        Assert.False(harness.CreateViewModel().KeepRecentDanmaku);
    }

    [Fact]
    public void Recent_rooms_survive_a_save_and_reload_with_other_settings()
    {
        var settings = harness.LoadSettings();
        settings.RecentRooms = ["006", "42"];
        settings.KeepRecentDanmaku = false;
        settings.Save();
        var reloaded = harness.LoadSettings();
        Assert.Equal(["006", "42"], reloaded.RecentRooms);
        Assert.False(reloaded.KeepRecentDanmaku);
    }

    public void Dispose() => harness.Dispose();
}
