using Aether.Core;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Cli.Tests;

public sealed class WelcomeTextsTests
{
    [Fact]
    public async Task Watch_sends_a_configured_welcome_text_with_that_texts_mention()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true",
            ["welcome.texts"] = Settings.TextListText(["欢迎{昵称}", "{@}欢迎回家"])
        });
        var sent = CaptureSends(h);
        var watching = WatchCommand.RunAsync(h.Client, Settings.Load(h.DataDirectory), 6,
            _ => throw new InvalidOperationException("Already logged in"), h.Stop.Token);
        try
        {
            await h.WaitForLogAsync("已连接弹幕服务器");
            await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message());
            var form = await NextSendAsync(sent);
            Assert.Contains(form["msg"], new[] { "欢迎测试观众", "欢迎回家" });
            Assert.Equal(form["msg"] == "欢迎回家" ? "10001" : null, form.GetValueOrDefault("reply_mid"));
            await h.WaitForLogAsync("发送弹幕成功：欢迎观众");
        }
        finally { await h.Stop.CancelAsync(); await watching; }
        Assert.Equal(1, SendAttempts(h));
    }
}
