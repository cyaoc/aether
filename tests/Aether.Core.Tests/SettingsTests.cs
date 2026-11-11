using System.Text.RegularExpressions;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

public sealed class SettingsTests : IAsyncDisposable
{
    private readonly WatchHarness harness = new();
    private string SettingsPath => Path.Combine(harness.DataDirectory, "aether.yml");

    [Theory]
    [InlineData("关闭", 0)]
    [InlineData("舰长", 1)]
    [InlineData("提督", 2)]
    [InlineData("总督", 3)]
    public void Upgrade_converts_each_guard_tier(string old, int expected)
    {
        Write($"welcome:\n  guard: '{old}' # 我的门槛\n");
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(expected, settings.WelcomeGuard.Value);
        Assert.Equal(0, settings.UpgradedFromVersion);
        Assert.Contains("# 我的门槛", File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("true", 0, 100)]
    [InlineData("false", 30, 80)]
    public void Upgrade_keeps_animation_behavior_or_new_defaults(string enabled, int level, int volume)
    {
        Write($"welcome_animation:\n  enabled: {enabled} # 开关\n");
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(level, settings.EntryAnimationFanClubLevel.Value);
        Assert.Equal(volume, settings.EntryAnimationVolume.Value);
        Assert.Equal(enabled == "true", File.ReadAllText(SettingsPath).Contains("fan_club_level: 0\n  volume: 100"));
    }

    [Theory]
    [InlineData("utf-8", "\n")]
    [InlineData("utf-8", "\r\n")]
    [InlineData("utf-16", "\r\n")]
    public void Upgrade_preserves_encoding_tags_indentation_and_document_markers(string encodingName, string newline)
    {
        var encoding = System.Text.Encoding.GetEncoding(encodingName);
        var original = "---\n  !!str 'welcome':\n    !!str guard: 提督 # 保留\n  welcome_animation:\n    enabled: true\n...\n".ReplaceLineEndings(newline);
        Write(original);
        File.WriteAllText(SettingsPath, original, encoding);
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(2, settings.WelcomeGuard.Value);
        var expected = original.Replace("  !!str 'welcome':", "  # 设置文件格式版本，由程序维护" + newline + "  format_version: 1" + newline + newline + "  !!str welcome_danmaku:")
            .Replace("guard: 提督", "guard: 2").Replace("welcome_animation:", "entry_animation:")
            .Replace("enabled: true" + newline, "enabled: true" + newline + "    fan_club_level: 0" + newline + "    volume: 100" + newline);
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(expected)), File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Upgrade_keeps_added_animation_lines_when_a_template_comment_follows_them()
    {
        Write("welcome_animation:\n  enabled: true\n# 欢迎动画的视频，放在 data/videos/ 里，每行一位观众：UID: 文件名\nwelcome_animation_videos:\n  1: a.mp4\n");
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(0, settings.EntryAnimationFanClubLevel.Value);
        Assert.Equal(100, settings.EntryAnimationVolume.Value);
        Assert.Contains("  volume: 100\n# 动画名单：一行一位，值是 30、40、50、60，0 表示不播，或视频文件名\nentry_animation_viewers:",
            File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Upgrade_is_written_before_an_invalid_value_is_reported_so_its_line_matches_the_file()
    {
        Write("send:\n  interval_seconds: 0\nwelcome:\n  guard: 舰长\n");
        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);
        var lines = File.ReadAllLines(SettingsPath);
        Assert.Equal("  interval_seconds: 0", lines[error.LineNumber - 1]);
        Assert.Contains("welcome_danmaku:", lines);
    }

    [Fact]
    public void Upgrade_only_replaces_exact_template_comments_and_keeps_unknown_content()
    {
        const string old = "# 大航海身份达到哪一档就欢迎：关闭、舰长、提督、总督；提督表示提督和总督";
        Write($"# 我的说明\nwelcome:\n  {old}\n  guard: 提督\n  {old}（自改）\n  custom: yes\nother: |\n  {old}\n");
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Contains("welcome_danmaku.custom", settings.UnknownKeys);
        var text = File.ReadAllText(SettingsPath);
        Assert.Contains("# 大航海身份门槛：0 关闭，1 舰长及以上，2 提督及以上，3 总督", text);
        Assert.Contains(old + "（自改）", text);
        Assert.Contains("other: |\n  " + old, text);
        Assert.Contains("# 我的说明", text);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("word")]
    [InlineData("1.5")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Invalid_or_future_format_version_prevents_all_writes(string version)
    {
        Write($"format_version: {version}\nwelcome:\n  guard: 提督\n");
        var before = File.ReadAllBytes(SettingsPath);
        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal("format_version", error.SettingName);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { ["log.level"] = "Debug" }));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Theory]
    [InlineData("{welcome: {guard: 提督}}")]
    [InlineData("welcome_animation: {enabled: true}")]
    [InlineData("welcome: [")]
    [InlineData("other: &g 提督\nwelcome:\n  guard: *g")]
    [InlineData("other: &g\n  guard: 提督\nwelcome: *g")]
    [InlineData("other: &a\n  enabled: true\nwelcome_animation: *a")]
    public void Failed_upgrade_does_not_append_groups_and_retries_after_repair(string text)
    {
        Write(text);
        var before = File.ReadAllBytes(SettingsPath);
        Assert.NotNull(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Write("welcome:\n  guard: 提督\n");
        Assert.Equal(2, Settings.Load(harness.DataDirectory).WelcomeGuard.Value);
        Assert.Contains("format_version: 1", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Upgrade_rejects_lossy_encoding_without_appending_groups()
    {
        Write("welcome:\n  enabled: true\n# ");
        // GBK bytes for Chinese text cannot round-trip through the default UTF-8 reader.
        var before = File.ReadAllBytes(SettingsPath).Concat(new byte[] { 0xc4, 0xe3, 0xba, 0xc3 }).ToArray();
        File.WriteAllBytes(SettingsPath, before);
        Assert.Contains("UTF-8", Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error).Message);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void Upgrade_write_failure_preserves_file_and_retries()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Unix file modes only.");
        Write("welcome:\n  enabled: true\n");
        var before = File.ReadAllBytes(SettingsPath);
        var mode = File.GetUnixFileMode(harness.DataDirectory);
        File.SetUnixFileMode(harness.DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.NotNull(Settings.Load(harness.DataDirectory).Error);
            Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        }
        finally { File.SetUnixFileMode(harness.DataDirectory, mode); }
        Assert.True(Settings.Load(harness.DataDirectory).WelcomeEnabled.Value);
        Assert.Contains("format_version: 1", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Upgrade_does_not_merge_old_groups_into_existing_new_groups()
    {
        Write("welcome:\n  enabled: true\nwelcome_danmaku:\n  enabled: false\nwelcome_animation:\n  enabled: true\nentry_animation:\n  enabled: false\nwelcome_animation_videos:\n  42: old.mp4\nentry_animation_viewers:\n  42: new.mp4\n...\n");
        var before = File.ReadAllBytes(SettingsPath);
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.False(settings.WelcomeEnabled.Value);
        Assert.False(settings.EntryAnimationEnabled.Value);
        Assert.Equal("new.mp4", settings.WelcomeAnimationVideos[42].Value);
        Assert.Contains("welcome.enabled", settings.UnknownKeys);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    // The fixture is the frozen v0.2.3 Template with non-default values; keep it unchanged.
    [Fact]
    public void Upgrade_preserves_the_v023_settings_and_is_idempotent()
    {
        Write(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v0.2.3.yml")));
        var settings = Settings.Load(harness.DataDirectory, harness.Logger);
        Assert.Null(settings.Error);
        Assert.Empty(settings.UnknownKeys);
        Assert.Equal(0, settings.UpgradedFromVersion);
        Assert.Contains(harness.Logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("升级"));
        Assert.True(settings.WelcomeEnabled.Value);
        Assert.Equal(new[] { "欢迎{昵称}", "{@}欢迎回家" }, settings.WelcomeTexts.Value);
        Assert.Equal(new long[] { 12345, 67890 }, settings.WelcomeUids.Value);
        Assert.Equal(12, settings.WelcomeFanClubLevel.Value);
        Assert.Equal(2, settings.WelcomeGuard.Value);
        Assert.Equal(25, settings.WelcomeWealthLevel.Value);
        Assert.True(settings.EntryAnimationEnabled.Value);
        Assert.Equal(0, settings.EntryAnimationFanClubLevel.Value);
        Assert.Equal(100, settings.EntryAnimationVolume.Value);
        Assert.Equal("小明进场.mp4", settings.WelcomeAnimationVideos[12345].Value);
        Assert.Equal(17, settings.LogRetentionDays.Value);
        Assert.Equal(LogLevel.Debug, settings.LogLevel.Value);
        Assert.True(settings.BlindBoxEnabled.Value);
        Assert.False(settings.BlindBoxAnnounce.Value);
        Assert.Equal("今天", settings.BlindBoxDailyKeyword.Value);
        Assert.Equal("本月", settings.BlindBoxMonthlyKeyword.Value);
        Assert.Equal("今年盲盒", settings.BlindBoxYearlyKeyword.Value);
        Assert.Equal(3, settings.SendIntervalSeconds.Value);
        Assert.Equal(17891, settings.WebServicePort.Value);
        // The fixture already ends with a blank line, so the appended group gets no second one.
        Assert.Contains("  12345: 小明进场.mp4\n\n# 送礼动画", File.ReadAllText(SettingsPath).ReplaceLineEndings("\n"));
        // The version goes where Template has it, above the log group's own comment.
        Assert.StartsWith("# 设置文件格式版本，由程序维护\nformat_version: 1\n\n# 日志设置，修改后重启程序生效\nlog:",
            File.ReadAllText(SettingsPath).ReplaceLineEndings("\n"));
        var bytes = File.ReadAllBytes(SettingsPath);
        Assert.Null(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal(bytes, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Web_service_port_is_appended_and_legacy_animation_port_only_warns()
    {
        Write("entry_animation:\n  enabled: false\n  port: invalid\n");
        var settings = Settings.Load(harness.DataDirectory, harness.Logger);
        Assert.Null(settings.Error);
        Assert.Contains("entry_animation.port", settings.UnknownKeys);
        Assert.Null(Settings.Validate("web_service.port", "17890"));
        Assert.Contains("web_service:\n", File.ReadAllText(SettingsPath));
        Assert.Contains("port: 17890", File.ReadAllText(SettingsPath));
        Assert.Contains(harness.Logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("entry_animation.port"));
    }

    [Fact]
    public void Welcome_animation_defaults_and_empty_video_group_are_appended_once()
    {
        Write("# 保留\nwelcome_danmaku:\n  enabled: true\n");
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.False(settings.EntryAnimationEnabled.Value);
        Assert.Equal(17890, settings.WebServicePort.Value);
        Assert.Empty(settings.WelcomeAnimationVideos);
        Assert.Null(Settings.Validate("entry_animation.enabled", "false"));
        Assert.Null(Settings.Validate("web_service.port", "17890"));
        var text = File.ReadAllText(SettingsPath);
        Assert.StartsWith("# 保留\nwelcome_danmaku:\n  enabled: true\n", text);
        Assert.Contains("entry_animation:\n", text);
        Assert.Contains("port: 17890", text);
        Assert.Contains("entry_animation_viewers:\n", text);
        Assert.Null(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal(text, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("0", "x.mp4")]
    [InlineData("+123", "x.mp4")]
    [InlineData("-1", "x.mp4")]
    [InlineData("x", "x.mp4")]
    [InlineData("9223372036854775808", "x.mp4")]
    [InlineData("1", "../x.mp4")]
    [InlineData("1", "'..\\x.mp4'")]
    [InlineData("1", "..")]
    [InlineData("1", "x.gif")]
    [InlineData("1", "'C:x.mp4'")]
    [InlineData("1", "[]")]
    [InlineData("1", "|\n    x.mp4")]
    public void Invalid_animation_mapping_reports_its_line_and_leaves_the_file_untouched(string uid, string value)
    {
        var yaml = $"# 视频\nentry_animation_viewers:\n  {uid}: {value}\n";
        Write(yaml);
        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal(3, error.LineNumber);
        Assert.Equal("entry_animation_viewers." + uid, error.SettingName);
        Assert.Equal(yaml, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    public void Invalid_animation_port_reports_its_line(string port)
    {
        Write($"web_service:\n  port: {port}\n");
        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal(2, error.LineNumber);
        Assert.NotNull(Settings.Validate("web_service.port", port));
    }

    [Fact]
    public void Missing_animation_files_warn_without_invalidating_or_removing_the_mapping()
    {
        Write("gift_animation:\nentry_animation_viewers:\n  42: 找不到.mp4\n");
        var loaded = Settings.Load(harness.DataDirectory, harness.Logger);
        Assert.Null(loaded.Error);
        Assert.Equal("找不到.mp4", loaded.WelcomeAnimationVideos[42].Value);
        Assert.Empty(loaded.UnknownKeys);
        Assert.Contains(harness.Logger.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("42") && entry.Message.Contains("找不到.mp4"));
    }

    [Fact]
    public void Numeric_aliases_of_the_same_uid_are_rejected()
    {
        Write("entry_animation_viewers:\n  01: a.mp4\n  1: b.mp4\n");
        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal(3, error.LineNumber);
        Assert.Contains("重复", error.Message);
    }

    [Fact]
    public void Flow_mapping_is_readable_but_cannot_be_line_edited()
    {
        const string yaml = "entry_animation_viewers: {42: a.mp4}\n...\n";
        Write(yaml);
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.False(settings.CanEditEntryAnimationViewers);
        Assert.Equal("a.mp4", settings.WelcomeAnimationVideos[42].Value);
        Assert.Throws<SettingsException>(() => Settings.SaveAnimationViewers(harness.DataDirectory,
            new Dictionary<string, string>(), new Dictionary<long, string>(), settings));
        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { ["entry_animation_viewers.43"] = "b.mp4" }));
        Assert.Equal(yaml, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Deleting_a_uid_key_with_an_anchor_must_not_break_aliases_elsewhere()
    {
        const string yaml = "gift_animation:\nentry_animation_viewers:\n  &who 42: a.mp4\nother:\n  uid: *who\n...\n";
        Write(yaml);
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.False(settings.WelcomeAnimationVideos[42].CanEdit);
        Assert.Throws<SettingsException>(() => Settings.SaveAnimationViewers(harness.DataDirectory,
            new Dictionary<string, string>(), new Dictionary<long, string>(), settings));
        Assert.Equal(yaml, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("00042")]
    [InlineData("\" 42\"")]
    [InlineData("'42 '")]
    public void Animation_mapping_add_replace_delete_preserves_other_lines_and_encoding(string uid)
    {
        var yaml = $"# 我的文件\r\ngift_animation:\r\nentry_animation_viewers:\r\n  # 观众\r\n  {uid}: 原片.mp4 # 保留\r\nlog:\r\n  level: Debug\r\n...\r\n";
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(SettingsPath, yaml, System.Text.Encoding.Unicode);
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["entry_animation_viewers.42"] = "新片.webm" });
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["entry_animation_viewers.43"] = "# 进场.mp4" });
        var replaced = yaml.Replace("原片.mp4", "新片.webm").Replace("log:", "  43: '# 进场.mp4'\r\nlog:");
        Assert.Equal(replaced, File.ReadAllText(SettingsPath));
        Assert.Equal("新片.webm", Settings.Load(harness.DataDirectory).WelcomeAnimationVideos[42].Value);
        Settings.SaveAnimationViewers(harness.DataDirectory, new Dictionary<string, string>(),
            new Dictionary<long, string> { [43] = "# 进场.mp4" }, Settings.Load(harness.DataDirectory));
        Settings.SaveAnimationViewers(harness.DataDirectory, new Dictionary<string, string>(),
            new Dictionary<long, string>(), Settings.Load(harness.DataDirectory));
        var deleted = yaml.Replace($"  {uid}: 原片.mp4 # 保留\r\n", "");
        Assert.Equal(deleted, File.ReadAllText(SettingsPath));
        Assert.Empty(Settings.Load(harness.DataDirectory).WelcomeAnimationVideos);
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["entry_animation_viewers.44"] = "最后.mp4" });
        Assert.Equal(deleted.Replace("log:", "  44: 最后.mp4\r\nlog:"), File.ReadAllText(SettingsPath));
        Assert.True(File.ReadAllBytes(SettingsPath).AsSpan().StartsWith(System.Text.Encoding.Unicode.GetPreamble()));
    }

    [Fact]
    public void Welcome_animation_form_save_updates_switch_port_and_mapping_together_or_not_at_all()
    {
        Write("entry_animation:\n  enabled: false # 开关\nweb_service:\n  port: 17890\ngift_animation:\nentry_animation_viewers:\n  42: old.mp4\n  43: shared.webm\n");
        var snapshot = Settings.Load(harness.DataDirectory);
        var before = File.ReadAllText(SettingsPath);
        var changes = new Dictionary<string, string> { ["entry_animation.enabled"] = "true", ["web_service.port"] = "17891" };
        Assert.Throws<SettingsException>(() => Settings.SaveAnimationViewers(harness.DataDirectory, changes,
            new Dictionary<long, string> { [44] = "../bad.mp4" }, snapshot));
        Assert.Equal(before, File.ReadAllText(SettingsPath));
        Settings.SaveAnimationViewers(harness.DataDirectory, changes,
            new Dictionary<long, string> { [43] = "shared.webm", [44] = "new.mp4" }, snapshot);
        var saved = Settings.Load(harness.DataDirectory);
        Assert.True(saved.EntryAnimationEnabled.Value);
        Assert.Equal(17891, saved.WebServicePort.Value);
        Assert.Equal(new long[] { 43, 44 }, saved.WelcomeAnimationVideos.Keys.Order());
        Assert.Equal("new.mp4", saved.WelcomeAnimationVideos[44].Value);
        Assert.Contains("enabled: true # 开关", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void An_existing_five_second_interval_is_preserved()
    {
        const string yaml = "auto_update:\nweb_service:\nentry_animation:\ngift_animation:\ndesktop_pet:\nentry_animation_viewers:\nwelcome_danmaku:\nlog:\nblind_box:\nsend:\n  interval_seconds: 5 # 保留原值\n";
        Write(yaml);
        Assert.Equal(5, Settings.Load(harness.DataDirectory).SendIntervalSeconds.Value);
        Assert.Equal(yaml, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Announcement_setting_defaults_on_and_saves_only_its_line_or_appends_to_existing_group()
    {
        Assert.True(Settings.Load(harness.DataDirectory).BlindBoxAnnounce.Value);
        Assert.Contains("announce: true", File.ReadAllText(SettingsPath));
        Write("auto_update:\nweb_service:\nentry_animation:\ngift_animation:\ndesktop_pet:\nentry_animation_viewers:\nwelcome_danmaku:\nlog:\nsend:\n# 保留\nblind_box:\n  enabled: true\n  daily_keyword: 今日盲盒 # 关键字\n");
        var before = File.ReadAllText(SettingsPath);
        Assert.True(Settings.Load(harness.DataDirectory).BlindBoxAnnounce.Value);
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["blind_box.announce"] = "false" });
        Assert.Equal(before + "  announce: false\n", File.ReadAllText(SettingsPath));
        Assert.False(Settings.Load(harness.DataDirectory).BlindBoxAnnounce.Value);
        Write(before + "  announce: false # 开关\n");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["blind_box.announce"] = "true" });
        Assert.Equal(before + "  announce: true # 开关\n", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Template_defaults_match_the_defaults_used_when_a_setting_is_missing()
    {
        // Each default is written twice, in Template and in its definition; this keeps the two from drifting apart.
        var fromTemplate = Settings.Load(harness.DataDirectory);
        Assert.Null(fromTemplate.Error);
        Assert.Empty(fromTemplate.UnknownKeys);
        Assert.Null(fromTemplate.UpgradedFromVersion);
        Assert.Contains("format_version: 1", File.ReadAllText(SettingsPath));
        // A definition without its line in Template would read its own default on both sides below.
        string? templateGroup = null;
        var templateNames = new List<string>();
        foreach (var line in File.ReadAllText(SettingsPath).ReplaceLineEndings("\n").Split('\n'))
            if (Regex.Match(line, @"^(\w+):$") is { Success: true } header) templateGroup = header.Groups[1].Value;
            else if (Regex.Match(line, @"^  (\w+):") is { Success: true } key) templateNames.Add(templateGroup + "." + key.Groups[1].Value);
        Assert.Equal(Settings.AllDefinitions.Where(definition => definition is not (Settings.ViewerGroupDefinition or Settings.LineDefinition)).Select(definition => definition.Name).Order(), templateNames.Order());
        // Keep every group present so Load cannot fill it from Template and hide a mismatched definition default.
        Write(string.Concat(Settings.AllDefinitions.Select(definition => definition.Group).Distinct().Select(group => group + ":\n")));
        var missing = Settings.Load(harness.DataDirectory);
        Assert.Null(missing.Error);
        // Paired with each name, so a failure says which setting drifted.
        Assert.Equal(Values(fromTemplate), Values(missing));

        static IEnumerable<(string, string)> Values(Settings settings) =>
            Settings.AllDefinitions.Where(definition => definition is not (Settings.ViewerGroupDefinition or Settings.LineDefinition))
                .Select(definition => (definition.Name, definition.ValueText(settings, definition.Name)));
    }

    [Fact]
    public void Blind_box_tally_and_send_queue_settings_have_defaults_and_save_missing_keys_without_rewriting_other_lines()
    {
        Settings.Load(harness.DataDirectory);
        var template = File.ReadAllText(SettingsPath);
        Assert.Contains("daily_keyword: 今日盲盒", template);
        Assert.Contains("monthly_keyword: 本月盲盒", template);
        Assert.Contains("yearly_keyword: 今年盲盒", template);
        Assert.Contains("interval_seconds: 1", template);
        // Group comments say when settings apply; each item describes its accepted values.
        var lines = template.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("# 盲盒统计，下次观看直播间时生效", lines[Array.FindIndex(lines, line => line == "blind_box:") - 1]);
        Assert.Equal("  # 今日查询关键字，非空文字", lines[Array.FindIndex(lines, line => line.Contains("daily_keyword: 今日盲盒")) - 1]);
        Assert.Equal("  # 本月查询关键字，非空文字；和今日关键字相同时只回复今日", lines[Array.FindIndex(lines, line => line.Contains("monthly_keyword: 本月盲盒")) - 1]);
        Assert.Equal("  # 今年查询关键字，非空文字；和今日或本月关键字相同时只回复今日或本月", lines[Array.FindIndex(lines, line => line.Contains("yearly_keyword: 今年盲盒")) - 1]);
        Assert.Contains("下次观看直播间时生效", lines[Array.FindIndex(lines, line => line == "send:") - 1]);
        Assert.Null(Settings.Validate("blind_box.daily_keyword", "查盲盒"));
        Assert.Null(Settings.Validate("blind_box.monthly_keyword", "查月盲盒"));
        Assert.Null(Settings.Validate("blind_box.yearly_keyword", "查年盲盒"));
        Assert.Null(Settings.Validate("send.interval_seconds", "7"));
        Write("# 保留\nblind_box:\n  enabled: true\n");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string>
        {
            ["blind_box.daily_keyword"] = "查盲盒", ["blind_box.monthly_keyword"] = "查月盲盒", ["blind_box.yearly_keyword"] = "查年盲盒",
            ["send.interval_seconds"] = "7",
        });
        Assert.Equal("# 保留\nblind_box:\n  enabled: true\n  daily_keyword: 查盲盒\n  monthly_keyword: 查月盲盒\n  yearly_keyword: 查年盲盒\nsend:\n  interval_seconds: 7\n", File.ReadAllText(SettingsPath));
        Assert.Empty(Settings.Load(harness.DataDirectory).UnknownKeys);
        Assert.Equal("查盲盒", Settings.Load(harness.DataDirectory).BlindBoxDailyKeyword.Value);
        Assert.Equal("查月盲盒", Settings.Load(harness.DataDirectory).BlindBoxMonthlyKeyword.Value);
        Assert.Equal("查年盲盒", Settings.Load(harness.DataDirectory).BlindBoxYearlyKeyword.Value);
        Assert.Equal(7, Settings.Load(harness.DataDirectory).SendIntervalSeconds.Value);
    }

    [Theory]
    [InlineData("blind_box", "daily_keyword", "''")]
    [InlineData("blind_box", "daily_keyword", "'   '")]
    [InlineData("blind_box", "daily_keyword", "")]
    [InlineData("blind_box", "daily_keyword", "[]")]
    [InlineData("blind_box", "daily_keyword", "\"两行\\n文字\"")]
    [InlineData("blind_box", "monthly_keyword", "''")]
    [InlineData("blind_box", "monthly_keyword", "'   '")]
    [InlineData("blind_box", "monthly_keyword", "")]
    [InlineData("blind_box", "monthly_keyword", "[]")]
    [InlineData("blind_box", "monthly_keyword", "\"两行\\n文字\"")]
    [InlineData("blind_box", "yearly_keyword", "''")]
    [InlineData("blind_box", "yearly_keyword", "'   '")]
    [InlineData("blind_box", "yearly_keyword", "")]
    [InlineData("blind_box", "yearly_keyword", "[]")]
    [InlineData("blind_box", "yearly_keyword", "\"两行\\n文字\"")]
    [InlineData("blind_box", "announce", "yes")]
    [InlineData("blind_box", "announce", "True")]
    [InlineData("blind_box", "announce", "0")]
    [InlineData("blind_box", "announce", "null")]
    [InlineData("blind_box", "announce", "[]")]
    [InlineData("blind_box", "announce", "")]
    [InlineData("send", "interval_seconds", "0")]
    [InlineData("send", "interval_seconds", "-1")]
    [InlineData("send", "interval_seconds", "1.5")]
    [InlineData("send", "interval_seconds", "abc")]
    [InlineData("send", "interval_seconds", "2147483648")]
    public async Task Invalid_blind_box_tally_and_send_queue_settings_refuse_watching_with_the_setting_location(string group, string key, string value)
    {
        Write($"{group}:\n  {key}: {value}\n");
        await using var updates = harness.Watch();
        var error = await Assert.ThrowsAsync<SettingsException>(() => updates.MoveNextAsync().AsTask());
        Assert.Equal(group + "." + key, error.SettingName);
        Assert.Equal(2, error.LineNumber);
        Assert.Equal(SettingsPath, error.FilePath);
        Assert.Empty(harness.Http.Requests);
    }

    [Theory]
    [InlineData("blind_box.daily_keyword", "")]
    [InlineData("blind_box.daily_keyword", " \t ")]
    [InlineData("blind_box.daily_keyword", "两行\n文字")]
    [InlineData("blind_box.monthly_keyword", "")]
    [InlineData("blind_box.monthly_keyword", " \t ")]
    [InlineData("blind_box.monthly_keyword", "两行\n文字")]
    [InlineData("blind_box.yearly_keyword", "")]
    [InlineData("blind_box.yearly_keyword", " \t ")]
    [InlineData("blind_box.yearly_keyword", "两行\n文字")]
    [InlineData("blind_box.announce", "False")]
    [InlineData("send.interval_seconds", "0")]
    [InlineData("send.interval_seconds", "1.5")]
    public void Blind_box_tally_and_send_queue_settings_validate_and_reject_saving_invalid_input(string name, string value)
    {
        Settings.Load(harness.DataDirectory);
        var before = File.ReadAllBytes(SettingsPath);
        Assert.NotNull(Settings.Validate(name, value));
        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { [name] = value }));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Missing_blind_box_tally_and_send_queue_settings_use_defaults_and_keywords_round_trip_as_Yaml_text()
    {
        Write("log:\n  level: Debug\n");
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Equal("今日盲盒", settings.BlindBoxDailyKeyword.Value);
        Assert.Equal("本月盲盒", settings.BlindBoxMonthlyKeyword.Value);
        Assert.Equal("今年盲盒", settings.BlindBoxYearlyKeyword.Value);
        Assert.Equal(1, settings.SendIntervalSeconds.Value);
        Settings.Save(harness.DataDirectory, new Dictionary<string, string>
        {
            ["blind_box.daily_keyword"] = "#盲盒: 查询", ["blind_box.monthly_keyword"] = "#本月: 查询"
        });
        Assert.Equal("#盲盒: 查询", Settings.Load(harness.DataDirectory).BlindBoxDailyKeyword.Value);
        Assert.Equal("#本月: 查询", Settings.Load(harness.DataDirectory).BlindBoxMonthlyKeyword.Value);
        Assert.Null(Settings.Load(harness.DataDirectory).Error);
    }

    [Fact]
    public void Blind_box_template_and_boolean_save_use_single_line_values()
    {
        Assert.Null(Settings.Load(harness.DataDirectory).Error);
        var template = File.ReadAllText(SettingsPath);
        Assert.Contains("下次观看直播间时生效", template);
        Assert.Contains("blind_box:\n  # true 或 false，false 时既不记录，也不回复和播报\n  enabled: true", template.ReplaceLineEndings("\n"));
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = "false" });
        Assert.Equal(BlindBoxOff(template), File.ReadAllText(SettingsPath));
        Assert.Empty(Settings.Load(harness.DataDirectory).UnknownKeys);
        Assert.False(Settings.Load(harness.DataDirectory).BlindBoxEnabled.Value);
    }

    // blind_box.enabled is the template's first switch that is on; auto_update.enabled further down is on too.
    private static string BlindBoxOff(string text) => new Regex("enabled: true").Replace(text, "enabled: false", 1);

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("|-\n    false", false)]
    public void Blind_box_boolean_loads_and_marks_multiline_as_read_only(string scalar, bool expected)
    {
        Write($"blind_box:\n  enabled: {scalar}\n");
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(expected, settings.BlindBoxEnabled.Value);
        Assert.Equal(!scalar.Contains('\n'), settings.BlindBoxEnabled.CanEdit);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("FALSE")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("[]")]
    public void Blind_box_boolean_rejects_everything_except_true_and_false(string scalar)
    {
        Write($"blind_box:\n  enabled: {scalar}\n");
        var before = File.ReadAllText(SettingsPath);
        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal("blind_box.enabled", error.SettingName);
        Assert.Equal(2, error.LineNumber);
        Assert.NotNull(Settings.Validate("blind_box.enabled", scalar));
        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { ["blind_box.enabled"] = scalar }));
        Assert.Equal(before, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("utf-8", "\n", 1)]
    [InlineData("utf-8", "\n", 2)]
    [InlineData("utf-16", "\r\n", 1)]
    [InlineData("utf-16", "\r\n", 2)]
    [InlineData("utf-16BE", "\r\n", 0)]
    public void Load_appends_commented_missing_groups_without_changing_existing_bytes(string encodingName, string newline, int finalNewlines)
    {
        Directory.CreateDirectory(harness.DataDirectory);
        var encoding = System.Text.Encoding.GetEncoding(encodingName);
        var yaml = "# 保留 🌙\nlog:\n  level: Debug # 级别".ReplaceLineEndings(newline) + string.Concat(Enumerable.Repeat(newline, finalNewlines));
        File.WriteAllText(SettingsPath, yaml, encoding);
        var before = File.ReadAllBytes(SettingsPath);

        var settings = Settings.Load(harness.DataDirectory);

        Assert.Null(settings.Error);
        Assert.True(settings.BlindBoxEnabled.Value);
        Assert.Equal(LogLevel.Debug, settings.LogLevel.Value);
        var after = File.ReadAllBytes(SettingsPath);
        Assert.Equal(before, after[..before.Length]);
        var appended = encoding.GetString(after[before.Length..]).ReplaceLineEndings("\n");
        // Exactly one blank line, as between the template's groups, counting one the file already ends with.
        Assert.StartsWith(new string('\n', Math.Max(0, 2 - finalNewlines)) + "# ", appended);
        Assert.Contains("# 盲盒统计，下次观看直播间时生效\nblind_box:\n  # true 或 false，false 时既不记录，也不回复和播报\n  enabled: true\n  # 今日查询关键字，非空文字\n  daily_keyword: 今日盲盒\n", appended);
        Assert.Contains("# 发送队列，下次观看直播间时生效\nsend:\n  # 两次发送之间的秒数，必须为正整数\n  interval_seconds: 1\n", appended);
        Assert.DoesNotContain("retention_days", appended);
        Assert.Null(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal(after, File.ReadAllBytes(SettingsPath));

        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = "false" });
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(
            BlindBoxOff(encoding.GetString(after[encoding.GetPreamble().Length..])))),
            File.ReadAllBytes(SettingsPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("# 只有注释\n")]
    [InlineData("  log:\n    level: Debug\n")]
    [InlineData("!!map\n  log:\n    level: Debug\n")]
    [InlineData("?\n  log\n: {level: Debug}\n")]
    [InlineData("log:\u2028  level: Debug\u2028")]
    public void Load_appends_groups_to_empty_or_indented_documents(string yaml)
    {
        Write(yaml);
        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        var after = File.ReadAllText(SettingsPath);
        Assert.StartsWith(yaml, after);
        Assert.Contains("blind_box:", after);
        Assert.Contains("send:", after);
        Assert.True(settings.BlindBoxEnabled.CanEdit);
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = "false" });
        Assert.Equal(BlindBoxOff(after), File.ReadAllText(SettingsPath));
    }

    [Fact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void Settings_still_load_when_missing_groups_cannot_be_appended()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Unix file modes only.");
        Write("log:\n  level: Debug\n");
        var before = File.ReadAllBytes(SettingsPath);
        var directoryMode = File.GetUnixFileMode(harness.DataDirectory);
        // A read-only data directory: the appended groups are a convenience, so watching and file logging carry on.
        File.SetUnixFileMode(harness.DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var settings = Settings.Load(harness.DataDirectory, harness.Logger);
            Assert.Null(settings.Error);
            Assert.Equal(LogLevel.Debug, settings.LogLevel.Value);
            Assert.True(settings.BlindBoxEnabled.Value);
        }
        finally { File.SetUnixFileMode(harness.DataDirectory, directoryMode); }
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Contains(harness.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("未能补写"));
    }

    [Theory]
    [InlineData("{log: {level: Debug}}\n")]
    [InlineData("log:\n  level: Debug\n...\n# 末尾\n")]
    public void Load_leaves_documents_that_cannot_accept_appended_groups_untouched(string yaml)
    {
        Write(yaml);
        var before = File.ReadAllBytes(SettingsPath);
        Assert.Null(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Save_changes_only_the_requested_scalar_bytes()
    {
        const string yaml = "# 我的注释 🌙\r\nlog:\r\n  retention_days: '7'  # 一周\r\n  level: Debug\r\n\r\nfuture: {option: true}\r\n";
        Write(yaml);
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["log.retention_days"] = "14" });

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(
            "# 我的注释 🌙\r\nlog:\r\n  retention_days: 14  # 一周\r\n  level: Debug\r\n\r\nfuture: {option: true}\r\n"), File.ReadAllBytes(SettingsPath));
        Assert.Equal(14, Settings.Load(harness.DataDirectory).LogRetentionDays.Value);
        Assert.Equal([SettingsPath], Directory.GetFiles(harness.DataDirectory));
    }

    [Theory]
    [InlineData("log:\n  level: Debug\n\nfuture: true\n", "log:\n  level: Debug\n  retention_days: 14\n\nfuture: true\n")]
    [InlineData("log:\n  level: Debug # 级别\n\n# 其他分组\nfuture: true\n",
        "log:\n  level: Debug # 级别\n  retention_days: 14\n\n# 其他分组\nfuture: true\n")]
    [InlineData("log:\n  level: Debug\n# 末尾\n", "log:\n  level: Debug\n  retention_days: 14\n# 末尾\n")]
    [InlineData("future: true\n...\n...\n", "future: true\nlog:\n  retention_days: 14\n...\n...\n")]
    [InlineData("log:\n    level: Debug", "log:\n    level: Debug\n    retention_days: 14\n")]
    [InlineData("log: # 日志\n  # 我的注释\nfuture: true\n", "log: # 日志\n  # 我的注释\n  retention_days: 14\nfuture: true\n")]
    [InlineData("# 开头\nfuture: true", "# 开头\nfuture: true\nlog:\n  retention_days: 14\n")]
    [InlineData("", "log:\n  retention_days: 14\n")]
    [InlineData("log:\r\n  level: Debug\r\n", "log:\r\n  level: Debug\r\n  retention_days: 14\r\n")]
    [InlineData("---\nfuture: true\n...\n# 末尾\n", "---\nfuture: true\nlog:\n  retention_days: 14\n...\n# 末尾\n")]
    [InlineData("  log:\n  future: true\n", "  log:\n    retention_days: 14\n  future: true\n")]
    [InlineData("  future: true\n", "  future: true\n  log:\n    retention_days: 14\n")]
    [InlineData("log: !!map\n  level: Debug\n", "log: !!map\n  level: Debug\n  retention_days: 14\n")]
    [InlineData("!!map\n  future: true\n", "!!map\n  future: true\n  log:\n    retention_days: 14\n")]
    [InlineData("log:\n  ? future\n  : true\n", "log:\n  ? future\n  : true\n  retention_days: 14\n")]
    [InlineData("? future\n: true\n", "? future\n: true\nlog:\n  retention_days: 14\n")]
    [InlineData("log:\n  ?\n    future\n  : true\n", "log:\n  ?\n    future\n  : true\n  retention_days: 14\n")]
    [InlineData("?\n  future\n: true\n", "?\n  future\n: true\nlog:\n  retention_days: 14\n")]
    [InlineData("log:\n  level: Debug\n?\n  future\n: true\n", "log:\n  level: Debug\n  retention_days: 14\n?\n  future\n: true\n")]
    [InlineData("log:\u0085  level: Debug\u0085future: true\u0085", "log:\u0085  level: Debug\u0085  retention_days: 14\u0085future: true\u0085")]
    [InlineData("log:\u2028  level: Debug\u2028future: true\u2028", "log:\u2028  level: Debug\u2028  retention_days: 14\u2028future: true\u2028")]
    [InlineData("log:\u2029  level: Debug\u2029future: true\u2029", "log:\u2029  level: Debug\u2029  retention_days: 14\u2029future: true\u2029")]
    public void Save_inserts_missing_settings_at_group_end_or_missing_groups_at_document_end(string yaml, string expected)
    {
        Write(yaml);

        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["log.retention_days"] = "14" });

