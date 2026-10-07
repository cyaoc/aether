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
        public Serilog.Core.Logger Logger { get; }

        public FileLog(string dataDirectory)
        {
            var logs = Path.Combine(dataDirectory, "logs");
            Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.Map("RoomId", 0L, (roomId, write) => write.File(
                    Path.Combine(roomId > 0 ? Path.Combine(logs, roomId.ToString(CultureInfo.InvariantCulture)) : logs,
                        "aether-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: null,
                    retainedFileTimeLimit: TimeSpan.FromDays(30),
                    fileSizeLimitBytes: null,
                    shared: roomId <= 0,
                    buffered: false,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"),
                    // ponytail: reopen per event so an idle GUI never holds a previous room's file;
                    // tie cached sinks to room-lock lifetime only if logging throughput needs it.
                    sinkMapCountLimit: 0)
                .CreateLogger();
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
            Logger.ForContext("SourceContext", "Aether.Core.UnhandledException")
                .Fatal(args.ExceptionObject as Exception, "进程未处理异常：{Error}", args.ExceptionObject);

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args) =>
            Logger.ForContext("SourceContext", "Aether.Core.UnobservedTaskException")
                .Fatal(args.Exception, "未观察到的 Task 异常");

        private void OnProcessExit(object? sender, EventArgs args) => Dispose();

        public void Dispose()
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            Logger.Dispose();
        }
    }
}
