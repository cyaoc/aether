using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class WelcomeSettingsTests : IAsyncDisposable
{
    private readonly WatchHarness h = new();

    [Fact]
    public void Defaults_are_written_and_missing_group_is_appended_without_changing_existing_bytes()
    {
        var settings = Settings.Load(h.DataDirectory);
        Assert.Null(settings.Error);
        Assert.False(settings.WelcomeEnabled.Value);
        Assert.Equal("欢迎{大航海}{昵称}", settings.WelcomeText.Value);
        Assert.Equal(20, settings.WelcomeMaxLength.Value);
        Assert.Empty(settings.WelcomeUids.Value);
        Assert.Equal(0, settings.WelcomeFanClubLevel.Value);
        Assert.Equal("舰长", settings.WelcomeGuard.Value);
        Assert.Equal(0, settings.WelcomeWealthLevel.Value);
        var template = File.ReadAllText(Settings.FilePath(h.DataDirectory)).ReplaceLineEndings("\n");
        var section = template[template.IndexOf("# 进场欢迎", StringComparison.Ordinal)..];
        Assert.Contains("用 {{ 和 }}", section);
        Assert.Contains("以 { 开头时要加引号", section);
        Assert.Contains("# 指定观众的 UID，例如 [12345, 67890]；[] 表示不按 UID 欢迎\n  uids: []", section);
        Assert.Contains("# 本直播间粉丝团等级达到多少就欢迎，0 表示不按粉丝团等级欢迎\n  fan_club_level: 0", section);
        Assert.Contains("# 荣耀等级达到多少就欢迎，0 表示不按荣耀等级欢迎\n  wealth_level: 0", section);
        Assert.Empty(settings.UnknownKeys);
        const string before = "# 保留\r\nlog:\r\nblind_box:\r\nsend:\r\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), before);
        Assert.Null(Settings.Load(h.DataDirectory).Error);
        var after = File.ReadAllText(Settings.FilePath(h.DataDirectory));
        Assert.Equal(before + "\r\n" + section.ReplaceLineEndings("\r\n"), after);
        Settings.Load(h.DataDirectory);
        Assert.Equal(after, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        File.WriteAllText(Settings.FilePath(h.DataDirectory), before + "welcome:\n");
        var missing = Settings.Load(h.DataDirectory);
        Assert.Equal(settings.WelcomeText.Value, missing.WelcomeText.Value);
        Assert.Equal(settings.WelcomeMaxLength.Value, missing.WelcomeMaxLength.Value);
        Assert.Empty(missing.WelcomeUids.Value);
        Assert.Equal(0, missing.WelcomeFanClubLevel.Value);
        Assert.Equal(settings.WelcomeGuard.Value, missing.WelcomeGuard.Value);
        Assert.Equal(0, missing.WelcomeWealthLevel.Value);
        Assert.Equal(settings.WelcomeEnabled.Value, missing.WelcomeEnabled.Value);
    }

    [Fact]
    public void Welcome_conditions_save_on_their_lines_and_preserve_comments_and_other_settings()
    {
        Settings.Load(h.DataDirectory);
        const string before = "# 保留\r\nlog:\r\nblind_box:\r\nsend:\r\nwelcome:\r\n  uids: [42]  # 老朋友\r\n  fan_club_level: 0 # 老粉\r\n  guard: 舰长\r\n  wealth_level: 0 # 荣耀\r\nfuture: 保留\r\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), before);
        var changes = new Dictionary<string, string>
        {
            ["welcome.uids"] = "[2147483648, 9223372036854775807]",
            ["welcome.fan_club_level"] = "21", ["welcome.wealth_level"] = "45"
        };
        Settings.Save(h.DataDirectory, changes);
        var expected = before.Replace("[42]", "[2147483648, 9223372036854775807]")
            .Replace("fan_club_level: 0", "fan_club_level: 21").Replace("wealth_level: 0", "wealth_level: 45");
        Assert.Equal(expected, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        var loaded = Settings.Load(h.DataDirectory);
        Assert.Null(loaded.Error);
        Assert.Equal([2147483648L, long.MaxValue], loaded.WelcomeUids.Value);
        Assert.True(loaded.WelcomeUids.CanEdit);
        Assert.Equal(21, loaded.WelcomeFanClubLevel.Value);
        Assert.Equal(45, loaded.WelcomeWealthLevel.Value);
        // Equal numeric values keep the original spelling, as scalar settings do.
        changes["welcome.uids"] = "[ 02147483648, +9223372036854775807 ]";
        Settings.Save(h.DataDirectory, changes);
        Assert.Equal(expected, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.uids"] = "[]" });
        Assert.Empty(Settings.Load(h.DataDirectory).WelcomeUids.Value);
    }

    [Fact]
    public void Save_inserts_missing_welcome_conditions_after_existing_settings()
    {
        Settings.Load(h.DataDirectory);
        const string before = "welcome:\n  guard: 提督 # 保留\nfuture: 保留\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), before);
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.uids"] = "[12345, 67890]", ["welcome.fan_club_level"] = "21", ["welcome.wealth_level"] = "45"
        });
        Assert.Equal("welcome:\n  guard: 提督 # 保留\n  uids: [12345, 67890]\n  fan_club_level: 21\n  wealth_level: 45\nfuture: 保留\n",
            File.ReadAllText(Settings.FilePath(h.DataDirectory)));
    }

    [Theory]
    [InlineData("uids", "12345")]
    [InlineData("uids", "'[12345]'")]
    [InlineData("uids", "[abc]")]
    [InlineData("uids", "[0]")]
    [InlineData("uids", "[-1]")]
    [InlineData("uids", "[1.5]")]
    [InlineData("uids", "[9223372036854775808]")]
    [InlineData("uids", "[[12345]]")]
    [InlineData("uids", "[true]")]
    [InlineData("uids", "{uid: 12345}")]
    [InlineData("uids", "\n    - 12345")]
    [InlineData("uids", "[12345,\n    67890]")]
    [InlineData("uids", "[12345\n  ]")]
    [InlineData("uids", "[\n  ]")]
    [InlineData("uids", "\n    [12345]")]
    [InlineData("fan_club_level", "-1")]
    [InlineData("fan_club_level", "1.5")]
    [InlineData("wealth_level", "-1")]
    [InlineData("wealth_level", "[]")]
    public void Invalid_welcome_conditions_report_their_line_and_leave_the_file_unchanged(string key, string value)
    {
        Settings.Load(h.DataDirectory);
        var path = Settings.FilePath(h.DataDirectory);
        var yaml = $"# 保留\nwelcome:\n  {key}: {value}\n";
        File.WriteAllText(path, yaml);
        var error = Assert.IsType<SettingsException>(Settings.Load(h.DataDirectory).Error);
        Assert.Equal("welcome." + key, error.SettingName);
        Assert.Equal(3, error.LineNumber);
        Assert.Contains(key == "uids" ? "单行数字列表" : "非负整数", error.Message);
        Assert.Equal(yaml, File.ReadAllText(path));

        const string valid = "welcome:\n  uids: []\n  fan_club_level: 0\n  wealth_level: 0\n";
        File.WriteAllText(path, valid);
        var saveError = Assert.Throws<SettingsException>(() => Settings.Save(h.DataDirectory,
            new Dictionary<string, string> { ["welcome." + key] = value }));
        Assert.Equal("welcome." + key, saveError.SettingName);
        Assert.Equal(1, saveError.LineNumber); // Invalid form input has no source line yet.
        Assert.Equal(valid, File.ReadAllText(path));
    }

    [Fact]
    public void A_single_line_uid_list_in_a_flow_group_loads_but_cannot_be_line_edited()
    {
        Settings.Load(h.DataDirectory);
        const string yaml = "log:\nblind_box:\nsend:\nwelcome: {uids: [12345]}\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), yaml);
        var loaded = Settings.Load(h.DataDirectory);
        Assert.Null(loaded.Error);
        Assert.Equal([12345L], loaded.WelcomeUids.Value);
        Assert.False(loaded.WelcomeUids.CanEdit);
        Assert.Throws<SettingsException>(() => Settings.Save(h.DataDirectory,
            new Dictionary<string, string> { ["welcome.uids"] = "[67890]" }));
        Assert.Equal(yaml, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
    }

    [Fact]
    public void Save_changes_only_requested_lines_and_checks_text_against_the_new_limit()
    {
        Settings.Load(h.DataDirectory);
        const string before = "welcome:\n  enabled: false # 开关\n  text: '欢迎{昵称}' # 正文\n  max_length: 20 # 上限\n  guard: 舰长 # 档位\nfuture: 保留\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), before);
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.text"] = "欢迎{大航海}{昵称}",
            ["welcome.max_length"] = "8", ["welcome.guard"] = "提督"
        });
        var expected = before.Replace("false", "true").Replace("'欢迎{昵称}'", "'欢迎{大航海}{昵称}'")
            .Replace("20 #", "8 #").Replace("舰长", "提督");
        Assert.Equal(expected, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        var loaded = Settings.Load(h.DataDirectory);
        Assert.Null(loaded.Error);
        Assert.True(loaded.WelcomeEnabled.Value);
        Assert.Equal("欢迎{大航海}{昵称}", loaded.WelcomeText.Value);
        Assert.Equal(8, loaded.WelcomeMaxLength.Value);
        Assert.Equal("提督", loaded.WelcomeGuard.Value);
        var beforeInvalid = File.ReadAllText(Settings.FilePath(h.DataDirectory));
        var error = Assert.Throws<SettingsException>(() => Settings.Save(h.DataDirectory,
            new Dictionary<string, string> { ["welcome.max_length"] = "7" }));
        Assert.Equal("welcome.text", error.SettingName);
        Assert.Equal(3, error.LineNumber);
        Assert.Contains("8", error.Message);
        Assert.Contains("7", error.Message);
        Assert.Equal(beforeInvalid, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
    }

    [Fact]
    public void Save_checks_text_against_the_limit_as_the_setting_reads_it()
    {
        Settings.Load(h.DataDirectory);
        // Trim drops a full-width space that int.Parse rejects.
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.max_length"] = "　30" });
        Assert.Equal(30, Settings.Load(h.DataDirectory).WelcomeMaxLength.Value);
        var error = Assert.Throws<SettingsException>(() => Settings.Save(h.DataDirectory,
            new Dictionary<string, string> { ["welcome.max_length"] = "　7" }));
        Assert.Equal("welcome.text", error.SettingName);
    }

    [Fact]
    public void A_default_text_over_the_limit_reports_the_welcome_group_line()
    {
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "welcome:\n  enabled: true\n  max_length: 7\n");
        var error = Assert.IsType<SettingsException>(Settings.Load(h.DataDirectory).Error);
        Assert.Equal("welcome.text", error.SettingName);
        Assert.Equal(1, error.LineNumber);
    }

    [Theory]
    [InlineData("enabled", "yes")]
    [InlineData("enabled", "[]")]
    [InlineData("guard", "船长")]
    [InlineData("guard", "3")]
    [InlineData("guard", "[]")]
    [InlineData("max_length", "0")]
    [InlineData("max_length", "-1")]
    [InlineData("max_length", "1.5")]
    [InlineData("max_length", "2147483648")]
    [InlineData("text", "[]")]
    [InlineData("text", "''")]
    [InlineData("text", "' '")]
    public async Task Invalid_values_refuse_watching_at_their_line(string key, string value)
    {
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), $"welcome:\n  {key}: {value}\n");
        await using var updates = h.Watch();
        var error = await Assert.ThrowsAsync<SettingsException>(() => updates.MoveNextAsync().AsTask());
        Assert.Equal("welcome." + key, error.SettingName);
        Assert.Equal(2, error.LineNumber);
        Assert.Empty(h.Http.Requests);
    }

    [Theory]
    [InlineData("{@}欢迎{大航海}{昵称}", 8)]
    [InlineData("{{{昵称}}}🌙", 4)]
    [InlineData("ABC xyz!~", 9)]
    [InlineData("{大航海}{大航海}", 8)]
    public void Fixed_text_at_the_exact_limit_is_valid(string text, int limit)
    {
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.text"] = text, ["welcome.max_length"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        var settings = Settings.Load(h.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(text, settings.WelcomeText.Value);
    }

    [Theory]
    [InlineData("'{舰长}'", 20, "可用")]
    [InlineData("'欢迎{@}'", 20, "可用")]
    [InlineData("' {@}欢迎'", 20, "可用")]
    [InlineData("'{@}{@}欢迎'", 20, "可用")]
    [InlineData("'欢迎{'", 20, "可用")]
    [InlineData("'欢迎}'", 20, "可用")]
    [InlineData("'欢迎{大航海}{昵称}'", 7, "8")]
    [InlineData("'{@}'", 20, "不能只有")]
    [InlineData("'{@} '", 20, "不能只有")]
    public void Invalid_text_reports_its_line_and_keeps_the_file(string text, int limit, string reason)
    {
        Directory.CreateDirectory(h.DataDirectory);
        var yaml = $"welcome:\n  max_length: {limit}\n  text: {text}\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), yaml);
        var error = Assert.IsType<SettingsException>(Settings.Load(h.DataDirectory).Error);
        Assert.Equal("welcome.text", error.SettingName);
        Assert.Equal(3, error.LineNumber);
        Assert.Contains(reason, error.Message);
        Assert.Equal(yaml, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "welcome:\n  text: '欢迎'\n");
        var beforeSave = File.ReadAllText(Settings.FilePath(h.DataDirectory));
        var saveError = Assert.Throws<SettingsException>(() => Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.text"] = text[1..^1], ["welcome.max_length"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        Assert.Equal("welcome.text", saveError.SettingName);
        Assert.Equal(2, saveError.LineNumber);
        Assert.Equal(beforeSave, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
    }

    public ValueTask DisposeAsync() => h.DisposeAsync();
}
