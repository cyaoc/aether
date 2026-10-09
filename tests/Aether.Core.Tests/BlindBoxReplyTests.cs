using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

public sealed class BlindBoxReplyTests
{
    [Theory]
    [InlineData(6500, "投入50电池 赚15电池")]
    [InlineData(1500, "投入50电池 亏35电池")]
    [InlineData(5000, "投入50电池 不赚不亏")]
    [InlineData(-1, "今日没有盲盒记录")]
    [InlineData(6510, "投入50电池 赚15.1电池")]
    public async Task Keyword_replies_to_the_viewer_with_todays_tally(int price, string expected)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureReplies(h);
        await using var updates = await h.WatchConnectedAsync();
        if (price >= 0) await PushAsync(h, Gift(h.Time.GetUtcNow(), price: price));
        await DanmakuAsync(h, updates, " \t今日盲盒 \n");
        var form = await NextReplyAsync(sent);
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
        var sent = CaptureReplies(h);
        h.Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] = """{"code":0,"data":{"room_id":999}}""";
        await using (var otherRoom = await h.WatchConnectedAsync())
        {
            await PushAsync(h, Gift(midnight, price: 99900));
            await DanmakuAsync(h, otherRoom, "同步");
        }
        h.Http.Responses["https://api.live.bilibili.com/room/v1/Room/room_init"] = """{"code":0,"data":{"room_id":7734200}}""";
        await using var updates = await h.WatchConnectedAsync();
        await PushAsync(h, Gift(midnight.AddSeconds(-1), price: 99900));
        await PushAsync(h, Gift(midnight, price: 6000));
        await PushAsync(h, Gift(midnight.AddDays(1).AddSeconds(-1), price: 7000));
        await PushAsync(h, Gift(midnight.AddDays(1), price: 99900));
        await PushAsync(h, Gift(midnight, uid: 20002, price: 99900));
        await DanmakuAsync(h, updates);
        Assert.Equal("投入100电池 赚30电池", (await NextReplyAsync(sent))["msg"]);
    }

    [Theory]
    [InlineData("今日盲盒呢", 10001, true)]
    [InlineData("今日盲盒", 0, true)]
    [InlineData("今日盲盒", 10001, false)]
    public async Task Only_exact_keywords_with_known_viewers_and_enabled_statistics_trigger(string text, long uid, bool enabled)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = Settings.BooleanText(enabled) });
        var sent = CaptureReplies(h);
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
        var sent = CaptureReplies(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates, "今日盲盒");
        await DanmakuAsync(h, updates, " 查盲盒 ", 9876543210);
        Assert.Equal("9876543210", (await NextReplyAsync(sent))["reply_mid"]);
        Assert.False(sent.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Queue_waits_the_configured_interval_and_queries_at_send_time_but_keeps_the_trigger_day()
    {
        await using var h = new WatchHarness();
        var beforeMidnight = new DateTimeOffset(2024, 11, 27, 23, 59, 59, TimeSpan.FromHours(8));
        h.Time.SetUtcNow(beforeMidnight.AddSeconds(-2));
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "7" });
        var sent = CaptureReplies(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        Assert.Equal("今日没有盲盒记录", (await NextReplyAsync(sent))["msg"]);
        await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
        await DanmakuAsync(h, updates);
        await PushAsync(h, Gift(beforeMidnight, price: 5010, spend: 4010));
        await PushAsync(h, Gift(beforeMidnight.AddSeconds(1), price: 99900));
        await DanmakuAsync(h, updates, "同步");
        h.Time.Advance(TimeSpan.FromMilliseconds(6999));
        await AssertNoReplyAsync(sent);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("投入40.1电池 赚10电池", (await NextReplyAsync(sent))["msg"]);
    }

    [Theory]
    [InlineData("{\"code\":0,\"message\":\"f\"}")]
    [InlineData("{\"code\":0,\"message\":\"k\"}")]
    [InlineData("{\"code\":0,\"message\":\"msg repeat\"}")]
    [InlineData("{\"code\":10031,\"message\":\"太快\"}")]
    [InlineData("{\"code\":0,\"message\":null}")]
    [InlineData("{\"code\":0}")]
    [InlineData("network")]
    public async Task Failed_sends_warn_and_are_discarded_without_retry(string response)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureReplies(h, response);
        if (response == "network") h.Http.Intercept("/msg/send", (_, _, _) => throw new HttpRequestException("test network failure"));
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await WaitForLogAsync(h, LogLevel.Warning, "发送弹幕失败");
        if (response != "network") await NextReplyAsync(sent);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await AssertNoReplyAsync(sent);
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("发送弹幕成功"));
        Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败"));
        Assert.Single(h.Http.Requests, r => r.Uri.AbsolutePath == "/msg/send");
    }

    [Fact]
    public async Task Waiting_replies_survive_reconnect_and_are_counted_and_cleared_on_disconnect()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureReplies(h);
        await using (var updates = await h.WatchConnectedAsync())
        {
            await DanmakuAsync(h, updates);
            await NextReplyAsync(sent);
            await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
            await DanmakuAsync(h, updates, uid: 20002);
            await DanmakuAsync(h, updates, uid: 30003);
            await DanmakuAsync(h, updates, uid: 40004);
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            h.Time.Advance(TimeSpan.FromMilliseconds(999));
            await AssertNoReplyAsync(sent);
            h.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Connected>(updates.Current);
            h.Time.Advance(TimeSpan.FromMilliseconds(3999));
            await AssertNoReplyAsync(sent);
            h.Time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal("20002", (await NextReplyAsync(sent))["reply_mid"]);
            await WaitForLogAsync(h, LogLevel.Information, "（20002）");
        }
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("丢掉 2 条"));
        await using var next = await h.WatchConnectedAsync();
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await AssertNoReplyAsync(sent);
    }

    [Fact]
    public async Task Hung_send_does_not_block_receiving_or_heartbeats_and_cancels_on_disconnect()
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
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("发送弹幕失败"));
    }

    private static async Task AssertNoReplyAsync(Channel<Dictionary<string, string>> sent)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sent.Reader.ReadAsync(timeout.Token).AsTask());
    }

    [Fact]
    public async Task Largest_valid_interval_keeps_waiting_without_ending_the_room_connection()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["send.interval_seconds"] = "2147483647" });
        var sent = CaptureReplies(h);
        await using var updates = await h.WatchConnectedAsync();
        await DanmakuAsync(h, updates);
        await NextReplyAsync(sent);
        await WaitForLogAsync(h, LogLevel.Information, "发送弹幕成功");
        await DanmakuAsync(h, updates);
        await AssertNoReplyAsync(sent);
        await DanmakuAsync(h, updates, "长间隔期间仍能接收");
    }

    private static Channel<Dictionary<string, string>> CaptureReplies(WatchHarness h,
        string response = "{\"code\":0,\"message\":\"\"}")
    {
        var sent = Channel.CreateUnbounded<Dictionary<string, string>>();
        h.Http.Intercept("/msg/send", async (request, token, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.live.bilibili.com/msg/send", request.RequestUri!.ToString());
            Assert.Equal("https://live.bilibili.com/", request.Headers.Referrer!.ToString());
            var cookie = Assert.Single(request.Headers.GetValues("Cookie"));
            Assert.Contains("SESSDATA=saved-session", cookie);
            Assert.Contains("bili_jct=saved-csrf", cookie);
            var body = await request.Content!.ReadAsStringAsync(token);
            sent.Writer.TryWrite(body.Split('&').Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => Decode(pair[0]), pair => Decode(pair[1])));
            return FakeBilibiliHttp.Json(response);
        });
        return sent;
        static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
    }

    private static async Task<Dictionary<string, string>> NextReplyAsync(Channel<Dictionary<string, string>> sent) =>
        await sent.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    private static string Gift(DateTimeOffset sentAt, long uid = 10001, long price = 1500, long spend = 5000)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "blind-gift-v1.json")))!;
        var data = root["data"]!;
        data["uid"] = uid;
        data["price"] = price;
        data["total_coin"] = spend;
        data["timestamp"] = sentAt.ToUnixTimeSeconds();
        data["tid"] = Guid.NewGuid().ToString();
        return root.ToJsonString();
    }

    private static Task PushAsync(WatchHarness h, string message) =>
        h.Server.PushAsync(FakeDanmakuServer.Packet(5, Encoding.UTF8.GetBytes(message), 0));

    private static async Task DanmakuAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates,
        string text = "今日盲盒", long uid = 10001)
    {
        object?[] meta = new object?[16];
        meta[15] = new { extra = JsonSerializer.Serialize(new { id_str = "12345678901234567890" }) };
        await PushAsync(h, JsonSerializer.Serialize(new { cmd = "DANMU_MSG", info = new object[] { meta, text, new object[] { uid, "观众" } } }));
        Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(text, Assert.IsType<Danmaku>(updates.Current).Content);
    }

    private static async Task WaitForLogAsync(WatchHarness h, LogLevel level, string text)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!h.Logger.Entries.Any(e => e.Level == level && e.Message.Contains(text)))
            await Task.Delay(5, timeout.Token);
    }
}
