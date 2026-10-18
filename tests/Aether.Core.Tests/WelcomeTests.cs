using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.BlindGiftV2Fixture;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class WelcomeTests
{
    [Theory]
    [InlineData(0, "欢迎测试观众", false, null)]
    [InlineData(1, "欢迎回家", true, null)]
    [InlineData(2, "", false, "欢迎语拼出来是空的")]
    [InlineData(3, "这条欢迎语肯定超过十个汉字", false, "欢迎语超过弹幕字数上限 20")]
    public async Task Each_match_uses_the_selected_text_and_mention_without_redrawing_rejected_texts(
        int selected, string text, bool mention, string? reason)
    {
        await using var h = new WatchHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.uids"] = "[10001]",
            ["welcome.texts"] = Settings.TextListText(["欢迎{昵称}", "{@}欢迎回家", "{大航海}", "这条欢迎语肯定超过十个汉字"])
        });
        var random = new FixedRandom(selected);
        var welcome = new Welcome(Settings.Load(h.DataDirectory), random);
        for (var i = 0; i < 2; i++)
        {
            var result = welcome.Evaluate(new(10001, "测试观众", 1, 0, null, 0), 20);
            Assert.NotNull(result);
            Assert.Equal(text, result.Text);
            Assert.Equal(mention, result.Mention);
            Assert.Equal(reason, result.RejectionReason);
        }
        Assert.Equal([4, 4], random.Bounds);
    }

    private sealed class FixedRandom(int selected) : Random
    {
        public List<int> Bounds { get; } = [];
        public override int Next(int maxValue) { Bounds.Add(maxValue); return selected; }
    }

    [Fact]
    public async Task An_expired_retry_does_not_consume_the_next_welcomes_retry()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h);
        const string limited = "{\"code\":10031,\"message\":\"太快\"}";
        var sent = CaptureSends(h, limited, limited, Accepted);
        var release = HoldSends(h, attempt: 1);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            await NextSendAsync(sent);
            h.Time.Advance(TimeSpan.FromSeconds(25));
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(nickname: "后来观众"));
            release.SetResult();
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
            h.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("欢迎舰长后来观众", (await NextSendAsync(sent))["msg"]);
            await h.WaitForLogAsync("欢迎已过期");
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5), count: 2);
            h.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("欢迎舰长后来观众", (await NextSendAsync(sent))["msg"]);
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎观众 后来观众");
        }
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("丢掉 0 条"));
    }

    [Theory]
    [InlineData("{\"code\":1003212,\"message\":\"超出限制长度\"}", false)]
    [InlineData(NetworkFailure, false)]
    [InlineData("{\"code\":10031,\"message\":\"太快\"}", true)]
    public async Task Failed_welcomes_are_discarded_after_at_most_one_frequency_retry(string response, bool retry)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h);
        var sent = CaptureSends(h, response);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            await NextSendAsync(sent);
            if (retry)
            {
                await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
                h.Time.Advance(TimeSpan.FromSeconds(5));
                await NextSendAsync(sent);
            }
            await h.WaitForLogAsync(LogLevel.Warning, "发送弹幕失败，已丢弃：欢迎观众");
            h.Time.Advance(TimeSpan.FromSeconds(5));
            await AssertNoSendAsync(sent);
        }
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("丢掉 0 条"));
        Assert.Equal(retry ? 2 : 1, SendAttempts(h));
    }

    [Fact]
    public async Task Welcomes_share_FIFO_with_queries_and_announcements_without_merging_same_viewers()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h);
        var sent = CaptureSends(h);
        var release = HoldSends(h, attempt: 1);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            await NextSendAsync(sent);
            await DanmakuAsync(h, updates);
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Gift(h.Time.GetUtcNow(), price: 6500));
            h.Time.Advance(TimeSpan.FromSeconds(3));
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            release.SetResult();
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
            await DanmakuAsync(h, updates); // Still merges with the waiting query after the welcome finishes.
            h.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal("今日投入50电池 赚15电池", (await NextSendAsync(sent))["msg"]);
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1), count: 2);
            h.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal("本次投入50电池 赚15电池", (await NextSendAsync(sent))["msg"]);
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(3));
            h.Time.Advance(TimeSpan.FromSeconds(3));
            Assert.Equal("欢迎舰长测试观众", (await NextSendAsync(sent))["msg"]);
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎");
            // A queued welcome currently in flight is counted too when the connection ends.
            HoldSends(h);
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(nickname: "另一个名字"));
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1), count: 3);
            h.Time.Advance(TimeSpan.FromSeconds(1));
            await NextSendAsync(sent);
        }
        Assert.Equal(5, SendAttempts(h));
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("丢掉 1 条"));
    }

    // The nested guards are malformed, not missing: falling back to privilege_type 2 would welcome 提督 first.
    public static TheoryData<string> MalformedEntries => new()
    {
        "{\"cmd\":\"INTERACT_WORD_V2\",\"data\":{\"pb\":\"invalid\"}}",
        "{\"cmd\":\"INTERACT_WORD_V2\",\"data\":{}}",
        EntryV2Fixture.Message(privilegeAndUserInfo: [.. Integer(16, 2), .. Integer(22, 2)]),
        EntryV2Fixture.Message(privilegeAndUserInfo: [.. Integer(16, 2), .. Bytes(22, Integer(6, 2))]),
        EntryV2Fixture.Message(privilegeAndUserInfo: [.. Integer(16, 2), .. Bytes(22, Bytes(6, Bytes(1, [])))]),
        EntryV2Fixture.Message(privilegeAndUserInfo: [.. Integer(16, 2), .. Bytes(22, Bytes(6, [0x08, 0x80]))]),
        EntryV2Fixture.Message(privilegeAndUserInfo: [.. Integer(16, 2), .. Bytes(22, [0x32, 0x02, 0x08])]),
        EntryV2Fixture.Message(privilegeAndUserInfo: [.. Integer(16, 2), .. Bytes(22, Bytes(6, Integer(1, -1)))]),
        EntryV2Fixture.Message(privilegeAndUserInfo: [.. Integer(16, -1), .. Bytes(22, Bytes(6, Integer(1, 2)))])
    };

    [Theory]
    [MemberData(nameof(MalformedEntries))]
    public async Task Malformed_entries_warn_without_stopping_later_entries(string message)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h);
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, message, EntryV2Fixture.Message());
        Assert.Equal("欢迎舰长测试观众", (await NextSendAsync(sent))["msg"]);
        Assert.Single(h.Logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("无法解析"));
    }

    [Theory]
    [InlineData(29999, true)]
    [InlineData(30000, false)]
    public async Task Welcome_age_is_checked_after_waiting_for_the_previous_send(int ageMilliseconds, bool sends)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h);
        var sent = CaptureSends(h);
        var release = HoldSends(h, attempt: 1);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        h.Time.Advance(TimeSpan.FromMilliseconds(ageMilliseconds - 1000));
        release.SetResult();
        await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：回复");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        if (sends) Assert.Equal("欢迎舰长测试观众", (await NextSendAsync(sent))["msg"]);
        else
        {
            await h.WaitForLogAsync("欢迎已过期");
            await AssertNoSendAsync(sent);
        }
    }

    [Theory]
    [InlineData(29999, true)]
    [InlineData(30000, false)]
    public async Task Rate_limit_retry_checks_age_again(int ageMilliseconds, bool retries)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h);
        var sent = CaptureSends(h, "{\"code\":10031,\"message\":\"太快\"}", Accepted);
        var release = HoldSends(h, attempt: 1);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        await NextSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(ageMilliseconds - 5000));
        release.SetResult();
        await h.WaitForLogAsync(LogLevel.Warning, "重试观众 测试观众（10001）的欢迎");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        h.Time.Advance(TimeSpan.FromSeconds(5));
        if (retries)
        {
            Assert.Equal("欢迎舰长测试观众", (await NextSendAsync(sent))["msg"]);
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎");
        }
        else
        {
            await h.WaitForLogAsync("欢迎已过期");
            await AssertNoSendAsync(sent);
        }
    }

    [Theory]
    [InlineData("{@}欢迎回家", "测试观众", 20, "欢迎回家", true)]
    [InlineData("欢迎{大航海}{昵称}", "一二三四五六七八九十", 20, "欢迎舰长一二三四五六", false)]
    [InlineData("欢迎{大航海}{昵称}", "abcdefghijklmnop", 20, "欢迎舰长abcdefghijkl", false)]
    [InlineData("欢迎{大航海}{昵称}", "ab🌙🌙🌙🌙🌙🌙", 19, "欢迎舰长ab🌙🌙🌙🌙", false)]
    [InlineData("{{{昵称}}}{{@}}", "测试观众", 20, "{测试观众}{@}", false)]
    [InlineData("{昵称}/{昵称}", "一二三四五六", 9, "一二/一二", false)]
    [InlineData("欢迎{大航海}{昵称}", "测试观众", 8, "欢迎舰长", false)]
    public async Task Text_is_rendered_truncated_by_code_point_and_optionally_mentions_the_viewer(
        string text, string nickname, int limit, string expected, bool mention)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h, text, limit);
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(uid: 9876543210, nickname: nickname));
        var form = await NextSendAsync(sent);
        Assert.Equal(expected, form["msg"]);
        Assert.DoesNotContain("�", form["msg"]);
        Assert.DoesNotContain("replay_dmid", form);
        if (mention) Assert.Equal("9876543210", form["reply_mid"]);
        else Assert.DoesNotContain(form.Keys, key => key.StartsWith("reply") || key.StartsWith("replay"));
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(10001, 2, true)]
    [InlineData(10001, 3, true)]
    [InlineData(10001, 0, true)]
    [InlineData(10001, 1, false)]
    public async Task Unknown_viewers_other_interactions_and_disabled_welcomes_do_not_enqueue(long uid, long messageType, bool enabled)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.enabled"] = Settings.BooleanText(enabled) });
        var sent = CaptureSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(uid: uid, messageType: messageType));
            if (!enabled) await h.PushAndSyncAsync(updates, "{\"cmd\":\"INTERACT_WORD_V2\",\"data\":{\"pb\":\"invalid\"}}");
        }
        Assert.Equal(0, SendAttempts(h));
        Assert.DoesNotContain(h.Logger.Entries, entry => entry.Message.StartsWith("[发送队列") || entry.Level == LogLevel.Warning);
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("丢掉 0 条"));
    }

    [Fact]
    public async Task Same_text_waits_five_seconds_and_repeated_entries_are_not_merged()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h, "{@}欢迎回家");
        var sent = CaptureSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(), EntryV2Fixture.Message(), EntryV2Fixture.Message(uid: 20002));
            Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
            for (var count = 1; count <= 2; count++)
            {
                await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5), count);
                h.Time.Advance(TimeSpan.FromMilliseconds(4999));
                await AssertNoSendAsync(sent);
                h.Time.Advance(TimeSpan.FromMilliseconds(1));
                Assert.Equal(count == 1 ? "10001" : "20002", (await NextSendAsync(sent))["reply_mid"]);
            }
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎观众 测试观众（20002）");
        }
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("丢掉 0 条"));
    }

    [Fact]
    public async Task Pending_welcomes_survive_reconnect_and_are_counted_and_cleared_at_connection_end()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h, "{@}欢迎回家");
        var sent = CaptureSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            await NextSendAsync(sent);
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎");
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(uid: 20002), EntryV2Fixture.Message(uid: 20002), EntryV2Fixture.Message(uid: 30003));
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(4));
            h.Time.Advance(TimeSpan.FromSeconds(4));
            Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功：欢迎观众 测试观众（20002）");
        }
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("丢掉 2 条"));
        await using var next = await h.WatchConnectedAsync();
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await AssertNoSendAsync(sent);
    }

    [Fact]
    public async Task Entry_format_notices_are_once_per_room_connection_even_across_reconnects()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h);
        // An entry effect usually arrives first; it must not stand in for the room's INTERACT_WORD format (#154).
        var messages = new[]
        {
            EntryEffectFixture.Message("ENTRY_EFFECT_MUST_RECEIVE", guard: 0, wealthLevel: 0, fanClubLevel: 0),
            EntryV2Fixture.Message(guard: 0), "{\"cmd\":\"INTERACT_WORD\"}", "{\"cmd\":\"INTERACT_WORD_V3\"}"
        };
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, messages);
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            await h.PushAndSyncAsync(updates, messages);
            Assert.Single(h.Logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("进场消息为 V2"));
            Assert.Single(h.Logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("进场消息格式可能变了"));
        }
        await using var next = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(next, messages);
        Assert.Equal(2, h.Logger.Entries.Count(entry => entry.Message.Contains("进场消息为 V2")));
        Assert.Equal(2, h.Logger.Entries.Count(entry => entry.Message.Contains("进场消息格式可能变了")));
    }

    [Fact]
    public async Task Disabled_welcomes_say_nothing_about_entry_formats()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using (var updates = await h.WatchConnectedAsync())
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(), "{\"cmd\":\"INTERACT_WORD\"}", "{\"cmd\":\"INTERACT_WORD_V3\"}");
        Assert.DoesNotContain(h.Logger.Entries, entry => entry.Message.Contains("进场消息"));
        Assert.DoesNotContain(h.Http.Requests, request => request.Uri.AbsolutePath.EndsWith("getInfoByUser"));
        Assert.Equal(0, SendAttempts(h));
    }

    private static void Enable(WatchHarness h, string text = "欢迎{大航海}{昵称}", int limit = 20)
    {
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.texts"] = Settings.TextListText([text])
        });
        h.Http.SetDanmakuLengthLimit(limit);
    }

    [Theory]
    [InlineData("{大航海}", 20)] // Not a guard, so nothing is left.
    [InlineData("{@}{昵称}", 1)] // The nickname's first character already exceeds the limit.
    public async Task Welcomes_that_render_blank_are_not_sent(string text, int limit)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Enable(h, text, limit);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.uids"] = "[10001]" });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(guard: 0));
        await h.WaitForLogAsync(LogLevel.Warning, "没进队：观众 测试观众（10001），命中UID，但欢迎语拼出来是空的");
        await AssertNoSendAsync(sent);
    }
}
