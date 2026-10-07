using System.Net.WebSockets;
using System.Text;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

public sealed class FileLoggingTests
{
    [Theory]
    [InlineData(LogLevel.Trace, 7)]
    [InlineData(LogLevel.Debug, 7)]
    [InlineData(LogLevel.Warning, 7)]
    [InlineData(LogLevel.Critical, int.MaxValue)]
    public void Startup_settings_control_file_levels_and_retention(LogLevel level, int days)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-logs-" + Guid.NewGuid());
        var abandoned = Path.Combine(directory, "logs", "1");
        Directory.CreateDirectory(abandoned);
        var old = Path.Combine(abandoned, "aether-20000101.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-10));
        File.WriteAllText(Path.Combine(directory, "aether.yml"), $"log:\n  retention_days: {days}\n  level: {level}\n");
        try
        {
            using (var host = FileLoggingHost(directory))
            {
                // Editing the file does not change the snapshot passed to logging at startup.
                File.WriteAllText(Path.Combine(directory, "aether.yml"), "log:\n  level: bad\n");
                var logger = host.Services.GetRequiredService<ILogger<FileLoggingTests>>();
                using var scope = logger.BeginScope(new Dictionary<string, object> { [FileLogging.RoomIdProperty] = 1L });
                foreach (var candidate in Enum.GetValues<LogLevel>().Where(value => value != LogLevel.None))
                    logger.Log(candidate, "marker-{Candidate}", candidate.ToString());
            }
            var content = File.ReadAllText(Assert.Single(Directory.GetFiles(abandoned, "*.log"), path => path != old));
            foreach (var candidate in Enum.GetValues<LogLevel>().Where(value => value != LogLevel.None))
                Assert.True((candidate >= level) == content.Contains($"marker-{candidate}"), content);
            Assert.Equal(days == int.MaxValue, File.Exists(old));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Invalid_startup_settings_use_default_logging_and_unknown_keys_are_warned()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-logs-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "aether.yml"), "log:\n  level: Critical\n  retention_days: 0\n  extra: true\n");
        try
        {
            using (var host = FileLoggingHost(directory))
            {
                var logger = host.Services.GetRequiredService<ILogger<FileLoggingTests>>();
                logger.LogDebug("hidden-debug");
                logger.LogInformation("default-information");
            }
            var content = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs"), "*.log")));
            Assert.Contains("default-information", content);
            Assert.DoesNotContain("hidden-debug", content);
            Assert.Contains("log.extra", content);
            Assert.Contains("[WRN]", content);
            Assert.Contains("读取设置失败", content);
            Assert.Contains("log.retention_days", content);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Host_writes_room_and_common_logs_with_levels_categories_and_exception_stacks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-logs-" + Guid.NewGuid());
        try
        {
            using (var host = FileLoggingHost(directory))
            {
                var logger = host.Services.GetRequiredService<ILogger<FileLoggingTests>>();
                logger.LogTrace("hidden-trace");
                logger.LogDebug("hidden-debug");
                logger.LogInformation("common-before");
                using (logger.BeginScope(new Dictionary<string, object> { [FileLogging.RoomIdProperty] = 7734200L }))
                {
                    logger.LogInformation("room-message");
                    try { ThrowForLog(); }
                    catch (Exception error) { logger.LogError(error, "room-error"); }
                }
                logger.LogInformation("common-after");
            }

            var logs = Path.Combine(directory, "logs");
            var common = File.ReadAllText(Assert.Single(Directory.GetFiles(logs, "*.log")));
            var roomPath = Assert.Single(Directory.GetFiles(Path.Combine(logs, "7734200"), "*.log"));
            var room = File.ReadAllText(roomPath);
            Assert.Matches(@"aether-\d{8}\.log$", roomPath);
            Assert.Contains("common-before", common);
            Assert.Contains("common-after", common);
            Assert.DoesNotContain("room-message", common);
            Assert.DoesNotContain("common-", room);
            Assert.DoesNotContain("hidden-", common + room);
            Assert.Contains($"[INF] ({Environment.ProcessId}) Aether.Core.Tests.FileLoggingTests", room);
            Assert.Contains("room-message", room);
            Assert.Contains("[ERR]", room);
            Assert.Contains("System.InvalidOperationException: log-stack-marker", room);
            Assert.Contains(nameof(ThrowForLog), room);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void ThrowForLog() => throw new InvalidOperationException("log-stack-marker");

    // Trace on the host shows the file keeps its independently configured floor.
    private static IHost FileLoggingHost(string dataDirectory)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddAetherFileLogging(dataDirectory);
        return builder.Build();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(7734200L)]
    public void Opening_log_removes_files_older_than_30_days(long roomId)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-logs-" + Guid.NewGuid());
        var logs = roomId == 0 ? Path.Combine(directory, "logs") : Path.Combine(directory, "logs", "7734200");
        Directory.CreateDirectory(logs);
        var old = Path.Combine(logs, $"aether-{DateTime.Today.AddDays(-40):yyyyMMdd}.log");
        var recent = Path.Combine(logs, $"aether-{DateTime.Today.AddDays(-7):yyyyMMdd}.log");
        File.WriteAllText(old, "old");
        File.WriteAllText(recent, "recent");
        try
        {
            using (var host = FileLoggingHost(directory))
            {
                var logger = host.Services.GetRequiredService<ILogger<FileLoggingTests>>();
                using var scope = logger.BeginScope(new Dictionary<string, object> { [FileLogging.RoomIdProperty] = roomId });
                logger.LogInformation("today");
            }
            Assert.False(File.Exists(old));
            Assert.Equal("recent", File.ReadAllText(recent));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Startup_removes_expired_logs_but_preserves_room_directories_for_writers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-logs-" + Guid.NewGuid());
        var room = Path.Combine(directory, "logs", "7734200");
        var abandoned = Path.Combine(directory, "logs", "1");
        Directory.CreateDirectory(room);
        Directory.CreateDirectory(abandoned);
        var old = Path.Combine(room, $"aether-{DateTime.Today.AddDays(-40):yyyyMMdd}.log");
        var recent = Path.Combine(room, $"aether-{DateTime.Today.AddDays(-7):yyyyMMdd}.log");
        var abandonedOld = Path.Combine(abandoned, $"aether-{DateTime.Today.AddDays(-40):yyyyMMdd}.log");
        File.WriteAllText(old, "old");
        File.WriteAllText(recent, "recent");
        File.WriteAllText(abandonedOld, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-40));
        File.SetLastWriteTime(recent, DateTime.Now.AddDays(-7));
        File.SetLastWriteTime(abandonedOld, DateTime.Now.AddDays(-40));
        try
        {
            using (var host = FileLoggingHost(directory))
                host.Services.GetRequiredService<ILoggerFactory>();
            Assert.False(File.Exists(old));
            Assert.Equal("recent", File.ReadAllText(recent));
            Assert.True(Directory.Exists(abandoned));
            Assert.Empty(Directory.GetFileSystemEntries(abandoned));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Startup_cleanup_does_not_lose_the_first_event_in_a_room_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-logs-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "logs"));
        var finished = 0;
        var cleanups = 0;
        using var start = new Barrier(2);
        using var host = FileLoggingHost(directory);
        var logger = host.Services.GetRequiredService<ILogger<FileLoggingTests>>();
        var cleanup = Task.Run(() =>
        {
            start.SignalAndWait(TestContext.Current.CancellationToken);
            while (Volatile.Read(ref finished) == 0)
            {
                using var other = LoggerFactory.Create(logging => logging.AddAetherFileLogging(directory));
                other.CreateLogger("Startup");
                Interlocked.Increment(ref cleanups);
            }
        }, TestContext.Current.CancellationToken);
        try
        {
            start.SignalAndWait(TestContext.Current.CancellationToken);
            for (var i = 0; i < 1000; i++)
            {
                var roomId = 7734200L + i;
                var room = Path.Combine(directory, "logs", roomId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(room);
                using var scope = logger.BeginScope(new Dictionary<string, object> { [FileLogging.RoomIdProperty] = roomId });
                logger.LogInformation("first-event-{Index}", i);
                var file = Assert.Single(Directory.GetFiles(room, "*.log"));
                Assert.Contains($"first-event-{i}", File.ReadAllText(file));
                // Recreate the empty-directory condition after verifying each event reached disk.
                File.Delete(file);
            }
        }
        finally
        {
            Volatile.Write(ref finished, 1);
            await cleanup;
            Directory.Delete(directory, true);
        }
        Assert.True(cleanups > 0);
    }

