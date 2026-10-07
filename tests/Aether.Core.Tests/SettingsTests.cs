using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

public sealed class SettingsTests : IAsyncDisposable
{
    private readonly WatchHarness harness = new();
    private string SettingsPath => Path.Combine(harness.DataDirectory, "aether.yml");

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

    private void Write(string yaml)
    {
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(SettingsPath, yaml);
    }

    public ValueTask DisposeAsync() => harness.DisposeAsync();
}
