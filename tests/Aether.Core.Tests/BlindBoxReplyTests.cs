using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.BlindGiftFixture;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class BlindBoxReplyTests
{
    [Fact]
    public async Task Daily_and_monthly_queries_queue_separately_and_only_duplicates_of_the_same_period_merge()
    {
        await using var h = new WatchHarness();
        h.Time.SetUtcNow(new DateTimeOffset(2024, 12, 1, 12, 0, 0, TimeSpan.FromHours(8)));
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.announce"] = "false" });
        var sent = CaptureSends(h);
        var release = HoldSends(h, attempt: 1);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h.Time.GetUtcNow()));
        await DanmakuAsync(h, updates, uid: 20002);
        await NextSendAsync(sent);
        await DanmakuAsync(h, updates, id: "daily");
        await DanmakuAsync(h, updates, "本月盲盒", id: "monthly");
        await DanmakuAsync(h, updates, id: "daily-duplicate");
        await DanmakuAsync(h, updates, "本月盲盒", id: "monthly-duplicate");
        await h.PushAndSyncAsync(updates);
        release.SetResult();
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        var daily = await NextSendAsync(sent);
        Assert.Equal("daily", daily["replay_dmid"]);
        Assert.Equal("今日投入50电池 亏35电池", daily["msg"]);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1), count: 2);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        var monthly = await NextSendAsync(sent);
        Assert.Equal("monthly", monthly["replay_dmid"]);
        Assert.Equal("本月投入50电池 亏35电池", monthly["msg"]);
        await h.WaitForLogAsync("发送弹幕成功：回复观众 观众（10001）：本月");
        Assert.Equal(2, h.Logger.Entries.Count(e => e.Level == LogLevel.Debug && e.Message.Contains("合并到这一项")));
        foreach (var period in new[] { "今日", "本月" })
        {
            Assert.Contains(h.Logger.Entries, e => e.Message.Contains(period) && e.Message.Contains("进队：观众 观众（10001）"));
            Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains(period) && e.Message.Contains("合并到这一项"));
        }
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await AssertNoSendAsync(sent);
        await DanmakuAsync(h, updates, "本月盲盒", id: "monthly-next");
        Assert.Equal("monthly-next", (await NextSendAsync(sent))["replay_dmid"]);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(3, 3)]
    public async Task Text_X_Y_X_waits_for_both_deadlines_and_later_different_text_cannot_pass(int interval, int remaining)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var settings = new Dictionary<string, string> { ["blind_box.announce"] = "false" };
        // Leave the default interval in place to exercise it through WatchAsync.
        if (interval != 1) settings["send.interval_seconds"] = interval.ToString(CultureInfo.InvariantCulture);
        Settings.Save(h.DataDirectory, settings);
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h.Time.GetUtcNow(), uid: 20002), Gift(h.Time.GetUtcNow(), uid: 40004, price: 9000));
        await DanmakuAsync(h, updates);
        var first = await NextSendAsync(sent);
        await h.WaitForLogAsync("发送弹幕成功");
        await DanmakuAsync(h, updates, uid: 20002);
        await DanmakuAsync(h, updates, uid: 30003);
        await DanmakuAsync(h, updates, uid: 40004);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(interval));
        h.Time.Advance(TimeSpan.FromMilliseconds(interval * 1000 - 1));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        var second = await NextSendAsync(sent);
        Assert.Equal("20002", second["reply_mid"]);
        Assert.NotEqual(first["msg"], second["msg"]);
        await h.WaitForLogAsync("（20002）");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(remaining), count: interval == remaining ? 2 : 1);
        h.Time.Advance(TimeSpan.FromMilliseconds(remaining * 1000 - 1));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        var third = await NextSendAsync(sent);
        Assert.Equal("30003", third["reply_mid"]);
        Assert.Equal(first["msg"], third["msg"]);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(interval), count: interval == remaining ? 3 : 2);
        h.Time.Advance(TimeSpan.FromSeconds(interval));
        Assert.Equal("40004", (await NextSendAsync(sent))["reply_mid"]);
    }

    [Fact]
    public async Task Reconnect_interrupting_the_retry_does_not_grant_another_rate_limit_retry()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h, "{\"code\":10031,\"message\":\"太快\"}");
        HoldSends(h, attempt: 2);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await h.WaitForLogAsync(LogLevel.Warning, "5 秒后重试");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await NextSendAsync(sent); // The retry, held until the reconnect interrupts it.
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        // The interrupted retry still waits five seconds for the same text from the interruption.
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(4));
        h.Time.Advance(TimeSpan.FromMilliseconds(3999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
        await h.WaitForLogAsync(LogLevel.Warning, "发送弹幕失败");
        // The interrupted HTTP attempt is resent under #46; its completed failure still exhausts the single retry.
        Assert.Single(h.Logger.Entries, e => e.Message.Contains("秒后重试观众"));
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await AssertNoSendAsync(sent);
        Assert.Equal(3, SendAttempts(h));
    }

    [Fact]
    public async Task Reconnect_keeps_the_send_interval_and_retry_budget_and_the_room_connection_end_clears_them()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h, "{\"code\":10031,\"message\":\"太快\"}");
        await using (var updates = await h.WatchConnectedAsync())
        {
            await DanmakuAsync(h, updates);
            await NextSendAsync(sent);
            await h.WaitForLogAsync(LogLevel.Warning, "5 秒后重试");
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
            await DanmakuAsync(h, updates, uid: 20002);
            h.Time.Advance(TimeSpan.FromSeconds(2));
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            await DanmakuAsync(h, updates);
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(2), count: 3); // Login armed two polling timers.
            h.Time.Advance(TimeSpan.FromMilliseconds(1999));
            await AssertNoSendAsync(sent);
            h.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
            await h.WaitForLogAsync(LogLevel.Warning, "发送弹幕失败");
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5), count: 2);
            h.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
            await Eventually.TrueAsync(() => h.Logger.Entries.Count(e => e.Message.Contains("秒后重试观众")) == 2);
        }
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("丢掉 1 条"));
        Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败"));
        // A new room connection has neither the old pending viewer nor the old send deadline.
        await using var next = await h.WatchConnectedAsync();
        await DanmakuAsync(h, next, uid: 20002);
        Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
    }

    [Theory]
    [InlineData("{\"code\":0,\"message\":\"f\"}")]
    [InlineData("{\"code\":1003212,\"message\":\"超长\"}")]
    [InlineData(NetworkFailure)]
    public async Task Ordinary_failure_still_delays_the_same_text_for_five_seconds(string failure)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h, failure, Accepted);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await h.WaitForLogAsync(LogLevel.Warning, "发送弹幕失败");
        await DanmakuAsync(h, updates);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        h.Time.Advance(TimeSpan.FromMilliseconds(4999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
        await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功");
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("秒后重试观众"));
    }

    [Theory]
    [InlineData(10031, "太快", true)]
    [InlineData(10030, "太快", true)]
    [InlineData(0, "msg in 1s", true)]
    [InlineData(0, "msg repeat", true)]
    [InlineData(10031, "太快", false)]
    [InlineData(10030, "太快", false)]
    [InlineData(0, "msg in 1s", false)]
    [InlineData(0, "msg repeat", false)]
    [InlineData(10031, "太快", true, 7)]
    public async Task Rate_limits_wait_for_both_pacing_rules_and_retry_the_head_only_once(int code, string message, bool retrySucceeds, int interval = 3)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        // A longer configured interval still wins over the same-text window.
        var wait = Math.Max(interval, 5);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = interval.ToString(CultureInfo.InvariantCulture) });
        var limited = JsonSerializer.Serialize(new { code, message });
        var sent = CaptureSends(h, limited, retrySucceeds ? Accepted : limited, Accepted);
        var release = HoldSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        var first = await NextSendAsync(sent);
        h.Time.Advance(TimeSpan.FromSeconds(2));
        release.SetResult();
        await h.WaitForLogAsync(LogLevel.Warning, "受频率限制");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(wait));
        var warning = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("受频率限制"));
        Assert.Contains($"（{code}）：{message}", warning.Message);
        Assert.Contains($"{wait} 秒后重试", warning.Message);
        Assert.Contains("的回复", warning.Message);
        await DanmakuAsync(h, updates, uid: 20002);
        await DanmakuAsync(h, updates, id: "second-question");
        h.Time.Advance(TimeSpan.FromMilliseconds(wait * 1000 - 1));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        var retry = await NextSendAsync(sent);
        Assert.Equal(first["reply_mid"], retry["reply_mid"]);
        Assert.Equal(first["replay_dmid"], retry["replay_dmid"]);
        await h.WaitForLogAsync(retrySucceeds ? LogLevel.Information : LogLevel.Warning,
            retrySucceeds ? "发送弹幕成功" : "发送弹幕失败");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(wait), count: 2);
        h.Time.Advance(TimeSpan.FromMilliseconds(wait * 1000 - 1));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
        await h.WaitForLogAsync(LogLevel.Information, "（20002）");
        h.Time.Advance(TimeSpan.FromSeconds(wait));
        await AssertNoSendAsync(sent);
        Assert.Equal(retrySucceeds ? 0 : 1, h.Logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败")));
        if (!retrySucceeds)
            Assert.Contains(message, Assert.Single(h.Logger.Entries, e => e.Message.Contains("发送弹幕失败")).Message);
        Assert.Equal(3, SendAttempts(h));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task Same_text_queries_wait_from_send_end_even_for_different_viewers(int interval)
    {
        var wait = Math.Max(interval, 5);
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = interval.ToString(CultureInfo.InvariantCulture) });
        var sent = CaptureSends(h);
        var release = HoldSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        release.SetResult();
        await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功");
        await DanmakuAsync(h, updates);
        await DanmakuAsync(h, updates);
        await DanmakuAsync(h, updates, uid: 20002);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(wait));
        h.Time.Advance(TimeSpan.FromMilliseconds(wait * 1000 - 1));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(wait), count: 2);
        h.Time.Advance(TimeSpan.FromMilliseconds(wait * 1000 - 1));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
        await h.WaitForLogAsync(LogLevel.Information, "（20002）");
        h.Time.Advance(TimeSpan.FromSeconds(wait));
        await AssertNoSendAsync(sent);
        Assert.Equal(3, SendAttempts(h));
    }

    [Fact]
    public async Task Repeated_keywords_merge_while_waiting_and_while_sending_without_losing_other_viewers()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        var release = HoldSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await DanmakuAsync(h, updates);
            Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
            await DanmakuAsync(h, updates);
            await DanmakuAsync(h, updates);
            await DanmakuAsync(h, updates, uid: 20002);
            await DanmakuAsync(h, updates, uid: 20002, id: "second-question");
            await DanmakuAsync(h, updates, uid: 20002, id: "third-question");
            await DanmakuAsync(h, updates, uid: 30003);
            release.SetResult();
            await h.WaitForLogAsync(LogLevel.Information, "（10001）");
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
            h.Time.Advance(TimeSpan.FromSeconds(5));
            var reply = await NextSendAsync(sent);
            Assert.Equal("20002", reply["reply_mid"]);
            Assert.Equal(DanmakuId, reply["replay_dmid"]);
            await h.WaitForLogAsync(LogLevel.Information, "（20002）");
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5), count: 2);
            h.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("30003", (await NextSendAsync(sent))["reply_mid"]);
            await h.WaitForLogAsync(LogLevel.Information, "（30003）");
            h.Time.Advance(TimeSpan.FromSeconds(5));
            await AssertNoSendAsync(sent);
        }
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("丢掉 0 条"));
    }

    [Theory]
    [InlineData(6500, "今日投入50电池 赚15电池")]
    [InlineData(1500, "今日投入50电池 亏35电池")]
    [InlineData(5000, "今日投入50电池 不赚不亏")]
    [InlineData(-1, "今日没有盲盒记录")]
    [InlineData(6510, "今日投入50电池 赚15.1电池")]
    // Amounts are rounded to one decimal first, so the words always match the number shown.
    [InlineData(5003, "今日投入50电池 不赚不亏")]
    [InlineData(5095, "今日投入50电池 赚1电池")]
    [InlineData(1095, "今日投入11电池 不赚不亏", 1095)]
    public async Task Keyword_replies_to_the_viewer_with_todays_tally(int price, string expected, int spend = 5000)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        if (price >= 0) await h.Server.PushRoomMessageAsync(Gift(h.Time.GetUtcNow(), price: price, spend: spend));
        await DanmakuAsync(h, updates, " \t今日盲盒 \n");
        var form = await NextSendAsync(sent);
        Assert.Equal(expected, form["msg"]);
        Assert.Equal("7734200", form["roomid"]);
        Assert.Equal("10001", form["reply_mid"]);
        Assert.Equal("12345678901234567890", form["replay_dmid"]);
        Assert.Equal("saved-csrf", form["csrf"]);
        Assert.Equal("saved-csrf", form["csrf_token"]);
        Assert.Equal(h.Time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), form["rnd"]);
        Assert.Equal("25", form["fontsize"]);
        Assert.Equal("16777215", form["color"]);
        Assert.Equal("1", form["mode"]);
        Assert.Equal("0", form["bubble"]);
        Assert.Equal("0", form["reply_attr"]);
        Assert.Equal("", form["reply_uname"]);
        await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功");
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Information
            && e.Message.Contains("10001") && e.Message.Contains(expected));
    }

    [Theory]
    [InlineData("今日盲盒", "今日", 2024, 11, 27)]
    [InlineData("本月盲盒", "本月", 2024, 2, 1)]
    [InlineData("本月盲盒", "本月", 2024, 12, 1)]
    public async Task Tally_uses_only_this_viewer_this_room_and_the_received_Beijing_period(
        string keyword, string prefix, int year, int month, int day)
    {
        await using var h = new WatchHarness();
        var midnight = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.FromHours(8));
        var end = prefix == "今日" ? midnight.AddDays(1) : midnight.AddMonths(1);
        h.Time.SetUtcNow(midnight.AddDays(prefix == "本月" ? 14 : 0).AddHours(12).AddSeconds(-2));
        h.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-7), "test", "test"));
        await h.LoginAsync();
        var sent = CaptureSends(h);
        h.Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] = """{"code":0,"data":{"room_id":999}}""";
        await using (var otherRoom = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(otherRoom, Gift(midnight, price: 99900));
        }
        h.Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] = """{"code":0,"data":{"room_id":7734200}}""";
        await using var updates = await h.WatchConnectedAsync();
        await h.Server.PushRoomMessageAsync(Gift(midnight.AddSeconds(-1), price: 99900));
        await h.Server.PushRoomMessageAsync(Gift(midnight, price: 6000));
        await h.Server.PushRoomMessageAsync(Gift(end.AddSeconds(-1), price: 7000));
        await h.Server.PushRoomMessageAsync(Gift(end, price: 99900));
        await h.Server.PushRoomMessageAsync(Gift(midnight, uid: 20002, price: 99900));
        await DanmakuAsync(h, updates, keyword);
        Assert.Equal($"{prefix}投入100电池 赚30电池", (await NextSendAsync(sent))["msg"]);
    }

    [Theory]
    [InlineData("今日盲盒呢", 10001, true)]
    [InlineData("今日盲盒", 0, true)]
    [InlineData("今日盲盒", 10001, false)]
    [InlineData("本月盲盒呢", 10001, true)]
    [InlineData("本月盲盒", 0, true)]
    [InlineData("本月盲盒", 10001, false)]
    public async Task Only_exact_keywords_with_known_viewers_and_enabled_tally_trigger(string text, long uid, bool enabled)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = Settings.BooleanText(enabled) });
        var sent = CaptureSends(h);
        await using (var updates = await h.WatchConnectedAsync()) await DanmakuAsync(h, updates, text, uid);
        Assert.False(sent.Reader.TryRead(out _));
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("丢掉 0 条"));
        if (uid == 0) Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("uid 为 0"));
    }

    [Fact]
    public async Task Accounts_own_keyword_triggers_and_custom_keyword_applies_at_next_room_connection()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.daily_keyword"] = "查盲盒" });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates, "今日盲盒");
        await DanmakuAsync(h, updates, " 查盲盒 ", 9876543210);
        Assert.Equal("9876543210", (await NextSendAsync(sent))["reply_mid"]);
        Assert.False(sent.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData("今日盲盒", "今日")]
    [InlineData("本月盲盒", "本月")]
    public async Task Send_queue_waits_the_configured_interval_and_queries_at_send_time_but_keeps_the_trigger_period(string keyword, string prefix)
    {
        await using var h = new WatchHarness();
        var beforeMidnight = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.FromHours(8));
        h.Time.SetUtcNow(beforeMidnight.AddSeconds(-2));
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "7" });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates, keyword, uid: 20002);
        Assert.Equal($"{prefix}没有盲盒记录", (await NextSendAsync(sent))["msg"]);
        await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功");
        await DanmakuAsync(h, updates, keyword);
        await h.PushAndSyncAsync(updates, Gift(beforeMidnight, price: 5010, spend: 4010), Gift(beforeMidnight.AddSeconds(1), price: 99900));
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(7));
        h.Time.Advance(TimeSpan.FromMilliseconds(6999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal($"{prefix}投入40.1电池 赚10电池", (await NextSendAsync(sent))["msg"]);
    }

    [Theory]
    [InlineData("{\"code\":0,\"message\":\"f\"}")]
    [InlineData("{\"code\":0,\"message\":\"k\"}")]
    [InlineData("{\"code\":1003212,\"message\":\"超长\"}")]
    [InlineData("{\"code\":1003212,\"message\":\"msg repeat\"}")]
    [InlineData("{\"code\":0,\"message\":null}")]
    [InlineData("{\"code\":0}")]
    [InlineData(NetworkFailure)]
    public async Task Failed_sends_warn_and_are_discarded_without_retry(string response)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h, response);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await h.WaitForLogAsync(LogLevel.Warning, "发送弹幕失败");
        await NextSendAsync(sent);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await AssertNoSendAsync(sent);
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("发送弹幕成功"));
        var warning = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败"));
        if (response == NetworkFailure) Assert.IsType<HttpRequestException>(warning.Exception);
        else Assert.NotNull(warning.Exception);
        Assert.Equal(1, SendAttempts(h));
    }

    [Fact]
    public async Task Waiting_replies_survive_reconnect_and_are_counted_and_cleared_when_the_room_connection_ends()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await DanmakuAsync(h, updates);
            await NextSendAsync(sent);
            await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功");
            await DanmakuAsync(h, updates, uid: 20002);
            await DanmakuAsync(h, updates, uid: 30003);
            await DanmakuAsync(h, updates, uid: 40004);
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            h.Time.Advance(TimeSpan.FromMilliseconds(999));
            await AssertNoSendAsync(sent);
            h.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Connected>(updates.Current);
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(4));
            h.Time.Advance(TimeSpan.FromMilliseconds(3999));
            await AssertNoSendAsync(sent);
            h.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
            await h.WaitForLogAsync(LogLevel.Information, "（20002）");
        }
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("丢掉 2 条"));
        await using var next = await h.WatchConnectedAsync();
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await AssertNoSendAsync(sent);
    }

    [Fact]
    public async Task Hung_send_does_not_block_receiving_or_heartbeats_and_cancels_when_the_room_connection_ends()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        HoldSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.Server.NextRequestAsync(); // Authentication.
            await DanmakuAsync(h, updates);
            await NextSendAsync(sent);
            await DanmakuAsync(h, updates, "发送卡住时的普通弹幕");
            h.Time.Advance(TimeSpan.FromSeconds(30));
            var heartbeat = await h.Server.NextRequestAsync();
            Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(heartbeat.AsSpan(8)));
            await DanmakuAsync(h, updates, "心跳后仍能接收");
        }
        // Ending the room connection is not a send failure: the interrupted reply is counted with the other dropped ones.
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("发送弹幕失败"));
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("丢掉 1 条"));
    }

    [Theory]
    [InlineData("daily_keyword", "今日")]
    [InlineData("monthly_keyword", "本月")]
    public async Task Keyword_saved_with_surrounding_spaces_still_matches_the_trimmed_danmaku(string key, string prefix)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { [$"blind_box.{key}"] = " 查盲盒 " });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates, " \t查盲盒 \n");
        Assert.Equal($"{prefix}没有盲盒记录", (await NextSendAsync(sent))["msg"]);
    }

    [Fact]
    public async Task Identical_keywords_only_trigger_the_daily_query()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["blind_box.daily_keyword"] = " 查盲盒 ", ["blind_box.monthly_keyword"] = "查盲盒"
        });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates, "查盲盒");
        Assert.Equal("今日没有盲盒记录", (await NextSendAsync(sent))["msg"]);
        await h.WaitForLogAsync("发送弹幕成功");
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await AssertNoSendAsync(sent);
        Assert.Single(h.Logger.Entries, e => e.Message.Contains("进队："));
    }

    [Theory]
    [InlineData("[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,{\"extra\":\"{\\\"id_str\\\":\\\"1\\\"}\"}]", "null", false)]
    [InlineData("[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,{\"extra\":\"{\\\"id_str\\\":\\\"1\\\"}\"}]", "-1", false)]
    [InlineData("[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,{\"extra\":null}]", "10001", true)]
    [InlineData("[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,{\"extra\":\"{\"}]", "10001", true)]
    [InlineData("[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,{\"extra\":\"{\\\"id_str\\\":1}\"}]", "10001", true)]
    public async Task Danmaku_shows_even_when_its_reply_details_are_unreadable(string meta, string uid, bool replies)
    {
        // uid and id_str only serve replies; a bad one must not hide the danmaku itself.
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.Server.PushRoomMessageAsync($$"""{"cmd":"DANMU_MSG","info":[{{meta}},"今日盲盒",[{{uid}},"观众"]]}""");
        Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.Equal("今日盲盒", Assert.IsType<Danmaku>(updates.Current).Content);
        if (replies) Assert.DoesNotContain("replay_dmid", await NextSendAsync(sent));
        else await AssertNoSendAsync(sent);
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Reply_interrupted_by_reconnect_keeps_the_same_text_deadline()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        HoldSends(h, attempt: 1);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        // The interrupted request may have reached B站, so its same-text window runs from the interruption.
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(4));
        h.Time.Advance(TimeSpan.FromMilliseconds(3999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
        await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功");
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("发送弹幕失败"));
    }

    [Fact]
    public async Task Largest_valid_interval_keeps_waiting_without_ending_the_room_connection()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "2147483647" });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await h.WaitForLogAsync(LogLevel.Information, "发送弹幕成功");
        await DanmakuAsync(h, updates, uid: 20002);
        await AssertNoSendAsync(sent);
        await DanmakuAsync(h, updates, "长间隔期间仍能接收");
    }
}
