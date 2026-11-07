using Aether.Core;
using Aether.Core.Tests.Support;

namespace Aether.Cli.Tests;

public sealed class WatchCommandTests
{
    [Theory]
    [InlineData("entry_animation.enabled")]
    [InlineData("gift_animation.enabled")]
    public async Task Watch_starts_no_web_service_and_prints_no_video_page_even_with_an_animation_on(string animation)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            [animation] = "true", ["web_service.port"] = Aether.Core.Tests.WelcomeAnimationTests.FreePort().ToString()
        });
        var output = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(captured));
        var watching = WatchCommand.RunAsync(h.Client, 6, _ => throw new InvalidOperationException("Already logged in"), h.Stop.Token);
        try
        {
            await h.WaitForLogAsync("已连接弹幕服务器");
            await h.Server.PushRoomMessageAsync(WatchHarness.SyncDanmaku);
            await Eventually.TrueAsync(() => captured.ToString().Contains("同步"));
            Assert.False(h.Client.WebService.Status.Running);
            Assert.DoesNotContain("视频页", captured.ToString());
        }
        finally
        {
            await h.Stop.CancelAsync();
            await watching;
            Console.SetOut(output);
        }
    }
}
