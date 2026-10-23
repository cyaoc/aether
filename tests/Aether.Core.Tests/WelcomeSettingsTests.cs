using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class WelcomeSettingsTests : IAsyncDisposable
{
    private readonly WatchHarness h = new();

    [Theory]
    [InlineData("{@}欢迎", true)]
    [InlineData("欢迎{@}", true)]
    [InlineData("{{@}", false)]
    [InlineData("}}{@}", true)]
    [InlineData("{{{@}", true)]
    [InlineData("{{{{@}", false)]
    [InlineData("欢迎{昵称}", false)]
    [InlineData("", false)]
    public void Mention_detection_skips_escaped_braces_without_validating_position(string text, bool expected)
    {
        Assert.Equal(expected, Settings.HasWelcomeMention(text));
    }

    [Fact]
    public void Multiple_welcome_texts_are_valid_setting_input()
    {
        Assert.Null(Settings.Validate("welcome.texts", "[\"欢迎{昵称}\", \"{@}欢迎回家\"]"));
    }

    [Theory]
    [InlineData("['欢迎{昵称}']", new[] { "欢迎{昵称}" })]
    [InlineData("['欢迎{昵称}', '{@}欢迎回家', '欢迎{昵称}']", new[] { "欢迎{昵称}", "{@}欢迎回家", "欢迎{昵称}" })]
    public void Single_line_lists_preserve_each_text_and_its_order(string value, string[] expected)
    {
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), $"welcome:\n  texts: {value}\n");
        var settings = Settings.Load(h.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(expected, settings.WelcomeTexts.Value);
        Assert.True(settings.WelcomeTexts.CanEdit);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("['']")]
    [InlineData("['欢迎', ' ']")]
    [InlineData("['欢迎', '{舰长}']")]
    [InlineData("['欢迎', '欢迎{@}']")]
    [InlineData("欢迎{昵称}")]
    [InlineData("[欢迎, [回家]]")]
    public void Invalid_list_or_item_reports_the_setting_and_line(string value)
    {
        Directory.CreateDirectory(h.DataDirectory);
        var yaml = $"# 保留\nwelcome:\n  texts: {value}\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), yaml);
        var error = Assert.IsType<SettingsException>(Settings.Load(h.DataDirectory).Error);
        Assert.Equal("welcome.texts", error.SettingName);
        Assert.Equal(3, error.LineNumber);
        Assert.Equal(yaml, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        Assert.NotNull(Settings.Validate("welcome.texts", value));
    }

    [Theory]
    [InlineData("\n    - 欢迎{昵称}\n    - '{@}欢迎回家'")]
    [InlineData("['欢迎{昵称}',\n    '{@}欢迎回家']")]
    public void Multiline_lists_load_but_cannot_be_edited_in_place(string value)
    {
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), $"welcome:\n  texts: {value}\n");
        var settings = Settings.Load(h.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(["欢迎{昵称}", "{@}欢迎回家"], settings.WelcomeTexts.Value);
        Assert.False(settings.WelcomeTexts.CanEdit);
        var before = File.ReadAllText(Settings.FilePath(h.DataDirectory));
        Assert.Throws<SettingsException>(() => Settings.Save(h.DataDirectory,
            new Dictionary<string, string> { ["welcome.texts"] = "['欢迎']" }));
        Assert.Equal(before, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
    }

    [Fact]
    public void Text_list_writer_quotes_every_item_and_round_trips_special_characters()
    {
        string[] texts = ["他说\"你好\"\\来啦,{{花}}", "{@}欢迎", "{昵称}#你好"];
        var value = Settings.TextListText(texts);
        Assert.Equal("""["他说\"你好\"\\来啦,{{花}}", "{@}欢迎", "{昵称}#你好"]""", value);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.texts"] = value });
        var settings = Settings.Load(h.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(texts, settings.WelcomeTexts.Value);
        Assert.True(settings.WelcomeTexts.CanEdit);
    }

    [Fact]
    public void Saving_the_same_texts_keeps_spelling_but_changing_order_is_a_change()
    {
        Settings.Load(h.DataDirectory);
        var path = Settings.FilePath(h.DataDirectory);
        File.WriteAllText(path, "welcome:\n  texts: ['回来啦', '欢迎'] # 保留\n");
        var settings = Settings.Load(h.DataDirectory);
        var before = File.ReadAllText(path);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.texts"] = "[回来啦, 欢迎]" }, settings);
        Assert.Equal(before, File.ReadAllText(path));
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.texts"] = "[欢迎, 回来啦]" }, settings);
        Assert.Equal(before.Replace("['回来啦', '欢迎']", "[\"欢迎\", \"回来啦\"]"), File.ReadAllText(path));
    }

    [Fact]
    public void Old_text_is_only_an_unknown_key_and_does_not_supply_the_list()
    {
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "welcome:\n  text: '欢迎回来'\n");
        var settings = Settings.Load(h.DataDirectory, h.Logger);
        Assert.Null(settings.Error);
        Assert.Equal(["welcome.text"], settings.UnknownKeys);
        Assert.Equal(["欢迎{大航海}{昵称}"], settings.WelcomeTexts.Value);
        var warning = Assert.Single(h.Logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, warning.Level);
        Assert.Contains("未知设置键：welcome.text", warning.Message);
    }

    [Fact]
    public void Defaults_are_written_and_missing_group_is_appended_without_changing_existing_bytes()
    {
        var settings = Settings.Load(h.DataDirectory);
        Assert.Null(settings.Error);
        Assert.False(settings.WelcomeEnabled.Value);
        Assert.Equal("欢迎{大航海}{昵称}", settings.WelcomeTexts.Value[0]);
        Assert.Empty(settings.WelcomeUids.Value);
        Assert.Equal(0, settings.WelcomeFanClubLevel.Value);
        Assert.Equal("舰长", settings.WelcomeGuard.Value);
        Assert.Equal(0, settings.WelcomeWealthLevel.Value);
        var template = File.ReadAllText(Settings.FilePath(h.DataDirectory)).ReplaceLineEndings("\n");
        var section = template[template.IndexOf("# 进场欢迎", StringComparison.Ordinal)..].Split("\n\n")[0] + "\n";
        Assert.Contains("用 {{ 和 }}", section);
        Assert.Contains("英文、数字、半角符号算 1，其他字符算 2", section);
        Assert.Contains("上限自动读取账号的", section);
        Assert.DoesNotContain("max_length", section);
        Assert.Contains("每次随机发一条", section);
        Assert.Contains("每条加双引号", section);
        Assert.Contains("# 指定观众的 UID，例如 [12345, 67890]；[] 表示不按 UID 欢迎\n  uids: []", section);
        Assert.Contains("# 本直播间粉丝团等级达到多少就欢迎，0 表示不按粉丝团等级欢迎\n  fan_club_level: 0", section);
        Assert.Contains("# 荣耀等级达到多少就欢迎，0 表示不按荣耀等级欢迎\n  wealth_level: 0", section);
        Assert.Empty(settings.UnknownKeys);
        const string before = "web_service:\r\nwelcome_animation:\r\nwelcome_animation_videos:\r\n# 保留\r\nlog:\r\nblind_box:\r\nsend:\r\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), before);
        Assert.Null(Settings.Load(h.DataDirectory).Error);
        var after = File.ReadAllText(Settings.FilePath(h.DataDirectory));
        Assert.Equal(before + "\r\n" + section.ReplaceLineEndings("\r\n"), after);
        Settings.Load(h.DataDirectory);
        Assert.Equal(after, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        File.WriteAllText(Settings.FilePath(h.DataDirectory), before + "welcome:\n");
        var missing = Settings.Load(h.DataDirectory);
        Assert.Equal(settings.WelcomeTexts.Value[0], missing.WelcomeTexts.Value[0]);
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
        const string before = "web_service:\r\nwelcome_animation:\r\nwelcome_animation_videos:\r\n# 保留\r\nlog:\r\nblind_box:\r\nsend:\r\nwelcome:\r\n  uids: [42]  # 老朋友\r\n  fan_club_level: 0 # 老粉\r\n  guard: 舰长\r\n  wealth_level: 0 # 荣耀\r\nfuture: 保留\r\n";
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
        changes["welcome.uids"] = "[ 02147483648, 9223372036854775807 ]";
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
    [InlineData("uids", "[+123]")]
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
        Assert.EndsWith(key == "uids"
            ? "必须是 [12345, 67890] 形式的单行数字列表，每项为 int64 范围内的正整数；[] 表示空列表。"
            : "必须为非负整数。", error.Message);
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
        const string yaml = "web_service:\nwelcome_animation:\nwelcome_animation_videos:\nlog:\nblind_box:\nsend:\nwelcome: {uids: [12345]}\n";
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
    public void Save_changes_only_requested_welcome_lines()
    {
        Settings.Load(h.DataDirectory);
        const string before = "welcome:\n  enabled: false # 开关\n  texts: ['欢迎{昵称}'] # 正文\n  guard: 舰长 # 档位\nfuture: 保留\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), before);
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.texts"] = Settings.TextListText(["欢迎{大航海}{昵称}"]),
            ["welcome.guard"] = "提督"
        });
        var expected = before.Replace("false", "true").Replace("['欢迎{昵称}']", "[\"欢迎{大航海}{昵称}\"]")
            .Replace("舰长", "提督");
        Assert.Equal(expected, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        var loaded = Settings.Load(h.DataDirectory);
        Assert.Null(loaded.Error);
        Assert.True(loaded.WelcomeEnabled.Value);
        Assert.Equal("欢迎{大航海}{昵称}", loaded.WelcomeTexts.Value[0]);
        Assert.Equal("提督", loaded.WelcomeGuard.Value);
    }

    [Theory]
    [InlineData("80")]
    [InlineData("invalid")]
    public void Old_max_length_is_only_an_unknown_key_and_is_preserved(string value)
    {
        Settings.Load(h.DataDirectory);
        var yaml = $"web_service:\nwelcome_animation:\nwelcome_animation_videos:\nlog:\nblind_box:\nsend:\nwelcome:\n  max_length: {value} # 保留\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), yaml);
        var settings = Settings.Load(h.DataDirectory, h.Logger);
        Assert.Null(settings.Error);
        Assert.Equal(["welcome.max_length"], settings.UnknownKeys);
        var warning = Assert.Single(h.Logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, warning.Level);
        Assert.Contains("未知设置键：welcome.max_length", warning.Message);
        Assert.Equal(yaml, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
    }

    [Theory]
    [InlineData("enabled", "yes")]
    [InlineData("enabled", "[]")]
    [InlineData("guard", "船长")]
    [InlineData("guard", "3")]
    [InlineData("guard", "[]")]
    [InlineData("texts", "[]")]
    [InlineData("texts", "''")]
    [InlineData("texts", "' '")]
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
    [InlineData("{@}欢迎{大航海}{昵称}")]
    [InlineData("{{{昵称}}}🌙")]
    [InlineData("ABC xyz!~")]
    [InlineData("{大航海}{大航海}")]
    [InlineData("一二三四五六七八九十百{昵称}")]
    public void Valid_placeholders_can_be_saved_without_a_known_length_limit(string text)
    {
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.texts"] = Settings.TextListText([text])
        });
        var settings = Settings.Load(h.DataDirectory);
        Assert.Null(settings.Error);
        Assert.Equal(text, settings.WelcomeTexts.Value[0]);
    }

    [Theory]
    [InlineData("'{舰长}'", "可用")]
    [InlineData("'欢迎{@}'", "可用")]
    [InlineData("' {@}欢迎'", "可用")]
    [InlineData("'{@}{@}欢迎'", "可用")]
    [InlineData("'欢迎{'", "可用")]
    [InlineData("'欢迎}'", "可用")]
    [InlineData("'{@}'", "不能只有")]
    [InlineData("'{@} '", "不能只有")]
    [InlineData("''", "不能为空")]
    [InlineData("' '", "不能为空")]
    public void Invalid_text_reports_its_line_and_keeps_the_file(string text, string reason)
    {
        Directory.CreateDirectory(h.DataDirectory);
        var yaml = $"welcome:\n  enabled: true\n  texts: [{text}]\n";
        File.WriteAllText(Settings.FilePath(h.DataDirectory), yaml);
        var loaded = Settings.Load(h.DataDirectory);
        var error = Assert.IsType<SettingsException>(loaded.Error);
        Assert.Equal("welcome.texts", error.SettingName);
        Assert.Equal(3, error.LineNumber);
        Assert.Contains(reason, error.Message);
        // Kept as written, so the settings window shows the text the error is about.
        Assert.Equal(text[1..^1], loaded.WelcomeTexts.Value[0]);
        Assert.Equal(yaml, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "welcome:\n  texts: ['欢迎']\n");
        var beforeSave = File.ReadAllText(Settings.FilePath(h.DataDirectory));
        var saveError = Assert.Throws<SettingsException>(() => Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.texts"] = Settings.TextListText([text[1..^1]])
        }));
        Assert.Equal("welcome.texts", saveError.SettingName);
        // Like every other rejected input, reported at line 1: the input is wrong, not the file.
        Assert.Equal(1, saveError.LineNumber);
        Assert.Contains(reason, saveError.Message);
        Assert.Equal(beforeSave, File.ReadAllText(Settings.FilePath(h.DataDirectory)));
    }

    public ValueTask DisposeAsync() => h.DisposeAsync();
}
