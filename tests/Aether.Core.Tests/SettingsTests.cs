using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

public sealed class SettingsTests : IAsyncDisposable
{
    private readonly WatchHarness harness = new();
    private string SettingsPath => Path.Combine(harness.DataDirectory, "aether.yml");

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

    private void Write(string yaml)
    {
        Directory.CreateDirectory(harness.DataDirectory);
        File.WriteAllText(SettingsPath, yaml);
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
    [InlineData("", 30, true, false)]
    [InlineData("log:\n  level: Debug", 30, true, false)]
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

    public ValueTask DisposeAsync() => harness.DisposeAsync();
}
