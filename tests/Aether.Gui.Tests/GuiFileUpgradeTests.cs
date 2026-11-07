using Aether.Core;
using Aether.Gui.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Gui.Tests;

public sealed class GuiFileUpgradeTests : IDisposable
{
    private readonly GuiHarness harness = new();

    private bool? Run() => GuiFileUpgrade.Run(harness.DataDirectory, harness.Client, NullLogger.Instance);

    private void WriteGuiFile(string yaml)
    {
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(harness.LegacyGuiFile, yaml);
    }

    [Fact]
    public void Old_gui_yml_moves_into_the_settings_file_and_the_database_and_is_deleted()
    {
        WriteGuiFile("KeepRecentDanmaku: false\nAutoUpdate: false\nLastLotteryRoomId: 7734200\nRecentRooms:\n- '006'\n- '42'\n");

        Assert.False(Run());

        Assert.False(Settings.Load(harness.DataDirectory).AutoUpdateEnabled.Value);
        Assert.Contains("# 自动更新，只在 GUI 里生效，下次打开 GUI 时生效\nauto_update:\n  # true 或 false，打开 GUI 时检查并自动更新\n  enabled: false\n",
            File.ReadAllText(Settings.FilePath(harness.DataDirectory)).ReplaceLineEndings("\n"));
        var state = harness.LoadGuiState();
        Assert.False(state.KeepRecentDanmaku);
        Assert.Equal(["006", "42"], state.RecentRooms);
        Assert.False(File.Exists(harness.LegacyGuiFile));
    }

    [Theory]
    [InlineData("", true, true, new string[0])]
    [InlineData("AutoUpdate: [", true, true, new string[0])]
    [InlineData("- false", true, true, new string[0])]
    [InlineData("AutoUpdate: maybe\nKeepRecentDanmaku: false", true, false, new string[0])]
    [InlineData("AutoUpdate: [false]\nKeepRecentDanmaku: ~\nRecentRooms: 6", true, true, new[] { "6" })]
    [InlineData("KeepRecentDanmaku: abc\nRecentRooms: {a: 1}", true, true, new string[0])]
    [InlineData("Unknown: 1\nRecentRooms: ['+123', '6', '006', invalid, '0', '6', '42', '123', '456', '789']", true, true,
        new[] { "6", "006", "42", "123", "456" })]
    public void A_value_gui_yml_cannot_give_takes_its_default_alone(string yaml, bool autoUpdate, bool keepRecentDanmaku, string[] recentRooms)
    {
        WriteGuiFile(yaml);

        Assert.Equal(autoUpdate, Run());

        Assert.Equal(autoUpdate, Settings.Load(harness.DataDirectory).AutoUpdateEnabled.Value);
        Assert.Equal(keepRecentDanmaku, harness.LoadGuiState().KeepRecentDanmaku);
        Assert.Equal(recentRooms, harness.LoadGuiState().RecentRooms);
        Assert.False(File.Exists(harness.LegacyGuiFile));
    }

    [Fact]
    public void Without_gui_yml_there_is_nothing_to_move()
    {
        harness.SaveSettings(("auto_update.enabled", "false"));
        var before = File.ReadAllText(Settings.FilePath(harness.DataDirectory));

        Assert.Null(Run());

        Assert.Equal(before, File.ReadAllText(Settings.FilePath(harness.DataDirectory)));
        Assert.False(File.Exists(harness.LegacyGuiFile));
    }

    [Fact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void An_unreadable_gui_yml_stays_and_the_settings_file_decides()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Unix file modes only.");
        WriteGuiFile("AutoUpdate: true\nRecentRooms: ['6']");
        File.SetUnixFileMode(harness.LegacyGuiFile, UnixFileMode.None);

        Assert.Null(Run());

        Assert.True(File.Exists(harness.LegacyGuiFile));
        Assert.Empty(harness.LoadGuiState().RecentRooms);
        File.SetUnixFileMode(harness.LegacyGuiFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.True(Run());
        Assert.Equal(["6"], harness.LoadGuiState().RecentRooms);
    }

    [Fact]
    public void Gui_yml_overrides_the_default_a_shell_already_wrote()
    {
        Assert.True(Settings.Load(harness.DataDirectory).AutoUpdateEnabled.Value);
        WriteGuiFile("AutoUpdate: false");

        Assert.False(Run());

        Assert.False(Settings.Load(harness.DataDirectory).AutoUpdateEnabled.Value);
    }

    [Fact]
    public void A_broken_settings_file_keeps_gui_yml_and_its_switch_until_it_is_fixed()
    {
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(Settings.FilePath(harness.DataDirectory), "web_service:\n  port: invalid\n");
        WriteGuiFile("AutoUpdate: false\nRecentRooms: ['6']");

        Assert.False(Run());
        Assert.True(File.Exists(harness.LegacyGuiFile));
        Assert.Equal("web_service:\n  port: invalid\n", File.ReadAllText(Settings.FilePath(harness.DataDirectory)));
        // Rooms remembered meanwhile are not put back over by every start.
        harness.SaveRecentRooms("42");
        Assert.False(Run());
        Assert.Equal(["42"], harness.LoadGuiState().RecentRooms);

        File.WriteAllText(Settings.FilePath(harness.DataDirectory), "web_service:\n  port: 17891\n");
        Assert.False(Run());
        Assert.False(File.Exists(harness.LegacyGuiFile));
        Assert.False(Settings.Load(harness.DataDirectory).AutoUpdateEnabled.Value);
        Assert.Equal(["6"], harness.LoadGuiState().RecentRooms);
    }

    [Fact]
    public void A_database_that_cannot_be_written_keeps_gui_yml()
    {
        harness.BreakGuiState();
        WriteGuiFile("AutoUpdate: false\nRecentRooms: ['6']");

        Assert.False(Run());

        Assert.True(File.Exists(harness.LegacyGuiFile));
        Assert.False(Settings.Load(harness.DataDirectory).AutoUpdateEnabled.Value);
    }

    public void Dispose() => harness.Dispose();
}
