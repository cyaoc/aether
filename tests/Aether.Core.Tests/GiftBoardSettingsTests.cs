using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class GiftBoardSettingsTests
{
    private const string Group = """
        # 礼物榜，网页服务的 /gift-board 页面；GUI 保存后立即生效，CLI 下次观看时生效
        gift_board:
          # true 或 false，false 时不记录也不显示
          enabled: false
          # 上榜门槛（电池），必须为正整数
          threshold: 100
          # 门槛比的是：1 礼物单价，2 同一观众同一礼物累计的总价
          threshold_by: 1
          # true 或 false，是否显示看板娘；关掉后她的位置用来多放几行礼物
          mascot: true
          # true 或 false，每场开播自动换一张新榜；false 时只在 GUI 点“清空礼物榜”时换
          reset_on_live: true
          # 新的第一名时那一声的音量，0 到 100，0 表示不响
          volume: 60

        """;

    [Fact]
    public async Task A_new_settings_file_has_the_gift_board_off_with_its_defaults()
    {
        await using var h = new WatchHarness();

        var settings = Settings.Load(h.DataDirectory);

        Assert.Null(settings.Error);
        Assert.Equal((false, 100, 1, true, true, 60), (settings.GiftBoardEnabled.Value, settings.GiftBoardThreshold.Value,
            settings.GiftBoardThresholdBy.Value, settings.GiftBoardMascot.Value, settings.GiftBoardResetOnLive.Value, settings.GiftBoardVolume.Value));
        Assert.Contains(Group, File.ReadAllText(Settings.FilePath(h.DataDirectory)).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("enabled", "yes")]
    [InlineData("enabled", "1")]
    [InlineData("threshold", "0")]
    [InlineData("threshold", "-5")]
    [InlineData("threshold", "1.5")]
    [InlineData("threshold_by", "0")]
    [InlineData("threshold_by", "3")]
    [InlineData("threshold_by", "总价")]
    [InlineData("mascot", "开")]
    [InlineData("reset_on_live", "2")]
    [InlineData("volume", "-1")]
    [InlineData("volume", "101")]
    [InlineData("volume", "60.5")]
    [InlineData("volume", "loud")]
    public async Task An_invalid_value_is_reported_at_its_line_and_cannot_be_saved(string key, string value)
    {
        await using var h = new WatchHarness();
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), $"gift_board:\n  {key}: {value}\n");

        var error = Assert.IsType<SettingsException>(Settings.Load(h.DataDirectory).Error);

        Assert.Equal(($"gift_board.{key}", 2), (error.SettingName, error.LineNumber));
        Assert.NotNull(Settings.Validate($"gift_board.{key}", value));
    }

    [Fact]
    public async Task Loading_a_file_from_before_the_gift_board_uses_its_defaults_and_appends_the_group_without_a_new_format_version()
    {
        await using var h = new WatchHarness();
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "format_version: 1\nlog:\n  level: Debug # 级别\n");

        var settings = Settings.Load(h.DataDirectory);

        Assert.Null(settings.Error);
        Assert.Null(settings.UpgradedFromVersion);
        Assert.False(settings.GiftBoardEnabled.Value);
        Assert.Equal(60, settings.GiftBoardVolume.Value);
        var text = File.ReadAllText(Settings.FilePath(h.DataDirectory)).ReplaceLineEndings("\n");
        Assert.StartsWith("format_version: 1\nlog:\n  level: Debug # 级别\n", text);
        Assert.Contains(Group, text);
    }

    [Fact]
    public async Task Saving_a_change_adds_the_group_to_a_file_that_lacks_it()
    {
        await using var h = new WatchHarness();
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "log:\n  level: Debug\n");

        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["gift_board.enabled"] = "true", ["gift_board.volume"] = "0" });

        Assert.Equal("log:\n  level: Debug\ngift_board:\n  enabled: true\n  volume: 0\n", File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        var settings = Settings.Load(h.DataDirectory);
        Assert.Equal((true, 0), (settings.GiftBoardEnabled.Value, settings.GiftBoardVolume.Value));
    }
}
