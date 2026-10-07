using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Aether.Core;

public static class FileLogging
{
    /// <summary>The log scope property that routes an event to its room's folder; its value is the real room ID.</summary>
    internal const string RoomIdProperty = "RealRoomId";

    /// <summary>Adds the Core-owned file logs and exception handlers; the host owns their lifetime.</summary>
    public static ILoggingBuilder AddAetherFileLogging(this ILoggingBuilder logging, string dataDirectory)
    {
        var settings = Settings.Load(dataDirectory);
        logging.Services.AddSingleton(_ => new FileLog(dataDirectory, settings));
        logging.Services.AddSingleton<ILoggerProvider>(services =>
            new SerilogLoggerProvider(services.GetRequiredService<FileLog>().Logger));
        // Let every event reach the file log, which applies log.level itself, whatever the host's own minimum.
        logging.AddFilter<SerilogLoggerProvider>(null, LogLevel.Trace);
        return logging;
    }

    private sealed class FileLog : IDisposable
    {
        // Null keeps logs forever: a retention reaching back past DateTime.MinValue would overflow date subtraction.
        private readonly TimeSpan? retention;

        public Serilog.Core.Logger Logger { get; }

        /// <remarks>Records settings problems found at startup; a settings error falls back to the default log settings.</remarks>
        public FileLog(string dataDirectory, Settings settings)
        {
            var effective = settings.Error is null ? settings : new Settings();
            var days = effective.LogRetentionDays.Value;
            retention = days < (DateTime.Now - DateTime.MinValue).TotalDays ? TimeSpan.FromDays(days) : null;
            var logs = Path.Combine(dataDirectory, "logs");
            Logger = new LoggerConfiguration()
                .MinimumLevel.Is(Serilog.Extensions.Logging.LevelConvert.ToSerilogLevel(effective.LogLevel.Value))
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .WriteTo.Map(RoomIdProperty, 0L, (roomId, write) => write.File(
                    Path.Combine(roomId > 0 ? Path.Combine(logs, roomId.ToString(CultureInfo.InvariantCulture)) : logs,
                        "aether-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: null,
                    retainedFileTimeLimit: retention,
                    fileSizeLimitBytes: null,
                    shared: roomId <= 0,
                    buffered: false,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({ProcessId}) {SourceContext}: {Message:lj}{NewLine}{Exception}"),
                    // Unbuffered and closed after every event, so exit has nothing left to flush.
                    // ponytail: reopen per event so an idle GUI never holds a previous room's file;
                    // tie cached sinks to room-lock lifetime only if logging throughput needs it.
                    sinkMapCountLimit: 0)
                .CreateLogger();
            var settingsLog = Logger.ForContext("SourceContext", "Aether.Core.Settings");
            if (settings.Error is { } error)
                settingsLog.Error(error, "读取设置失败，日志使用默认值；开始直播间连接前需修正设置");
            if (settings.UnknownKeys.Count > 0)
                settingsLog.Warning("未知设置键：{Keys}", string.Join("、", settings.UnknownKeys));
            DeleteExpiredLogs(logs);
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        // Serilog prunes only the folder it writes to, so a room never reconnected would keep its logs forever.
        private void DeleteExpiredLogs(string logs)
        {
            if (retention is not { } limit || !Directory.Exists(logs)) return;
            var expired = DateTime.Now - limit;
            try
            {
                foreach (var file in Directory.EnumerateFiles(logs, "aether-*.log", new EnumerationOptions { RecurseSubdirectories = true }))
                    if (File.GetLastWriteTime(file) < expired) File.Delete(file);
                // Keep directories: a writer may have ensured its directory exists but not opened the file yet.
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Logger.ForContext("SourceContext", "Aether.Core.FileLogging")
                    .Warning("清理过期日志失败，下次启动再试：{Error}", error.Message);
            }
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
            Logger.ForContext("SourceContext", "Aether.Core.UnhandledException")
                .Fatal(args.ExceptionObject as Exception, "进程未处理异常");

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args) =>
            Logger.ForContext("SourceContext", "Aether.Core.UnobservedTaskException")
                .Error(args.Exception, "未观察到的 Task 异常"); // Does not end the process by default, so not Fatal.

        public void Dispose()
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            Logger.Dispose();
        }
    }
}
