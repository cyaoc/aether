using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class EntryAnimationSettingsTests
{
    [Fact]
    public void Animation_list_accepts_tiers_off_and_video_names()
    {
        Assert.Null(Settings.Validate("entry_animation_viewers.123", "40"));
        Assert.Null(Settings.Validate("entry_animation_viewers.123", "0"));
        Assert.Null(Settings.Validate("entry_animation_viewers.123", "老板.webm"));
        Assert.NotNull(Settings.Validate("entry_animation_viewers.123", "35"));
    }

    [Theory]
    [InlineData(29, null)]
    [InlineData(39, 30)]
    [InlineData(50, 50)]
    [InlineData(99, 60)]
    public void Fan_club_level_rounds_down_and_caps(int level, int? expected) =>
        Assert.Equal(expected, EntryTier.FromLevel(level)?.Value);

    [Theory]
    [InlineData(9999, null)]
    [InlineData(10000, 30)]
    [InlineData(50000, 40)]
    [InlineData(200001, 50)]
    [InlineData(1000000, 60)]
    public void Spend_compares_exact_coin_thresholds(long coins, int? expected) =>
        Assert.Equal(expected, EntryTier.FromSpend(coins, [100, 500, 2000, 10000])?.Value);

    [Fact]
    public async Task Viewer_form_save_writes_each_row_and_drops_removed_ones()
    {
        await using var h = new WatchHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> {
            ["entry_animation_viewers.1"] = "40", ["entry_animation_viewers.2"] = "0", ["entry_animation_viewers.3"] = "old.webm", ["entry_animation_viewers.4"] = "50" });
        var snapshot = Settings.Load(h.DataDirectory);
        Settings.SaveAnimationViewers(h.DataDirectory, new Dictionary<string, string>(), new Dictionary<long, string> { [1] = "40", [2] = "0", [3] = "new.webm" }, snapshot);
        var saved = Settings.Load(h.DataDirectory);
        Assert.Equal(40, saved.EntryAnimationViewers[1].Value.Tier?.Value);
        Assert.Equal(AnimationChoice.Off, saved.EntryAnimationViewers[2].Value);
        Assert.Equal("new.webm", saved.EntryAnimationViewers[3].Value.Video);
        Assert.False(saved.EntryAnimationViewers.ContainsKey(4));
    }

    [Fact]
    public async Task Invalid_threshold_order_is_rejected_on_read_and_save()
    {
        await using var h = new WatchHarness();
        var settings = Settings.Load(h.DataDirectory);
        var before = File.ReadAllText(Settings.FilePath(h.DataDirectory));
        Assert.Throws<SettingsException>(() => Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["gift_animation.tier_40_spend"] = "100" }, settings));
        Assert.Equal(before, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "gift_animation:\n  tier_40_spend: 100\n");
        var error = Assert.IsType<SettingsException>(Settings.Load(h.DataDirectory).Error);
        Assert.Equal(2, error.LineNumber);
    }
}
