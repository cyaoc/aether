using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Aether.Core;

public static class FileLogging
{
    /// <summary>Adds the Core-owned file logs and exception handlers; the host owns their lifetime.</summary>
    public static ILoggingBuilder AddAetherFileLogging(this ILoggingBuilder logging, string dataDirectory)
    {
        logging.Services.AddSingleton(_ => new FileLog(dataDirectory));
        logging.Services.AddSingleton<ILoggerProvider>(services =>
            new SerilogLoggerProvider(services.GetRequiredService<FileLog>().Logger));
        logging.AddFilter<SerilogLoggerProvider>(null, LogLevel.Information);
        return logging;
    }

    private sealed class FileLog : IDisposable
    {
        private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

        public Serilog.Core.Logger Logger { get; }

        public FileLog(string dataDirectory)
        {
            var logs = Path.Combine(dataDirectory, "logs");
            Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .WriteTo.Map("RoomId", 0L, (roomId, write) => write.File(
                    Path.Combine(roomId > 0 ? Path.Combine(logs, roomId.ToString(CultureInfo.InvariantCulture)) : logs,
                        "aether-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: null,
                    retainedFileTimeLimit: Retention,
                    fileSizeLimitBytes: null,
                    shared: roomId <= 0,
                    buffered: false,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({ProcessId}) {SourceContext}: {Message:lj}{NewLine}{Exception}"),
                    // Unbuffered and closed after every event, so exit has nothing left to flush.
                    // ponytail: reopen per event so an idle GUI never holds a previous room's file;
                    // tie cached sinks to room-lock lifetime only if logging throughput needs it.
                    sinkMapCountLimit: 0)
                .CreateLogger();
            DeleteExpiredLogs(logs);
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        // Serilog prunes only the folder it writes to, so a room never reconnected would keep its logs forever.
        private void DeleteExpiredLogs(string logs)
        {
            if (!Directory.Exists(logs)) return;
            var expired = DateTime.Now - Retention;
            try
            {
                foreach (var file in Directory.EnumerateFiles(logs, "aether-*.log", new EnumerationOptions { RecurseSubdirectories = true }))
                    if (File.GetLastWriteTime(file) < expired) File.Delete(file);
                // ponytail: a process creating this folder right now may lose one line; rooms rarely start in the same instant.
                foreach (var room in Directory.EnumerateDirectories(logs))
                    if (!Directory.EnumerateFileSystemEntries(room).Any()) Directory.Delete(room);
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
