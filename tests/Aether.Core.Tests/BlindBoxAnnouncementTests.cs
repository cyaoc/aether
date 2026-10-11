using System.Text.Json;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class BlindBoxAnnouncementTests
{
    [Fact]
    public async Task Captured_boxes_1_3_seconds_apart_restart_the_quiet_window_and_ten_boxes_form_one_announcement()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        var messages = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gifts-v2-20261009.jsonl"));
        await h.PushAndSyncAsync(updates, messages[3]);
        h.Time.Advance(TimeSpan.FromMilliseconds(1300));
        await h.PushAndSyncAsync(updates, messages[4]);
        h.Time.Advance(TimeSpan.FromMilliseconds(2999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入300电池 亏50电池");
        await WaitForLogAsync(h, "发送弹幕成功：播报观众 测试观众（10001）");

        h.Time.Advance(TimeSpan.FromSeconds(5));
        await h.PushAndSyncAsync(updates, messages[2]);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入1500电池 亏460电池");
        await AssertNoSendAsync(sent);
    }

    [Fact]
    public async Task A_box_arriving_inside_the_quiet_window_stays_in_the_round_when_its_save_waits_for_a_database_writer()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromMilliseconds(2900));
        using var database = TestDatabase.Open(h.DataDirectory);
        using var writer = database.BeginTransaction();
        var gift = Gift(h);
        var receiving = h.PushAndSyncAsync(updates, gift);
        Task advancing = Task.CompletedTask;
        try
        {
            await WaitForLogAsync(h, gift); // The message arrived even though SQLite cannot record it yet.
            await AssertNoSendAsync(sent);
            advancing = Task.Run(() => h.Time.Advance(TimeSpan.FromMilliseconds(100)), h.Stop.Token);
            await AssertNoSendAsync(sent);
        }
        finally
        {
            writer.Rollback();
            await receiving;
            await advancing.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
        }
        h.Time.Advance(TimeSpan.FromMilliseconds(2899));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入100电池 亏70电池");
    }

    [Fact]
    public async Task A_captured_ten_box_message_is_not_split_when_saving_outlasts_its_quiet_window()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        var message = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gifts-v2-20261009.jsonl"))[2];
        using var database = TestDatabase.Open(h.DataDirectory);
        using var writer = database.BeginTransaction();
        var receiving = h.PushAndSyncAsync(updates, message);
        try
        {
            await WaitForLogAsync(h, message);
            h.Time.Advance(TimeSpan.FromSeconds(4));
            await AssertNoSendAsync(sent);
        }
        finally
        {
            writer.Rollback();
            await receiving;
        }
        AssertAnnouncement(await NextSendAsync(sent), "本次投入1500电池 亏460电池");
        await WaitForLogAsync(h, "发送弹幕成功：播报");
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await AssertNoSendAsync(sent);
    }

    [Theory]
    [InlineData(5000, 6000, "本次投入50电池 赚10电池")]
    [InlineData(5000, 5000, "本次投入50电池 不赚不亏")]
    [InlineData(5015, 4004, "本次投入50.2电池 亏10.1电池")]
    [InlineData(5015, 5014, "本次投入50.2电池 不赚不亏")]
    [InlineData(0, 0, "本次投入0电池 不赚不亏")]
    public async Task One_box_announces_actual_spend_with_query_rounding_including_the_accounts_own_gifts(
        long spend, long price, string expected)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h, uid: 9876543210, spend: spend, price: price));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), expected, 9876543210);
        await AssertNoSendAsync(sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Queries_and_announcements_merge_separately_and_send_in_enqueue_order_with_current_amounts(bool queryFirst)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        var release = HoldFirst(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates, uid: 20002);
        await NextSendAsync(sent); // Hold another viewer's reply to keep both kinds queued.
        if (queryFirst) await DanmakuAsync(h, updates);
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        if (!queryFirst) await DanmakuAsync(h, updates);
        await DanmakuAsync(h, updates);
        await h.PushAndSyncAsync(updates, Gift(h, nickname: "新昵称", blindId: 32650));
        release.SetResult();
        await WaitForLogAsync(h, "发送弹幕成功：回复观众 观众（20002）");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        h.Time.Advance(TimeSpan.FromSeconds(5));
        var first = await NextSendAsync(sent);
        Assert.Equal(queryFirst ? "投入100电池 亏70电池" : "本次投入100电池 亏70电池", first["msg"]);
        Assert.Equal(queryFirst, first.ContainsKey("replay_dmid"));
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5), count: 2);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        var second = await NextSendAsync(sent);
        Assert.Equal(queryFirst ? "本次投入100电池 亏70电池" : "投入100电池 亏70电池", second["msg"]);
        Assert.Equal(!queryFirst, second.ContainsKey("replay_dmid"));
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("播报观众 新昵称（10001）"));
        await AssertNoSendAsync(sent);
    }

    [Fact]
    public async Task Same_viewer_queries_and_announcements_share_only_the_send_interval()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "1" });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await WaitForLogAsync(h, "发送弹幕成功：回复");
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
        await WaitForLogAsync(h, "发送弹幕成功：播报");
        await DanmakuAsync(h, updates);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        h.Time.Advance(TimeSpan.FromMilliseconds(999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("投入50电池 亏35电池", (await NextSendAsync(sent))["msg"]);
    }

    [Theory]
    [InlineData(false, Accepted)]
    [InlineData(true, Accepted)]
    [InlineData(false, "{\"code\":1003212,\"message\":\"超长\"}")]
    [InlineData(true, "{\"code\":0,\"message\":\"f\"}")]
    public async Task Gifts_received_in_flight_start_a_new_round_after_success_or_final_failure(bool alreadyQuiet, string response)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "1" });
        var sent = CaptureSends(h, response, Accepted);
        var release = HoldFirst(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.PushAndSyncAsync(updates, Gift(h, spend: 7000, price: 9000));
        if (alreadyQuiet) h.Time.Advance(TimeSpan.FromSeconds(3));
        release.SetResult();
        await WaitForLogAsync(h, response == Accepted ? "发送弹幕成功：播报" : "发送弹幕失败，已丢弃：播报");
        if (response != Accepted)
            Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败，已丢弃：播报"));
        var remaining = alreadyQuiet ? 1000 : 3000;
        if (alreadyQuiet) await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        h.Time.Advance(TimeSpan.FromMilliseconds(remaining - 1));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入70电池 赚20电池");
        await AssertNoSendAsync(sent);
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
    public async Task Rate_limit_retry_includes_new_gifts_and_finishes_the_reported_round_even_when_retry_fails(
        int code, string message, bool retrySucceeds)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var limited = JsonSerializer.Serialize(new { code, message });
        var sent = CaptureSends(h, limited, retrySucceeds ? Accepted : limited, Accepted);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
        await WaitForLogAsync(h, "的播报");
        var warning = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("的播报"));
        Assert.Contains($"（{code}）：{message}", warning.Message);
        Assert.Contains("5 秒后重试", warning.Message);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        await h.PushAndSyncAsync(updates, Gift(h));
        await DanmakuAsync(h, updates, uid: 20002);
        h.Time.Advance(TimeSpan.FromMilliseconds(4999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入100电池 亏70电池");
        await WaitForLogAsync(h, retrySucceeds ? "发送弹幕成功：播报" : "发送弹幕失败，已丢弃：播报");
        await h.PushAndSyncAsync(updates, Gift(h));
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5), count: 2);
        h.Time.Advance(TimeSpan.FromMilliseconds(4999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("20002", (await NextSendAsync(sent))["reply_mid"]);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5), count: 3);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
        await WaitForLogAsync(h, "发送弹幕成功：播报");
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await AssertNoSendAsync(sent);
        Assert.Single(h.Logger.Entries, e => e.Message.Contains("秒后重试观众"));
        Assert.Equal(retrySucceeds ? 0 : 1,
            h.Logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败，已丢弃：播报")));
        Assert.Equal(4, h.Http.Requests.Count(r => r.Uri.AbsolutePath == "/msg/send"));
    }

    [Theory]
    [InlineData("{\"code\":0,\"message\":\"f\"}")]
    [InlineData("{\"code\":1003212,\"message\":\"超长\"}")]
    [InlineData("network")]
    public async Task Ordinary_announcement_failure_ends_the_round_without_retry_or_carrying_amounts_forward(string response)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h, response, Accepted);
        if (response == "network")
        {
            var first = true;
            h.Http.Intercept("/msg/send", async (_, _, next) =>
            {
                var result = await next();
                if (!first) return result;
                first = false;
                throw new HttpRequestException("test network failure");
            });
        }
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
        await WaitForLogAsync(h, "发送弹幕失败，已丢弃：播报");
        Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败，已丢弃：播报"));
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await AssertNoSendAsync(sent);
        await h.PushAndSyncAsync(updates, Gift(h, spend: 7000, price: 9000));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入70电池 赚20电池");
        await WaitForLogAsync(h, "发送弹幕成功：播报");
        Assert.Equal(2, h.Http.Requests.Count(r => r.Uri.AbsolutePath == "/msg/send"));
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("秒后重试观众"));
    }

    [Fact]
    public async Task Duplicate_tid_and_zero_uid_neither_add_amounts_nor_restart_the_quiet_window()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        var gift = Gift(h);
        var conflicting = System.Text.Json.Nodes.JsonNode.Parse(gift)!;
        conflicting["data"]!["total_coin"] = 9000;
        await h.PushAndSyncAsync(updates, gift);
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await h.PushAndSyncAsync(updates, gift, conflicting.ToJsonString(), Gift(h, uid: 0));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await AssertNoSendAsync(sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disabling_announcements_keeps_recording_and_queries_unless_blind_boxes_are_disabled(bool enabled)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["blind_box.announce"] = Settings.BooleanText(!enabled), ["blind_box.enabled"] = Settings.BooleanText(enabled),
        });
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await AssertNoSendAsync(sent);
        await DanmakuAsync(h, updates);
        if (enabled) Assert.Equal("投入50电池 亏35电池", (await NextSendAsync(sent))["msg"]);
        else
        {
            await AssertNoSendAsync(sent);
            Assert.DoesNotContain(h.Logger.Entries, e => e.Message.StartsWith("记录盲盒"));
        }
    }

    [Fact]
    public async Task Silent_round_survives_reconnect_without_restarting_its_deadline_and_settings_apply_at_the_next_room_connection()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, Gift(h));
            h.Time.Advance(TimeSpan.FromSeconds(1));
            Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.announce"] = "false" });
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            h.Time.Advance(TimeSpan.FromMilliseconds(999));
            await AssertNoSendAsync(sent);
            h.Time.Advance(TimeSpan.FromMilliseconds(1));
            AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
            await WaitForLogAsync(h, "发送弹幕成功：播报");
        }
        await using var next = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(next, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await AssertNoSendAsync(sent);
        await DanmakuAsync(h, next);
        Assert.Equal("投入100电池 亏70电池", (await NextSendAsync(sent))["msg"]);
    }

    [Fact]
    public async Task Queued_announcement_survives_reconnect_and_keeps_the_send_interval()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextSendAsync(sent);
        await WaitForLogAsync(h, "发送弹幕成功：回复");
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1), count: 2); // Reconnect backoff also takes 1 s.
        h.Time.Advance(TimeSpan.FromMilliseconds(999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
    }

    [Fact]
    public async Task Interrupted_announcement_is_resent_with_new_amounts_without_losing_its_round()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        HoldFirst(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        await h.PushAndSyncAsync(updates, Gift(h));
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(4));
        h.Time.Advance(TimeSpan.FromMilliseconds(3999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入100电池 亏70电池");
        await WaitForLogAsync(h, "发送弹幕成功：播报");
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("发送弹幕失败"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Interrupted_announcement_retry_waits_from_interruption_and_recalculates_without_spending_its_retry(bool retrySucceeds)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        const string limited = "{\"code\":10031,\"message\":\"太快\"}";
        var sent = CaptureSends(h, limited, Accepted, retrySucceeds ? Accepted : limited, Accepted);
        var attempts = 0;
        h.Http.Intercept("/msg/send", async (_, token, next) =>
        {
            var result = await next();
            if (++attempts == 2) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return result;
        });
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
        await WaitForLogAsync(h, "5 秒后重试");
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        h.Time.Advance(TimeSpan.FromSeconds(5));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await h.Server.DisconnectAsync();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Reconnecting>(updates.Current);
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        await h.PushAndSyncAsync(updates, Gift(h));
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(4));
        h.Time.Advance(TimeSpan.FromMilliseconds(3999));
        await AssertNoSendAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入100电池 亏70电池");
        await WaitForLogAsync(h, retrySucceeds ? "发送弹幕成功：播报" : "发送弹幕失败，已丢弃：播报");
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await AssertNoSendAsync(sent);
        await h.PushAndSyncAsync(updates, Gift(h, spend: 7000, price: 9000));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入70电池 赚20电池");
        Assert.Single(h.Logger.Entries, e => e.Message.Contains("秒后重试观众"));
        Assert.Equal(retrySucceeds ? 0 : 1,
            h.Logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败，已丢弃：播报")));
        Assert.Equal(4, h.Http.Requests.Count(r => r.Uri.AbsolutePath == "/msg/send"));
    }

    [Fact]
    public async Task Ending_the_room_connection_counts_and_clears_in_flight_queries_queued_announcements_and_silent_rounds()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        HoldFirst(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await DanmakuAsync(h, updates, uid: 30003);
            await NextSendAsync(sent);
            await h.PushAndSyncAsync(updates, Gift(h));
            h.Time.Advance(TimeSpan.FromSeconds(3));
            await h.PushAndSyncAsync(updates, Gift(h, uid: 20002));
        }
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("丢掉 3 条待发弹幕"));
        await using var next = await h.WatchConnectedAsync();
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await AssertNoSendAsync(sent);
        await h.PushAndSyncAsync(next, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        AssertAnnouncement(await NextSendAsync(sent), "本次投入50电池 亏35电池");
    }

    [Fact]
    public async Task Hung_announcement_does_not_block_receiving_or_heartbeats_and_idle_timeout_cancels_it()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        HoldFirst(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.Server.NextRequestAsync(); // Authentication.
        await h.PushAndSyncAsync(updates, Gift(h));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await NextSendAsync(sent);
        h.Server.ReplyToHeartbeats = false;
        h.Time.Advance(TimeSpan.FromSeconds(27));
        var heartbeat = await h.Server.NextRequestAsync();
        Assert.Equal(2, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(heartbeat.AsSpan(8)));
        await h.PushAndSyncAsync(updates); // Receiving is still independent of the hung send.
        h.Time.Advance(TimeSpan.FromSeconds(60));
        Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.IsType<Reconnecting>(updates.Current);
        Assert.Contains(h.Logger.Entries, e => e.Message.Contains("60 秒未收到任何数据"));
    }

    private static string Gift(WatchHarness h, long uid = 10001, long spend = 5000, long price = 1500,
        string nickname = "测试观众", long blindId = 32649) => BlindGiftFixture.With(data =>
    {
        data["uid"] = uid;
        data["uname"] = nickname;
        data["total_coin"] = spend;
        data["price"] = price;
        data["timestamp"] = h.Time.GetUtcNow().ToUnixTimeSeconds();
        data["tid"] = Guid.NewGuid().ToString();
        data["blind_gift"]!["original_gift_id"] = blindId;
    });

    private static TaskCompletionSource HoldFirst(WatchHarness h)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        h.Http.Intercept("/msg/send", async (_, token, next) =>
        {
            var response = await next();
            if (first)
            {
                first = false;
                await release.Task.WaitAsync(token);
            }
            return response;
        });
        return release;
    }

    private static void AssertAnnouncement(Dictionary<string, string> form, string message, long uid = 10001)
    {
        Assert.Equal(message, form["msg"]);
        Assert.Equal(uid.ToString(System.Globalization.CultureInfo.InvariantCulture), form["reply_mid"]);
        Assert.Equal("", form["reply_uname"]);
        Assert.Equal("0", form["reply_attr"]);
        Assert.DoesNotContain("replay_dmid", form);
    }

    private static Task WaitForLogAsync(WatchHarness h, string text) =>
        Eventually.TrueAsync(() => h.Logger.Entries.Any(e => e.Message.Contains(text)));
}