        Assert.Equal(expected, File.ReadAllText(SettingsPath));
        var loaded = Settings.Load(harness.DataDirectory);
        Assert.Null(loaded.Error);
        Assert.Equal(14, loaded.LogRetentionDays.Value);
    }

    [Theory]
    [InlineData("log:\n  retention_days: 7\n", "log.retention_days", "0")]
    [InlineData("log:\n  retention_days: 7\n", "log.retention_days", "1.5")]
    [InlineData("log:\n  retention_days: 7\n", "log.retention_days", "2147483648")]
    [InlineData("log:\n  level: Debug\n", "log.level", "None")]
    [InlineData("log:\n  level: Debug\n", "log.level", "Debug\nfuture: yes")]
    [InlineData("log:\n  level: @bad\n", "log.level", "Warning")]
    [InlineData("log:\n  retention_days: 0\n", "log.level", "Warning")]
    [InlineData("log:\n  level: |-\n    Debug\n", "log.level", "Warning")]
    [InlineData("log: {level: Debug}\n", "log.level", "Warning")]
    [InlineData("log:\n  level: &level Debug\n", "log.level", "Warning")]
    [InlineData("log:\n  level: Debug\n", "future.option", "yes")]
    public void Save_rejects_invalid_input_invalid_files_and_read_only_settings_without_writing(string yaml, string name, string value)
    {
        Write(yaml);
        var before = File.ReadAllBytes(SettingsPath);

        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { [name] = value }));

        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
        Assert.Equal([SettingsPath], Directory.GetFiles(harness.DataDirectory));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void Save_preserves_encoding_bom_and_untouched_values(string encodingName)
    {
        Directory.CreateDirectory(harness.DataDirectory);
        var encoding = System.Text.Encoding.GetEncoding(encodingName);
        File.WriteAllText(SettingsPath, "# 🌙\r\nlog:\r\n  level: 'Debug' # 注释\r\n  retention_days: 7\r\n", encoding);

        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["log.retention_days"] = "14" });

        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(
            "# 🌙\r\nlog:\r\n  level: 'Debug' # 注释\r\n  retention_days: 14\r\n")), File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Save_can_insert_both_missing_settings_and_leave_a_multiline_sibling_unchanged()
    {
        Write("# 设置\n");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string>
        {
            ["log.retention_days"] = "7", ["log.level"] = "Warning"
        });
        Assert.Equal("# 设置\nlog:\n  retention_days: 7\n  level: Warning\n", File.ReadAllText(SettingsPath));

        Write("log:\n  retention_days: |-\n    7\n  level: Debug\n");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["log.level"] = "Warning" });
        Assert.Equal("log:\n  retention_days: |-\n    7\n  level: Warning\n", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Save_finds_a_tagged_key_by_name_instead_of_adding_a_second_line()
    {
        Write("log:\n  !!str retention_days: 7\n");
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["log.retention_days"] = "14" });
        Assert.Equal("log:\n  !!str retention_days: 14\n", File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("log:\n  !!str level: Debug\n  level: Warning\n", "log.level")]
    [InlineData("log:\n  level: Warning\n  !!str level: Debug\n", "log.level")]
    [InlineData("log:\n  !!str retention_days: 7\n  retention_days: 14\n", "log.retention_days")]
    public void Duplicate_setting_names_are_rejected_even_when_the_yaml_tags_differ(string yaml, string name)
    {
        Write(yaml);
        var before = File.ReadAllBytes(SettingsPath);

        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal(name, error.SettingName);
        Assert.Equal(3, error.LineNumber);
        Assert.Contains("重复", error.Message);
        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { ["log.level"] = "Error" }));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Theory]
    [InlineData("!!str log:\n  level: Debug\nlog:\n  retention_days: 7\n")]
    [InlineData("log:\n  level: Debug\n!!str log:\n  retention_days: 7\n")]
    [InlineData("log: # 默认值\n# 注释\n!!str log:\n  level: Debug\n")]
    public void Duplicate_known_groups_are_rejected_even_with_different_or_missing_settings(string yaml)
    {
        Write(yaml);
        var before = File.ReadAllBytes(SettingsPath);

        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);
        Assert.Equal("log", error.SettingName);
        Assert.Equal(3, error.LineNumber);
        Assert.Contains("重复", error.Message);
        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { ["log.level"] = "Error" }));
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Non_utf8_file_still_loads_but_is_read_only_and_save_does_not_change_its_bytes()
    {
        Write("log:\n  level: Debug\n# ");
        var bytes = File.ReadAllBytes(SettingsPath).Concat(new byte[] { 0xff }).ToArray();
        File.WriteAllBytes(SettingsPath, bytes);

        var settings = Settings.Load(harness.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(LogLevel.Debug, settings.LogLevel.Value);
        Assert.False(settings.LogLevel.CanEdit);
        Assert.False(settings.LogRetentionDays.CanEdit);
        Assert.Equal(bytes, File.ReadAllBytes(SettingsPath));
        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { ["log.level"] = "Warning" }));
        Assert.Equal(bytes, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Missing_file_creates_commented_template_with_active_defaults()
    {
        var settings = Settings.Load(harness.DataDirectory);

        Assert.Null(settings.Error);
        Assert.Equal(30, settings.LogRetentionDays.Value);
        Assert.Equal(LogLevel.Information, settings.LogLevel.Value);
        Assert.True(settings.LogRetentionDays.CanEdit);
        Assert.True(settings.LogLevel.CanEdit);
        Assert.Empty(settings.UnknownKeys);
        var template = File.ReadAllText(SettingsPath);
        Assert.Contains("# 日志", template);
        Assert.Contains("\n  retention_days: 30", template);
        Assert.Contains("\n  level: Information", template);
        Assert.Equal(settings.LogRetentionDays, Settings.Load(harness.DataDirectory).LogRetentionDays);
        Assert.Equal(template, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("Information", LogLevel.Information)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("Critical", LogLevel.Critical)]
    public void Reads_known_values_without_rewriting_the_file(string level, LogLevel expected)
    {
        var yaml = $"auto_update:\nweb_service:\nentry_animation:\ngift_animation:\ndesktop_pet:\nentry_animation_viewers:\n# 我的注释\nlog:\n  retention_days: 7 # 一周\n  level: {level}\nblind_box:\nsend:\nwelcome_danmaku:\n";
        Write(yaml);
        var before = File.ReadAllBytes(SettingsPath);

        var settings = Settings.Load(harness.DataDirectory);

        Assert.Null(settings.Error);
        Assert.Equal(7, settings.LogRetentionDays.Value);
        Assert.Equal(expected, settings.LogLevel.Value);
        Assert.True(settings.LogRetentionDays.CanEdit);
        Assert.True(settings.LogLevel.CanEdit);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Theory]
    [InlineData("log:\n  retention_days: 0", "log.retention_days", 2)]
    [InlineData("log:\n  retention_days: -1", "log.retention_days", 2)]
    [InlineData("log:\n  retention_days: 1.5", "log.retention_days", 2)]
    [InlineData("log:\n  level: Verbose", "log.level", 2)]
    [InlineData("log:\n  level: None", "log.level", 2)]
    [InlineData("log:\n  level: 2", "log.level", 2)]
    [InlineData("log:\n  level:", "log.level", 2)]
    [InlineData("log: false", "log", 1)]
    public void Invalid_values_report_file_line_and_setting(string yaml, string name, long line)
    {
        Write(yaml);
        var before = File.ReadAllBytes(SettingsPath);

        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);

        Assert.Equal(SettingsPath, error.FilePath);
        Assert.Equal(line, error.LineNumber);
        Assert.Equal(name, error.SettingName);
        Assert.Contains(SettingsPath, error.Message);
        Assert.Contains($"第 {line} 行", error.Message);
        Assert.Contains(name, error.Message);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Malformed_yaml_reports_the_parser_line()
    {
        Write("log:\n  level: @bad\n");
        var before = File.ReadAllBytes(SettingsPath);

        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);

        Assert.Equal(SettingsPath, error.FilePath);
        Assert.Equal(2, error.LineNumber);
        Assert.Equal(before, File.ReadAllBytes(SettingsPath));
    }

    [Fact]
    public void Unknown_keys_are_listed_and_warned_without_discarding_valid_settings()
    {
        Write("log:\n  retention_days: 7\n  leve: Debug\nfuture:\n  option: true\n");

        var settings = Settings.Load(harness.DataDirectory, harness.Logger);

        Assert.Null(settings.Error);
        Assert.Equal(7, settings.LogRetentionDays.Value);
        Assert.Equal(LogLevel.Information, settings.LogLevel.Value);
        Assert.Equal(["log.leve", "future.option"], settings.UnknownKeys);
        var warning = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("log.leve", warning.Message);
        Assert.Contains("future.option", warning.Message);
    }

    [Fact]
    public void Old_blind_box_keyword_is_warned_as_unknown_and_new_keywords_keep_their_defaults()
    {
        Write("blind_box:\n  keyword: 旧查询\n");
        var settings = Settings.Load(harness.DataDirectory, harness.Logger);
        Assert.Null(settings.Error);
        Assert.Equal("今日盲盒", settings.BlindBoxDailyKeyword.Value);
        Assert.Equal("本月盲盒", settings.BlindBoxMonthlyKeyword.Value);
        Assert.Equal(["blind_box.keyword"], settings.UnknownKeys);
        Assert.Contains(harness.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("blind_box.keyword"));
    }

    [Theory]
    [InlineData("future: true\nlog: false")]
    [InlineData("log: false\nfuture: true")]
    public void Invalid_log_group_preserves_unknown_key_diagnostics(string yaml)
    {
        Write(yaml);

        var settings = Settings.Load(harness.DataDirectory, harness.Logger);

        Assert.Equal("log", Assert.IsType<SettingsException>(settings.Error).SettingName);
        Assert.Equal(["future"], settings.UnknownKeys);
        var warning = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("future", warning.Message);
    }

    [Theory]
    [InlineData("", 30, true, false)]
    [InlineData("log:\n  level: Debug", 30, true, false)]
    [InlineData("log:\n  # retention_days: 7\n  # level: Debug\n", 30, true, false)]
    [InlineData("log:\n  retention_days: |-\n    7", 7, false, false)]
    [InlineData("log:\n  retention_days: >-\n    7", 7, false, false)]
    [InlineData("log:\n  retention_days:\n    7", 7, false, false)]
    [InlineData("log:\n  retention_days: {days: 7}", 30, false, true)]
    [InlineData("log:\n  retention_days:\n    days: 7", 30, false, true)]
    [InlineData("log: {retention_days: 7, level: Debug}", 7, false, false)]
    [InlineData("log: {}", 30, false, false)]
    [InlineData("{future: true}", 30, false, false)]
    [InlineData("log:\n  retention_days: &days 7", 7, false, false)]
    [InlineData("log: &logging\n  retention_days: 7", 7, false, false)]
    [InlineData("log: &logging\n", 30, false, false)]
    public void Missing_settings_keep_defaults_and_non_line_editable_values_are_marked(
        string yaml, int days, bool canEdit, bool hasError)
    {
        Write(yaml);

        var settings = Settings.Load(harness.DataDirectory);

        Assert.Equal(days, settings.LogRetentionDays.Value);
        Assert.Equal(canEdit, settings.LogRetentionDays.CanEdit);
        Assert.Equal(hasError, settings.Error is not null);
    }

    [Fact]
    public async Task Concurrent_first_loads_all_read_a_complete_template()
    {
        var loaded = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(
            () => Settings.Load(harness.DataDirectory), harness.Stop.Token)));

        Assert.All(loaded, settings =>
        {
            Assert.Null(settings.Error);
            Assert.Equal(30, settings.LogRetentionDays.Value);
            Assert.Equal(LogLevel.Information, settings.LogLevel.Value);
        });
        Assert.Equal([SettingsPath], Directory.GetFiles(harness.DataDirectory));
    }

    [Fact]
    public async Task Watch_rejects_invalid_settings_before_network_or_retry_and_rereads_on_the_next_connection()
    {
        Write("log:\n  retention_days: 0\n");
        await using (var updates = harness.Client.WatchAsync(6, harness.Stop.Token).GetAsyncEnumerator(harness.Stop.Token))
        {
            var error = await Assert.ThrowsAsync<SettingsException>(async () => await updates.MoveNextAsync());
            Assert.Equal(SettingsPath, error.FilePath);
            Assert.Equal(2, error.LineNumber);
            Assert.Empty(harness.Http.Requests);
        }

        Write("log:\n  retention_days: 7\n  extra: true\n");
        await using var next = harness.Client.WatchAsync(6, harness.Stop.Token).GetAsyncEnumerator(harness.Stop.Token);
        Assert.True(await next.MoveNextAsync());
        Assert.IsType<WatchQrCode>(next.Current);
        Assert.Contains(harness.Logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("log.extra"));
    }

    [Fact]
    public async Task Reconnect_does_not_reread_settings_but_a_new_room_connection_does()
    {
        var attempts = 0;
        using var client = harness.ClientSharingData((uri, token) => ++attempts == 1
            ? throw new System.Net.WebSockets.WebSocketException("disconnect") : harness.Server.ConnectAsync(uri, token));
        await harness.LoginAsync();
        await using (var updates = client.WatchAsync(6, harness.Stop.Token).GetAsyncEnumerator(harness.Stop.Token))
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Connecting>(updates.Current);
            Write("log:\n  level: bad\n");
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await harness.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
        }
        await using var next = client.WatchAsync(6, harness.Stop.Token).GetAsyncEnumerator(harness.Stop.Token);
        await Assert.ThrowsAsync<SettingsException>(async () => await next.MoveNextAsync());
    }

    [Theory]
    [InlineData("log:\n  retention_days: '7'\n  level: Debug\n", "7", "Debug")]
    [InlineData("log:\n  retention_days: 007 # 一周\n", " 7 ", "Information")]
    [InlineData("log:\n  level: \"Debug\"\n", "30", "Debug")]
    public void Save_leaves_values_equal_to_the_current_ones_untouched(string yaml, string days, string level)
    {
        Write(yaml);

        Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { ["log.retention_days"] = days, ["log.level"] = level });

        Assert.Equal(yaml, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Save_keeps_an_external_edit_to_a_value_unchanged_since_the_snapshot()
    {
        Write("log:\n  level: Information\n");
        var snapshot = Settings.Load(harness.DataDirectory);
        Write("# 外部修改\nlog:\n  level: Warning\n");

        Settings.Save(harness.DataDirectory,
            new Dictionary<string, string> { ["log.retention_days"] = "7", ["log.level"] = "Information" }, snapshot);

        Assert.Equal("# 外部修改\nlog:\n  level: Warning\n  retention_days: 7\n", File.ReadAllText(SettingsPath));
    }

    // Isolated definitions exercise text and list editing independently of the shipped settings' validation.
    private static readonly Settings.Definition<string> Greeting = Settings.Definition.Text("welcome_danmaku.greeting", "");

    [Theory]
    [InlineData("{观众} 欢迎", "'{观众} 欢迎'")]
    [InlineData("欢迎 # 新朋友", "'欢迎 # 新朋友'")]
    [InlineData(" 前后空格 ", "' 前后空格 '")]
    [InlineData("it's", "it's")]
    [InlineData("", "''")]
    [InlineData("你好", "你好")]
    public void Text_settings_are_quoted_only_when_yaml_needs_it_and_read_back_as_typed(string text, string written)
    {
        Write("# 欢迎\nwelcome_danmaku:\n  greeting: 旧的  # 行尾\nlog:\n  level: Debug\n");

        Settings.Save(harness.DataDirectory, [Greeting], new Dictionary<string, string> { ["welcome_danmaku.greeting"] = text });

        Assert.Equal($"# 欢迎\nwelcome_danmaku:\n  greeting: {written}  # 行尾\nlog:\n  level: Debug\n", File.ReadAllText(SettingsPath));
        var loaded = Settings.Load(harness.DataDirectory, [Greeting]);
        Assert.Null(loaded.Error);
        Assert.Equal(new Setting<string>("welcome_danmaku.greeting", text), loaded.Get(Greeting));
    }

    private static readonly Settings.Definition<IReadOnlyList<string>> Viewers = Settings.Definition.TextList("welcome_danmaku.viewers", []);

    [Fact]
    public void One_line_lists_are_rewritten_in_place_while_block_lists_stay_read_only()
    {
        Write("welcome_danmaku:\n  viewers: [甲, '乙']  # 名单\n");
        var loaded = Settings.Load(harness.DataDirectory, [Viewers]);
        Assert.Equal(["甲", "乙"], loaded.Get(Viewers).Value);
        Assert.True(loaded.Get(Viewers).CanEdit);

        Settings.Save(harness.DataDirectory, [Viewers], new Dictionary<string, string> { ["welcome_danmaku.viewers"] = "[甲, '{丙}', 'a, b']" });

        Assert.Equal("welcome_danmaku:\n  viewers: [\"甲\", \"{丙}\", \"a, b\"]  # 名单\n", File.ReadAllText(SettingsPath));
        Assert.Equal(["甲", "{丙}", "a, b"], Settings.Load(harness.DataDirectory, [Viewers]).Get(Viewers).Value);

        Write("welcome_danmaku:\n  viewers:\n    - 甲\n");
        loaded = Settings.Load(harness.DataDirectory, [Viewers]);
        Assert.Null(loaded.Error);
        Assert.Equal(["甲"], loaded.Get(Viewers).Value);
        Assert.False(loaded.Get(Viewers).CanEdit);
        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory, [Viewers],
            new Dictionary<string, string> { ["welcome_danmaku.viewers"] = "[乙]" }));
        Assert.Equal("welcome_danmaku:\n  viewers:\n    - 甲\n", File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("welcome_danmaku.viewers", "甲")]
    [InlineData("welcome_danmaku.viewers", "[[甲]]")]
    [InlineData("welcome_danmaku.viewers", "[甲")]
    [InlineData("welcome_danmaku.greeting", "两\n行")]
    [InlineData("welcome_danmaku.viewers", "[[a],\n  b: 7\n    c]")]
    public void Invalid_text_or_list_input_is_rejected_without_writing(string name, string value)
    {
        const string yaml = "welcome_danmaku:\n  greeting: 你好\n  viewers: [甲]\n";
        Write(yaml);

        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory, [Greeting, Viewers],
            new Dictionary<string, string> { [name] = value }));

        Assert.Equal(yaml, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("welcome_danmaku:\n  greeting: 你好\n", "welcome_danmaku.viewers", "[甲, 乙]", "welcome_danmaku:\n  greeting: 你好\n  viewers: [\"甲\", \"乙\"]\n")]
    [InlineData("welcome_danmaku:\n  viewers: [甲]  # 名单\nlog: {}\n", "welcome_danmaku.greeting", "你好",
        "welcome_danmaku:\n  viewers: [甲]  # 名单\n  greeting: 你好\nlog: {}\n")]
    public void Missing_text_or_list_settings_are_added_after_the_groups_last_line(string yaml, string name, string value, string expected)
    {
        Write(yaml);

        Settings.Save(harness.DataDirectory, [Greeting, Viewers], new Dictionary<string, string> { [name] = value });

        Assert.Equal(expected, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("log:\n  level: Debug\n  extra: [[a],\n    b]\n", "log:\n  level: Debug\n  extra: [[a],\n    b]\n  retention_days: 7\n")]
    [InlineData("log:\n  level: Debug\n  extra: {a: 1,\n    b: 2}\n", "log:\n  level: Debug\n  extra: {a: 1,\n    b: 2}\n  retention_days: 7\n")]
    [InlineData("log:\n  level: Debug\n  extra: [a, ']']\n", "log:\n  level: Debug\n  extra: [a, ']']\n  retention_days: 7\n")]
    public void A_missing_setting_goes_after_a_flow_collection_that_ends_the_group(string yaml, string expected)
    {
        Write(yaml);

        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["log.retention_days"] = "7" });

        Assert.Equal(expected, File.ReadAllText(SettingsPath));
        Assert.Equal(7, Settings.Load(harness.DataDirectory).LogRetentionDays.Value);
    }

    [Fact]
    public void Yaml_that_the_parser_rejects_outside_its_syntax_errors_is_still_a_settings_error()
    {
        Write("log:\n  level: Debug\n  extra: [[a],\n  retention_days: 7\n    b]\n");

        var loaded = Settings.Load(harness.DataDirectory);

        Assert.Equal("YAML", loaded.Error?.SettingName);
    }

    [Fact]
    public void A_flow_list_spread_over_lines_is_read_only()
    {
        Write("welcome_danmaku:\n  viewers: [甲,\n    乙]\n");

        var loaded = Settings.Load(harness.DataDirectory, [Viewers]);

        Assert.Equal(["甲", "乙"], loaded.Get(Viewers).Value);
        Assert.False(loaded.Get(Viewers).CanEdit);
    }

    private void Write(string yaml)
    {
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(SettingsPath, yaml);
    }

    public ValueTask DisposeAsync() => harness.DisposeAsync();
}
