using Aether.Core;

namespace Aether.Cli;

internal static class WatchCommand
{
    /// <summary>No web service: the 直播画面功能 that need one are GUI-only (ADR 0003).</summary>
    public static async Task RunAsync(AetherClient client, long roomId,
        Func<string, Task> showQrCode, CancellationToken cancellationToken)
    {
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
}
