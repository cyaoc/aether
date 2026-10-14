using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.BlindGiftV2Fixture;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class WelcomeGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task Nested_guard_without_top_level_guard_queues_and_sends_one_welcome(long? topLevelGuard)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        EnableGuardWelcome(h);
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        var progress = await h.PushAndCollectAsync(updates, EntryV2Fixture.Message(privilegeAndUserInfo:
        [
            .. (topLevelGuard is { } guard ? Integer(16, guard) : []),
            .. Bytes(22, Bytes(6, Integer(1, 2)))
        ]));
        var queued = Assert.Single(progress.OfType<SendQueueUpdate>(), item => item.State == SendQueueItemState.Queued);
        Assert.Equal(SendQueueItemKind.Welcome, queued.Kind);
        Assert.Equal(WelcomeCondition.Guard, queued.WelcomeCondition);
        Assert.Equal("提督", queued.WelcomeValue);
        Assert.Equal("欢迎提督测试观众", queued.Text);
        Assert.Equal(queued.Text, (await NextSendAsync(sent))["msg"]);
        await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎观众");
        Assert.Single(h.Logger.Entries, log => log.Message.Contains("进队：") && log.Message.EndsWith("命中大航海身份提督"));
        Assert.Equal(1, SendAttempts(h));
    }

    [Theory]
    [InlineData("舰长", 3, "舰长")]
    [InlineData("舰长", 2, "提督")]
    [InlineData("舰长", 1, "总督")]
    [InlineData("提督", 3, null)]
    [InlineData("提督", 2, "提督")]
    [InlineData("提督", 1, "总督")]
    [InlineData("总督", 3, null)]
    [InlineData("总督", 2, null)]
    [InlineData("总督", 1, "总督")]
    [InlineData("关闭", 1, null)]
    [InlineData("关闭", 2, null)]
    [InlineData("关闭", 3, null)]
    [InlineData("舰长", 0, null)]
    public async Task Guard_threshold_welcomes_only_qualifying_entries(string threshold, long guard, string? guardName)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        EnableGuardWelcome(h, threshold);
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(privilegeAndUserInfo: Bytes(22, Bytes(6, Integer(1, guard)))));
        if (guardName is not null)
        {
            var form = await NextSendAsync(sent);
            Assert.Equal($"欢迎{guardName}测试观众", form["msg"]);
            Assert.DoesNotContain(form.Keys, key => key.StartsWith("reply") || key.StartsWith("replay"));
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎观众 测试观众（10001）");
            Assert.Contains(h.Logger.Entries, entry => entry.Level == LogLevel.Information
                && entry.Message.EndsWith($"进队：观众 测试观众（10001），命中大航海身份{guardName}"));
        }
        else await AssertNoSendAsync(sent);
    }

    public static TheoryData<byte[], string?> GuardSources => new()
    {
        { [.. Integer(16, 3), .. Bytes(22, Bytes(6, Integer(1, 2)))], "提督" },
        { [.. Bytes(22, Bytes(6, Integer(1, 2))), .. Integer(16, 3)], "提督" },
        { [.. Integer(16, 3), .. Bytes(22, Bytes(6, Integer(1, 2))), .. Bytes(22, Bytes(4, Integer(1, 45)))], "提督" },
        { [.. Bytes(22, Bytes(4, Integer(1, 45))), .. Bytes(22, Bytes(6, Integer(1, 2))), .. Integer(16, 3)], "提督" },
        { [.. Integer(16, 2), .. Bytes(22, Bytes(6, Integer(1, 0)))], null },
        { [.. Bytes(22, Bytes(6, Integer(1, 0))), .. Integer(16, 2)], null },
        { [.. Integer(16, 2), .. Bytes(22, Bytes(6, []))], null },
        { [.. Integer(16, 2), .. Bytes(22, Bytes(6, Integer(1, 0))), .. Bytes(22, [])], null },
        { [.. Integer(16, 2), .. Bytes(22, Bytes(6, [])), .. Bytes(22, [])], null },
        { Integer(16, 2), "提督" },
        { [.. Integer(16, 2), .. Bytes(22, [])], "提督" },
        { [.. Integer(16, 2), .. Bytes(22, Bytes(4, Integer(1, 45)))], "提督" },
        { [], null }, // The fixture's worn fan club has guard_level=1; it cannot supply this room's 大航海身份.
        { Bytes(22, Bytes(3, Integer(11, 1))), null },
        { [.. Integer(16, 2), .. Bytes(22, Bytes(6, Integer(1, 99)))], null }
    };

    [Theory]
    [MemberData(nameof(GuardSources))]
    public async Task Nested_guard_presence_controls_fallback_independent_of_field_order(byte[] privilegeAndUserInfo, string? guardName)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        EnableGuardWelcome(h);
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        var progress = await h.PushAndCollectAsync(updates, EntryV2Fixture.Message(privilegeAndUserInfo: privilegeAndUserInfo));
        var queued = progress.OfType<SendQueueUpdate>().Where(item => item.State == SendQueueItemState.Queued);
        if (guardName is null)
        {
            Assert.Empty(queued);
            await AssertNoSendAsync(sent);
        }
        else
        {
            Assert.Equal(guardName, Assert.Single(queued).WelcomeValue);
            Assert.Equal($"欢迎{guardName}测试观众", (await NextSendAsync(sent))["msg"]);
        }
        Assert.DoesNotContain(h.Logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nested_wealth_can_welcome_below_guard_threshold_and_unknown_fields_are_skipped(bool splitUserInfo)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        EnableGuardWelcome(h, "总督", wealthLevel: "45");
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        var guard = Bytes(6, [.. Integer(7, 99), .. Integer(1, 2)]);
        var wealth = Bytes(4, [.. Integer(1, 45), .. Text(2, "未使用")]);
        var progress = await h.PushAndCollectAsync(updates, EntryV2Fixture.Message(privilegeAndUserInfo: splitUserInfo
            ? [.. Bytes(22, wealth), .. Bytes(22, guard)]
            : Bytes(22, [.. guard, .. Bytes(27, []), .. wealth])));
        var queued = Assert.Single(progress.OfType<SendQueueUpdate>(), item => item.State == SendQueueItemState.Queued);
        Assert.Equal(WelcomeCondition.WealthLevel, queued.WelcomeCondition);
        Assert.Equal("45", queued.WelcomeValue);
        Assert.Equal("欢迎提督测试观众", queued.Text);
        Assert.Equal(queued.Text, (await NextSendAsync(sent))["msg"]);
        Assert.Contains(h.Logger.Entries, entry => entry.Message.EndsWith("命中荣耀等级45"));
        Assert.DoesNotContain(h.Logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // Other conditions are off so only the 大航海身份 can welcome.
    private static void EnableGuardWelcome(WatchHarness h, string guard = "舰长", string wealthLevel = "0") =>
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.guard"] = guard,
            ["welcome.uids"] = "[]", ["welcome.fan_club_level"] = "0", ["welcome.wealth_level"] = wealthLevel
        });
}
