using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class BlindBoxReplyTests
{
    [Fact]
    public async Task Reconnect_interrupting_the_retry_does_not_grant_another_rate_limit_retry()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h, "{\"code\":10031,\"message\":\"太快\"}");
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        h.Http.Intercept("/msg/send", (_, token, next) => ++attempts == 2
            ? FakeBilibiliHttp.HangUntilCancelledAsync(retryStarted, token) : next());
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await WaitForLogAsync(h, LogLevel.Warning, "暂停 60 秒");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(60), count: 2);
        await AdvanceAMinuteAsync(h, updates);
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        // The retry is still owed, so the full 60 s pause runs again from the interruption.
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(59));
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await DanmakuAsync(h, updates, "保持连接");
        h.Time.Advance(TimeSpan.FromMilliseconds(28999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
        await WaitForLogAsync(h, LogLevel.Warning, "发送弹幕失败");
        // The interrupted HTTP attempt is resent under #46; its completed failure still exhausts the single retry.
        Assert.Single(h.Logger.Entries, e => e.Message.Contains("暂停"));
        await AdvanceAMinuteAsync(h, updates, silent: sent);
        Assert.Equal(3, h.Http.Requests.Count(r => r.Uri.AbsolutePath == "/msg/send"));
    }

    [Fact]
    public async Task Reconnect_keeps_the_rate_limit_deadline_and_retry_budget_and_the_room_connection_end_clears_them()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h, "{\"code\":10031,\"message\":\"太快\"}");
        await using (var updates = await h.WatchConnectedAsync())
        {
            await DanmakuAsync(h, updates);
            await NextSendAsync(sent);
            await WaitForLogAsync(h, LogLevel.Warning, "暂停 60 秒");
            await DanmakuAsync(h, updates, uid: 20002);
            h.Time.Advance(TimeSpan.FromSeconds(20));
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            await DanmakuAsync(h, updates);
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(39));
            h.Time.Advance(TimeSpan.FromMilliseconds(38999));
            await AssertNoSendAsync(sent);
            h.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
            await WaitForLogAsync(h, LogLevel.Warning, "发送弹幕失败");
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
            h.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
            await Eventually.TrueAsync(() => h.Logger.Entries.Count(e => e.Message.Contains("暂停")) == 2);
        }
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("丢掉 1 条"));
        Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败"));
        // A new room connection has neither the old pending viewer nor the old pause.
        await using var next = await h.WatchConnectedAsync();
        await DanmakuAsync(h, next, uid: 20002);
        Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
    }

    [Theory]
    [InlineData("{\"code\":0,\"message\":\"f\"}")]
    [InlineData("{\"code\":1003212,\"message\":\"超长\"}")]
    public async Task Ordinary_failure_does_not_pause_or_cool_down_the_viewer(string failure)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "1" });
        var sent = CaptureSends(h, failure, Accepted);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await WaitForLogAsync(h, LogLevel.Warning, "发送弹幕失败");
        await DanmakuAsync(h, updates);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        h.Time.Advance(TimeSpan.FromMilliseconds(999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
        await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("暂停") || e.Message.Contains("冷却"));
    }

    [Fact]
    public async Task Rate_limit_retry_respects_a_send_interval_longer_than_the_pause()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "70" });
        var sent = CaptureSends(h, "{\"code\":0,\"message\":\"msg repeat\"}", Accepted);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await WaitForLogAsync(h, LogLevel.Warning, "暂停 60 秒");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(70));
        await AdvanceAMinuteAsync(h, updates, silent: sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(9999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
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
    public async Task Rate_limits_pause_every_viewer_for_sixty_seconds_and_retry_the_head_only_once(int code, string message, bool retrySucceeds)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var limited = JsonSerializer.Serialize(new { code, message });
        var sent = CaptureSends(h, limited, retrySucceeds ? Accepted : limited, Accepted);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        var first = await NextSendAsync(sent);
        await WaitForLogAsync(h, LogLevel.Warning, "暂停 60 秒");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(60), count: 2);
        // Hitting a rate limit means the account is close to being muted, so it shows at the default level with B站's reason.
        Assert.Contains(message, Assert.Single(h.Logger.Entries, e => e.Message.Contains("暂停 60 秒")).Message);
        await DanmakuAsync(h, updates, uid: 20002);
        await DanmakuAsync(h, updates);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await DanmakuAsync(h, updates, "暂停时仍能接收");
        h.Time.Advance(TimeSpan.FromMilliseconds(29999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        var retry = await NextSendAsync(sent);
        Assert.Equal(first["reply_mid"], retry["reply_mid"]);
        Assert.Equal(first["replay_dmid"], retry["replay_dmid"]);
        await WaitForLogAsync(h, retrySucceeds ? LogLevel.Information : LogLevel.Warning,
            retrySucceeds ? "发送弹幕成功" : "发送弹幕失败");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        h.Time.Advance(TimeSpan.FromMilliseconds(4999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
        await WaitForLogAsync(h, LogLevel.Information, "（20002）");
        await AdvanceAMinuteAsync(h, updates, silent: sent);
        Assert.Equal(retrySucceeds ? 0 : 1, h.Logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败")));
        if (!retrySucceeds)
            Assert.Contains(message, Assert.Single(h.Logger.Entries, e => e.Message.Contains("发送弹幕失败")).Message);
        Assert.Equal(3, h.Http.Requests.Count(r => r.Uri.AbsolutePath == "/msg/send"));
    }

    [Fact]
    public async Task Viewer_cooldown_starts_at_success_and_expires_at_exactly_five_seconds()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "1" });
        var sent = CaptureSends(h);
        var release = HoldReplies(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        release.SetResult();
        await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
        await DanmakuAsync(h, updates);
        await WaitForLogAsync(h, LogLevel.Debug, "冷却");
        await DanmakuAsync(h, updates, uid: 20002);
        await AssertNoSendAsync(sent);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
        await WaitForLogAsync(h, LogLevel.Information, "（20002）");
        h.Time.Advance(TimeSpan.FromMilliseconds(3999));
        await DanmakuAsync(h, updates);
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        await DanmakuAsync(h, updates);
        Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
        await WaitForLogAsync(h, LogLevel.Information, "（10001）");
        Assert.Equal(2, h.Logger.Entries.Count(e => e.Level == LogLevel.Debug && e.Message.Contains("冷却")));
    }

    [Fact]
    public async Task Repeated_keywords_merge_while_waiting_and_while_sending_without_losing_other_viewers()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        var release = HoldReplies(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await DanmakuAsync(h, updates);
            Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
            await DanmakuAsync(h, updates);
            await DanmakuAsync(h, updates);
            await DanmakuAsync(h, updates, uid: 20002);
            await DanmakuAsync(h, updates, uid: 20002);
            await DanmakuAsync(h, updates, uid: 20002);
            await DanmakuAsync(h, updates, uid: 30003);
            release.SetResult();
            await WaitForLogAsync(h, LogLevel.Information, "（10001）");
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
            h.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
            await WaitForLogAsync(h, LogLevel.Information, "（20002）");
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5), count: 2);
            h.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("30003", (await NextSendAsync(sent))["reply_mid"]);
            await WaitForLogAsync(h, LogLevel.Information, "（30003）");
            h.Time.Advance(TimeSpan.FromSeconds(5));
            await AssertNoSendAsync(sent);
        }
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("丢掉 0 条"));
    }

    [Theory]
    [InlineData(6500, "投入50电池 赚15电池")]
    [InlineData(1500, "投入50电池 亏35电池")]
    [InlineData(5000, "投入50电池 不赚不亏")]
    [InlineData(-1, "今日没有盲盒记录")]
    [InlineData(6510, "投入50电池 赚15.1电池")]
    // Amounts are rounded to one decimal first, so the words always match the number shown.
    [InlineData(5003, "投入50电池 不赚不亏")]
    [InlineData(5095, "投入50电池 赚1电池")]
    [InlineData(1095, "投入11电池 不赚不亏", 1095)]
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
        await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Information
            && e.Message.Contains("10001") && e.Message.Contains(expected));
    }

    [Fact]
    public async Task Tally_uses_only_this_viewer_this_room_and_the_received_Beijing_day()
    {
        await using var h = new WatchHarness();
        var midnight = new DateTimeOffset(2024, 11, 27, 0, 0, 0, TimeSpan.FromHours(8));
        h.Time.SetUtcNow(midnight.AddHours(12).AddSeconds(-2));
        h.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-7), "test", "test"));
        await h.LoginAsync();
        var sent = CaptureSends(h);
        h.Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] = """{"code":0,"data":{"room_id":999}}""";
        await using (var otherRoom = await h.WatchConnectedAsync())
        {
            await h.Server.PushRoomMessageAsync(Gift(midnight, price: 99900));
            await DanmakuAsync(h, otherRoom, "同步");
        }
        h.Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] = """{"code":0,"data":{"room_id":7734200}}""";
        await using var updates = await h.WatchConnectedAsync();
        await h.Server.PushRoomMessageAsync(Gift(midnight.AddSeconds(-1), price: 99900));
        await h.Server.PushRoomMessageAsync(Gift(midnight, price: 6000));
        await h.Server.PushRoomMessageAsync(Gift(midnight.AddDays(1).AddSeconds(-1), price: 7000));
        await h.Server.PushRoomMessageAsync(Gift(midnight.AddDays(1), price: 99900));
        await h.Server.PushRoomMessageAsync(Gift(midnight, uid: 20002, price: 99900));
        await DanmakuAsync(h, updates);
        Assert.Equal("投入100电池 赚30电池", (await NextSendAsync(sent))["msg"]);
    }

    [Theory]
    [InlineData("今日盲盒呢", 10001, true)]
    [InlineData("今日盲盒", 0, true)]
    [InlineData("今日盲盒", 10001, false)]
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
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.keyword"] = "查盲盒" });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates, "今日盲盒");
        await DanmakuAsync(h, updates, " 查盲盒 ", 9876543210);
        Assert.Equal("9876543210", (await NextSendAsync(sent))["reply_mid"]);
        Assert.False(sent.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Send_queue_waits_the_configured_interval_and_queries_at_send_time_but_keeps_the_trigger_day()
    {
        await using var h = new WatchHarness();
        var beforeMidnight = new DateTimeOffset(2024, 11, 27, 23, 59, 59, TimeSpan.FromHours(8));
        h.Time.SetUtcNow(beforeMidnight.AddSeconds(-2));
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "7" });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates, uid: 20002);
        Assert.Equal("今日没有盲盒记录", (await NextSendAsync(sent))["msg"]);
        await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
        await DanmakuAsync(h, updates);
        await h.Server.PushRoomMessageAsync(Gift(beforeMidnight, price: 5010, spend: 4010));
        await h.Server.PushRoomMessageAsync(Gift(beforeMidnight.AddSeconds(1), price: 99900));
        await DanmakuAsync(h, updates, "同步");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(7));
        h.Time.Advance(TimeSpan.FromMilliseconds(6999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("投入40.1电池 赚10电池", (await NextSendAsync(sent))["msg"]);
    }

    [Theory]
    [InlineData("{\"code\":0,\"message\":\"f\"}")]
    [InlineData("{\"code\":0,\"message\":\"k\"}")]
    [InlineData("{\"code\":1003212,\"message\":\"超长\"}")]
    [InlineData("{\"code\":1003212,\"message\":\"msg repeat\"}")]
    [InlineData("{\"code\":0,\"message\":null}")]
    [InlineData("{\"code\":0}")]
    [InlineData("network")]
    public async Task Failed_sends_warn_and_are_discarded_without_retry(string response)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h, response);
        if (response == "network") h.Http.Intercept("/msg/send", (_, _, _) => throw new HttpRequestException("test network failure"));
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await WaitForLogAsync(h, LogLevel.Warning, "发送弹幕失败");
        if (response != "network") await NextSendAsync(sent);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await AssertNoSendAsync(sent);
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("发送弹幕成功"));
        var warning = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败"));
        if (response == "network") Assert.IsType<HttpRequestException>(warning.Exception);
        else Assert.NotNull(warning.Exception);
        Assert.Single(h.Http.Requests, r => r.Uri.AbsolutePath == "/msg/send");
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
            await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
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
            await WaitForLogAsync(h, LogLevel.Information, "（20002）");
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
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Http.Intercept("/msg/send", (_, token, _) => FakeBilibiliHttp.HangUntilCancelledAsync(requested, token));
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.Server.NextRequestAsync(); // Authentication.
            await DanmakuAsync(h, updates);
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
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

    [Fact]
    public async Task Keyword_saved_with_surrounding_spaces_still_matches_the_trimmed_danmaku()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.keyword"] = " 查盲盒 " });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates, "查盲盒");
        Assert.Equal("今日没有盲盒记录", (await NextSendAsync(sent))["msg"]);
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
    public async Task Reply_interrupted_by_reconnect_stays_in_the_send_queue_and_is_resent_one_interval_after_it()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hung = false;
        h.Http.Intercept("/msg/send", (_, token, next) =>
        {
            if (hung) return next();
            hung = true;
            return FakeBilibiliHttp.HangUntilCancelledAsync(requested, token);
        });
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        // The interrupted request may have reached B站, so it counts as a send: the 5 s interval runs from the interruption.
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(4));
        h.Time.Advance(TimeSpan.FromMilliseconds(3999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("10001", (await NextSendAsync(sent))["reply_mid"]);
        await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
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
        await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
        await DanmakuAsync(h, updates, uid: 20002);
        await AssertNoSendAsync(sent);
        await DanmakuAsync(h, updates, "长间隔期间仍能接收");
    }

    /// <summary>Holds each msg/send response until the returned source is set, so the send stays in flight.</summary>
    private static TaskCompletionSource HoldReplies(WatchHarness h)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Http.Intercept("/msg/send", async (_, token, next) =>
        {
            var response = await next();
            await release.Task.WaitAsync(token);
            return response;
        });
        return release;
    }

    /// <summary>Advances a minute in half-minute steps, with a danmaku each step so the room connection does not idle out;
    /// with <paramref name="silent"/>, also checks that no reply goes out meanwhile.</summary>
    private static async Task AdvanceAMinuteAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates,
        Channel<Dictionary<string, string>>? silent = null)
    {
        for (var i = 0; i < 2; i++)
        {
            h.Time.Advance(TimeSpan.FromSeconds(30));
            await DanmakuAsync(h, updates, "保持连接");
            if (silent is not null) await AssertNoSendAsync(silent);
        }
    }

    private static string Gift(DateTimeOffset sentAt, long uid = 10001, long price = 1500, long spend = 5000) =>
        BlindGiftFixture.With(data =>
        {
            data["uid"] = uid;
            data["price"] = price;
            data["total_coin"] = spend;
            data["timestamp"] = sentAt.ToUnixTimeSeconds();
            data["tid"] = Guid.NewGuid().ToString();
        });

    private static Task WaitForLogAsync(WatchHarness h, LogLevel level, string text) =>
        Eventually.TrueAsync(() => h.Logger.Entries.Any(e => e.Level == level && e.Message.Contains(text)));
}
