using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

public sealed class SettingsTests : IAsyncDisposable
{
    private readonly WatchHarness harness = new();
    private string SettingsPath => Path.Combine(harness.DataDirectory, "aether.yml");

    [Fact]
    public void Blind_box_template_and_boolean_save_use_single_line_values()
    {
        Assert.Null(Settings.Load(harness.DataDirectory).Error);
        var template = File.ReadAllText(SettingsPath);
        Assert.Contains("下次观看直播间时生效", template);
        Assert.Contains("blind_box:\n  enabled: true", template);
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = "false" });
        Assert.Equal(template.Replace("enabled: true", "enabled: false"), File.ReadAllText(SettingsPath));
        Assert.Empty(Settings.Load(harness.DataDirectory).UnknownKeys);
        Assert.False(Settings.Load(harness.DataDirectory).BlindBoxEnabled.Value);
    }

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

    [Fact]
    public void Missing_blind_box_group_defaults_to_enabled_and_is_appended_when_changed()
    {
        const string yaml = "# 保留\r\nlog:\r\n  level: Debug\r\n";
        Write(yaml);
        Assert.True(Settings.Load(harness.DataDirectory).BlindBoxEnabled.Value);
        Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = "false" });
        Assert.Equal(yaml + "blind_box:\r\n  enabled: false\r\n", File.ReadAllText(SettingsPath));
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
        var yaml = $"# 我的注释\nlog:\n  retention_days: 7 # 一周\n  level: {level}\n";
        Write(yaml);

        var settings = Settings.Load(harness.DataDirectory);

        Assert.Null(settings.Error);
        Assert.Equal(7, settings.LogRetentionDays.Value);
        Assert.Equal(expected, settings.LogLevel.Value);
        Assert.True(settings.LogRetentionDays.CanEdit);
        Assert.True(settings.LogLevel.CanEdit);
        Assert.Equal(yaml, File.ReadAllText(SettingsPath));
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

        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);

        Assert.Equal(SettingsPath, error.FilePath);
        Assert.Equal(line, error.LineNumber);
        Assert.Equal(name, error.SettingName);
        Assert.Contains(SettingsPath, error.Message);
        Assert.Contains($"第 {line} 行", error.Message);
        Assert.Contains(name, error.Message);
    }

    [Fact]
    public void Malformed_yaml_reports_the_parser_line()
    {
        Write("log:\n  level: @bad\n");

        var error = Assert.IsType<SettingsException>(Settings.Load(harness.DataDirectory).Error);

        Assert.Equal(SettingsPath, error.FilePath);
        Assert.Equal(2, error.LineNumber);
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

    // No shipped setting is text or a list yet; these stand in for the coming welcome settings.
    private static readonly Settings.Definition<string> Greeting = Settings.Definition.Text("welcome.greeting", "");

    [Theory]
    [InlineData("{观众} 欢迎", "'{观众} 欢迎'")]
    [InlineData("欢迎 # 新朋友", "'欢迎 # 新朋友'")]
    [InlineData(" 前后空格 ", "' 前后空格 '")]
    [InlineData("it's", "it's")]
    [InlineData("", "''")]
    [InlineData("你好", "你好")]
    public void Text_settings_are_quoted_only_when_yaml_needs_it_and_read_back_as_typed(string text, string written)
    {
        Write("# 欢迎\nwelcome:\n  greeting: 旧的  # 行尾\nlog:\n  level: Debug\n");

        Settings.Save(harness.DataDirectory, [Greeting], new Dictionary<string, string> { ["welcome.greeting"] = text });

        Assert.Equal($"# 欢迎\nwelcome:\n  greeting: {written}  # 行尾\nlog:\n  level: Debug\n", File.ReadAllText(SettingsPath));
        var loaded = Settings.Load(harness.DataDirectory, [Greeting]);
        Assert.Null(loaded.Error);
        Assert.Equal(new Setting<string>("welcome.greeting", text), loaded.Get(Greeting));
    }

    private static readonly Settings.Definition<IReadOnlyList<string>> Viewers = Settings.Definition.TextList("welcome.viewers", []);

    [Fact]
    public void One_line_lists_are_rewritten_in_place_while_block_lists_stay_read_only()
    {
        Write("welcome:\n  viewers: [甲, '乙']  # 名单\n");
        var loaded = Settings.Load(harness.DataDirectory, [Viewers]);
        Assert.Equal(["甲", "乙"], loaded.Get(Viewers).Value);
        Assert.True(loaded.Get(Viewers).CanEdit);

        Settings.Save(harness.DataDirectory, [Viewers], new Dictionary<string, string> { ["welcome.viewers"] = "[甲, '{丙}', 'a, b']" });

        Assert.Equal("welcome:\n  viewers: [甲, '{丙}', 'a, b']  # 名单\n", File.ReadAllText(SettingsPath));
        Assert.Equal(["甲", "{丙}", "a, b"], Settings.Load(harness.DataDirectory, [Viewers]).Get(Viewers).Value);

        Write("welcome:\n  viewers:\n    - 甲\n");
        loaded = Settings.Load(harness.DataDirectory, [Viewers]);
        Assert.Null(loaded.Error);
        Assert.Equal(["甲"], loaded.Get(Viewers).Value);
        Assert.False(loaded.Get(Viewers).CanEdit);
        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory, [Viewers],
            new Dictionary<string, string> { ["welcome.viewers"] = "[乙]" }));
        Assert.Equal("welcome:\n  viewers:\n    - 甲\n", File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("welcome.viewers", "甲")]
    [InlineData("welcome.viewers", "[[甲]]")]
    [InlineData("welcome.viewers", "[甲")]
    [InlineData("welcome.greeting", "两\n行")]
    [InlineData("welcome.viewers", "[[a],\n  b: 7\n    c]")]
    public void Invalid_text_or_list_input_is_rejected_without_writing(string name, string value)
    {
        const string yaml = "welcome:\n  greeting: 你好\n  viewers: [甲]\n";
        Write(yaml);

        Assert.Throws<SettingsException>(() => Settings.Save(harness.DataDirectory, [Greeting, Viewers],
            new Dictionary<string, string> { [name] = value }));

        Assert.Equal(yaml, File.ReadAllText(SettingsPath));
    }

    [Theory]
    [InlineData("welcome:\n  greeting: 你好\n", "welcome.viewers", "[甲, 乙]", "welcome:\n  greeting: 你好\n  viewers: [甲, 乙]\n")]
    [InlineData("welcome:\n  viewers: [甲]  # 名单\nlog: {}\n", "welcome.greeting", "你好",
        "welcome:\n  viewers: [甲]  # 名单\n  greeting: 你好\nlog: {}\n")]
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
        Write("welcome:\n  viewers: [甲,\n    乙]\n");

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
