using System.Net.WebSockets;
using System.Text;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

public sealed class FileLoggingTests
{
    [Fact]
    public async Task Retry_logs_distinguish_failed_attempts_from_disconnects_and_keep_inner_exceptions()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        using var host = FileLoggingHost(h.DataDirectory);
        var attempts = 0;
        using var client = new AetherClient(h.Http, (uri, token) => ++attempts == 2
            ? h.Server.ConnectAsync(uri, token)
            : throw new WebSocketException("Unable to connect to the remote server",
                new HttpRequestException("proxy CONNECT rejected: 403")), h.Time,
            host.Services.GetRequiredService<ILogger<AetherClient>>(), h.DataDirectory);
        await using var updates = h.Watch(client);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Reconnecting>(updates.Current);

        var lines = File.ReadAllLines(Assert.Single(Directory.GetFiles(
            Path.Combine(h.DataDirectory, "logs", "7734200"), "*.log")));
        Assert.Collection(lines.Where(line => line.Contains("[WRN]")),
            line => Assert.Contains("连接弹幕服务器失败，1 秒后重试", line),
            line => Assert.Contains("直播间连接中断，1 秒后重试", line),
            line => Assert.Contains("连接弹幕服务器失败，2 秒后重试", line));
        Assert.Contains(lines, line => line.Contains("System.Net.Http.HttpRequestException: proxy CONNECT rejected: 403"));
        Assert.Contains(lines, line => line.Contains(nameof(Retry_logs_distinguish_failed_attempts_from_disconnects_and_keep_inner_exceptions)));
    }

    [Fact]
    public async Task Room_logs_start_once_and_each_authenticated_server_across_reconnects()
    {
        await using var h = new WatchHarness();
        await using var second = new FakeDanmakuServer();
        await h.LoginAsync();
        using var host = FileLoggingHost(h.DataDirectory);
        var attempts = 0;
        using var client = new AetherClient(h.Http, (uri, token) =>
            (++attempts == 1 ? h.Server : second).ConnectAsync(uri, token), h.Time,
            host.Services.GetRequiredService<ILogger<AetherClient>>(), h.DataDirectory);
        await using var updates = await h.WatchConnectedAsync(client);
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        h.Http.Responses["https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo"] =
            """{"code":0,"data":{"token":"fresh-token","host_list":[{"host":"fresh.example","wss_port":443}]}}""";
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);

        var logs = Path.Combine(h.DataDirectory, "logs");
        var lines = File.ReadAllLines(Assert.Single(Directory.GetFiles(Path.Combine(logs, "7734200"), "*.log")));
        Assert.Collection(lines.Where(line => line.Contains("[INF]")),
            line => Assert.Contains("直播间连接开始", line),
            line => { Assert.Contains("已连接弹幕服务器", line); Assert.Contains("wss://danmaku.example/sub", line); },
            line => { Assert.Contains("已连接弹幕服务器", line); Assert.Contains("wss://fresh.example/sub", line); });
        Assert.Empty(Directory.GetFiles(logs, "*.log"));
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public async Task Only_trace_records_each_valid_room_message_as_one_raw_json_line(LogLevel level)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        File.WriteAllText(Path.Combine(h.DataDirectory, "aether.yml"), $"log:\n  level: {level}\n");
        using var host = FileLoggingHost(h.DataDirectory);
        using var client = new AetherClient(h.Http, h.Server.ConnectAsync, h.Time,
            host.Services.GetRequiredService<ILogger<AetherClient>>(), h.DataDirectory);
        await using var updates = await h.WatchConnectedAsync(client);
        const string body = """{"cmd":"DANMU_MSG","info":[[],"内容",[0,"观众"]]}""";
        var ignored = new[]
        {
            """{"cmd":"SEND_GIFT","data":{"uid":0,"uname":"测试观众"}}""",
            """{"cmd":"SEND_GIFT_V2","data":{"pb":""}}""",
            """{"cmd":"COMBO_SEND","data":{}}""",
            "{\r\n  \"cmd\": \"FUTURE_EVENT\",\n  \"text\": \"escaped\\nline\"\r\n}",
            "{\"cmd\":\"FUTURE_LARGE_EVENT\",\"text\":\"" + new string('中', 2048) + "\"}"
        };
        foreach (var message in ignored)
            await h.Server.PushAsync(FakeDanmakuServer.Packet(5, Encoding.UTF8.GetBytes(message), 0));
        const string malformed = """{"cmd":"DANMU_MSG","info":[]}""";
        await h.Server.PushAsync(FakeDanmakuServer.Packet(5, Encoding.UTF8.GetBytes(malformed), 0));
        await h.Server.PushAsync(FakeDanmakuServer.Packet(5, Encoding.UTF8.GetBytes(body), 0));
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal(new Danmaku(h.Time.GetLocalNow(), "观众", "内容"), updates.Current);

        var room = Path.Combine(h.DataDirectory, "logs", "7734200");
        var lines = Directory.Exists(room)
            ? File.ReadAllLines(Assert.Single(Directory.GetFiles(room, "*.log"))) : [];
        const string prefix = "直播间消息原始 JSON：";
        var captures = lines.Where(line => line.Contains(prefix)).ToArray();
        if (level == LogLevel.Trace)
        {
            Assert.Equal(new[]
            {
                ignored[0], ignored[1], ignored[2],
                """{  "cmd": "FUTURE_EVENT",  "text": "escaped\nline"}""",
                ignored[4], body
            }, captures.Select(line => line[(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length)..]));
            Assert.All(captures, line => Assert.Contains("[VRB]", line));
        }
        else Assert.Empty(captures);
        if (level <= LogLevel.Warning)
            Assert.Contains(malformed, Assert.Single(lines, line => line.Contains("[WRN]")));
        // The SEND_GIFT then SEND_GIFT_V2 above report the gift message version, then its switch.
        var notices = lines.Where(line => line.Contains("[INF]")).ToArray();
        if (level <= LogLevel.Information)
            Assert.Collection(notices,
                line => Assert.Contains("直播间连接开始", line),
                line => Assert.Contains("已连接弹幕服务器", line),
                line => Assert.Contains("V1", line), line => Assert.Contains("V2", line));
        else Assert.Empty(notices);
        Assert.Equal(ignored.Length * (level <= LogLevel.Debug ? 1 : 0)
            + (level <= LogLevel.Warning ? 1 : 0) + notices.Length + captures.Length, lines.Length);
        Assert.False(Directory.Exists(Path.Combine(h.DataDirectory, "logs", "6")));
        var logs = Path.Combine(h.DataDirectory, "logs");
        if (Directory.Exists(logs)) Assert.Empty(Directory.GetFiles(logs, "*.log"));
    }

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
                using var scope = logger.BeginRoomScope(1L);
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
                using (logger.BeginRoomScope(7734200L))
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
                using var scope = logger.BeginRoomScope(roomId);
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
                using var scope = logger.BeginRoomScope(roomId);
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
                using (logger.BeginRoomScope(1L))
                    logger.LogInformation("first-room-owner");
                using (var other = FileLoggingHost(directory))
                {
                    var otherLogger = other.Services.GetRequiredService<ILogger<FileLoggingTests>>();
                    otherLogger.LogInformation("second-host");
                    using (otherLogger.BeginRoomScope(1L))
                        otherLogger.LogInformation("second-room-owner");
                }
                logger.LogInformation("first-still-alive");
                foreach (var room in new[] { 1L, 2L, 3L, 4L })
                {
                    using var scope = logger.BeginRoomScope(room);
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
