using Aether.Core;

namespace Aether.Cli;

internal static class WatchCommand
{
    public static async Task RunAsync(AetherClient client, Settings settings, long roomId,
        Func<string, Task> showQrCode, CancellationToken cancellationToken)
    {
        try
        {
            if (settings.WelcomeAnimationEnabled.Value) await client.ApplyWebSettingsAsync(settings);
            if (client.WebService.Status.Running)
                await Console.Out.WriteLineAsync($"视频页：{client.WebService.Status.VideoAddress}");
            await foreach (var update in client.WatchAsync(roomId, cancellationToken))
            {
                switch (update)
                {
                    case WatchQrCode qr:
                        await showQrCode(qr.Content);
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
                        await Console.Out.WriteLineAsync(DanmakuOutput.Format(danmaku));
                        break;
                }
            }
        }
        finally { await client.WebService.StopAsync(); }
    }
}
