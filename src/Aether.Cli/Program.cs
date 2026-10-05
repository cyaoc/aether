using System.CommandLine;
using System.Text;
using Aether.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

Console.OutputEncoding = new UTF8Encoding(false);
var room = new Argument<long>("房间号") { Description = "直播间短号或真实房间号" };
var watch = new Command("watch", "匿名观看直播间弹幕") { room };
watch.SetAction(async (result, cancellationToken) =>
{
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
    builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<AetherClient>();
    using var host = builder.Build();
    try
    {
        var client = host.Services.GetRequiredService<AetherClient>();
        await foreach (var update in client.WatchAsync(result.GetValue(room), cancellationToken))
        {
            switch (update)
            {
                case Connecting:
                    await Console.Error.WriteLineAsync("连接中");
                    break;
                case Connected:
                    await Console.Error.WriteLineAsync("已连接");
                    break;
                case Danmaku danmaku:
                    await Console.Out.WriteLineAsync(FormattableString.Invariant($"[{danmaku.ReceivedAt:HH:mm:ss}] {OneLine(danmaku.Nickname)}: {OneLine(danmaku.Content)}"));
                    break;
            }
        }
        return 0;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
    catch (Exception error)
    {
        await Console.Error.WriteLineAsync($"错误：{error.Message}");
        return 1;
    }
});
var root = new RootCommand("Aether B站直播弹幕") { watch };
return await root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = Console.Error });

static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