    [Fact]
    public void Hosts_share_common_file_and_switching_rooms_releases_old_file_handles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-logs-" + Guid.NewGuid());
        try
        {
            using (var host = FileLoggingHost(directory))
            {
                var logger = host.Services.GetRequiredService<ILogger<FileLoggingTests>>();
                logger.LogInformation("first-host");
                using (logger.BeginScope(new Dictionary<string, object> { [FileLogging.RoomIdProperty] = 1L }))
                    logger.LogInformation("first-room-owner");
                using (var other = FileLoggingHost(directory))
                {
                    var otherLogger = other.Services.GetRequiredService<ILogger<FileLoggingTests>>();
                    otherLogger.LogInformation("second-host");
                    using (otherLogger.BeginScope(new Dictionary<string, object> { [FileLogging.RoomIdProperty] = 1L }))
                        otherLogger.LogInformation("second-room-owner");
                }
                logger.LogInformation("first-still-alive");
                foreach (var room in new[] { 1L, 2L, 3L, 4L })
                {
                    using var scope = logger.BeginScope(new Dictionary<string, object> { [FileLogging.RoomIdProperty] = room });
                    logger.LogInformation("switch-room");
                }
                var released = Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs", "1"), "*.log"));
                using var exclusive = new FileStream(released, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            var common = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs"), "*.log")));
            Assert.Contains("first-host", common);
            Assert.Contains("second-host", common);
            Assert.Contains("first-still-alive", common);
            var roomLog = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs", "1"), "*.log")));
            Assert.Contains("first-room-owner", roomLog);
            Assert.Contains("second-room-owner", roomLog);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Rejected_room_connection_leaves_recording_the_failure_to_the_caller()
    {
        await using var h = new WatchHarness();
        await using var owner = h.Watch();
        Assert.True(await owner.MoveNextAsync());
        using (var host = FileLoggingHost(h.DataDirectory))
        {
            using var client = new AetherClient(h.Http, h.Server.ConnectAsync, h.Time,
                host.Services.GetRequiredService<ILogger<AetherClient>>(), h.DataDirectory);
            await using var rejected = h.Watch(client);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await rejected.MoveNextAsync());
        }
        // The room is not known as ours yet, so nothing goes near the owner's file or duplicates the caller's record.
        Assert.False(Directory.Exists(Path.Combine(h.DataDirectory, "logs")));
    }

    [Fact]
    public async Task Room_connection_logs_keep_real_room_id_through_qr_polling_reconnect_and_messages()
    {
        await using var h = new WatchHarness();
        using (var host = FileLoggingHost(h.DataDirectory))
        {
            var logger = host.Services.GetRequiredService<ILogger<AetherClient>>();
            var respond = h.Http.Respond!;
            h.Http.Respond = (request, token) =>
            {
                logger.LogInformation("request {Path}", request.RequestUri!.AbsolutePath);
                return respond(request, token);
            };
            var attempts = 0;
            using var client = new AetherClient(h.Http, (uri, token) => ++attempts == 1
                ? throw new WebSocketException("reconnect-marker") : h.Server.ConnectAsync(uri, token),
                h.Time, logger, h.DataDirectory);
            await using (var updates = h.Watch(client))
            {
                Assert.True(await updates.MoveNextAsync());
                Assert.IsType<WatchQrCode>(updates.Current);
                Assert.True(await h.AdvancePollAsync(updates));
                Assert.IsType<Connecting>(updates.Current);
                Assert.True(await updates.MoveNextAsync());
                Assert.IsType<Reconnecting>(updates.Current);
                await h.AdvanceRetryAsync(updates, 1);
                Assert.IsType<Connected>(updates.Current);
                await h.Server.PushAsync(FakeDanmakuServer.Packet(5, Encoding.UTF8.GetBytes("{"), 0));
                await h.Server.PushAsync(FakeDanmakuServer.Packet(5,
                    Encoding.UTF8.GetBytes("""{"cmd":"DANMU_MSG","info":[[],"内容",[0,"观众"]]}"""), 0));
                Assert.True(await updates.MoveNextAsync());
                Assert.IsType<Danmaku>(updates.Current);
                await h.Server.PushAsync(FakeDanmakuServer.Packet(5, [], 4));
                await Assert.ThrowsAsync<InvalidDataException>(async () => await updates.MoveNextAsync());
            }
            logger.LogInformation("outside-room");
        }
        var logs = Path.Combine(h.DataDirectory, "logs");
        var common = File.ReadAllText(Assert.Single(Directory.GetFiles(logs, "*.log")));
        var room = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(logs, "7734200"), "*.log")));
        Assert.Contains("room_init", common);
        Assert.Contains("outside-room", common);
        Assert.DoesNotContain("outside-room", room);
        foreach (var marker in new[] { "qrcode/generate", "qrcode/poll", "getDanmuInfo", "reconnect-marker", "跳过无法解析", "不支持的弹幕协议版本" })
        {
            Assert.Contains(marker, room);
            Assert.DoesNotContain(marker, common);
        }
        Assert.False(Directory.Exists(Path.Combine(logs, "6")));
    }
}
