using System.CommandLine;
using System.Text;
using Aether.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using QRCoder;

Console.OutputEncoding = new UTF8Encoding(false);
var room = new Argument<long>("房间号") { Description = "直播间短号或真实房间号" };
var watch = new Command("watch", "观看直播间弹幕，需要时扫码登录") { room };
watch.SetAction((result, cancellationToken) => RunAsync(async client =>
{
    await foreach (var update in client.WatchAsync(result.GetValue(room), cancellationToken))
    {
        switch (update)
        {
            case WatchQrCode qr:
                await ShowQrCodeAsync(qr.Content);
                break;
            case Connecting:
                await Console.Error.WriteLineAsync("连接中");
                break;
            case Connected:
                await Console.Error.WriteLineAsync("已连接");
                break;
            case Reconnecting:
                await Console.Error.WriteLineAsync("重连中");
                break;
            case Danmaku danmaku:
                await Console.Out.WriteLineAsync(FormattableString.Invariant($"[{danmaku.ReceivedAt:HH:mm:ss}] {OneLine(danmaku.Nickname)}: {OneLine(danmaku.Content)}"));
                break;
        }
    }
}, cancellationToken));
var login = new Command("login", "扫码登录 B站账号");
login.SetAction((_, cancellationToken) => RunAsync(async client =>
{
    await foreach (var update in client.LoginAsync(cancellationToken))
    {
        switch (update)
        {
            case LoginQrCode qr:
                await ShowQrCodeAsync(qr.Content);
                break;
            case LoggedIn:
                await Console.Error.WriteLineAsync("已登录");
                break;
        }
    }
}, cancellationToken));
var logout = new Command("logout", "退出登录并删除本地登录凭据");
logout.SetAction((_, cancellationToken) => RunAsync(async client =>
{
    await client.LogoutAsync(cancellationToken);
    await Console.Error.WriteLineAsync("已退出登录");
}, cancellationToken));
var root = new RootCommand("Aether B站直播弹幕") { login, watch, logout };
return await root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = Console.Error });

static async Task<int> RunAsync(Func<AetherClient, Task> action, CancellationToken cancellationToken)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
    builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    // The CLI prints its own status and errors to stderr; its log records only go to the file.
    builder.Logging.AddFilter<ConsoleLoggerProvider>("Aether.Cli", LogLevel.None);
    var dataDirectory = DataDirectory.Locate();
    builder.Logging.AddAetherFileLogging(dataDirectory);
    builder.Services.AddSingleton(services => new AetherClient(
        services.GetRequiredService<ILogger<AetherClient>>(), dataDirectory));
    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aether.Cli");
    try
    {
        logger.LogInformation("CLI 已启动");
        await action(host.Services.GetRequiredService<AetherClient>());
        return 0;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
    catch (Exception error)
    {
        logger.LogError(error, "CLI 操作失败");
        await Console.Error.WriteLineAsync($"错误：{error.Message}");
        return 1;
    }
}

static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

static async Task ShowQrCodeAsync(string content)
{
    await Console.Error.WriteLineAsync("请用 B站 App 扫码并确认登录（Ctrl+C 取消；过期后自动换码）");
    using var data = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.M);
    using var code = new AsciiQRCode(data);
    await Console.Error.WriteLineAsync(code.GetGraphicSmall());
}
