using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.BlindGiftFixture;
using static Aether.Core.Tests.Support.SentDanmaku;

namespace Aether.Core.Tests;

public sealed class SendQueueUpdateTests
{
    private const string RateLimited = "{\"code\":10031,\"message\":\"太快\"}";
    private const string Query = """{"cmd":"DANMU_MSG","info":[[],"今日盲盒",[10001,"查询观众"]]}""";

    [Theory]
    [InlineData("welcome.uids", "[10001]", "UID", null)]
    [InlineData("welcome.fan_club_level", "21", "粉丝团等级", "47")]
    [InlineData("welcome.guard", "舰长", "大航海身份", "舰长")]
    [InlineData("welcome.wealth_level", "30", "荣耀等级", "45")]
    public async Task Welcome_reports_queued_sending_and_sent_with_the_same_id_and_local_time(
        string setting, string threshold, string condition, string? value)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test+08", TimeSpan.FromHours(8), "test", "test"));
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.guard"] = "关闭", [setting] = threshold
        });
        var sent = CaptureSends(h);
        var release = HoldSends(h);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        var queuedAt = h.Time.GetLocalNow();
        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message());

        var queued = await NextProgressAsync(h, updates);
        Assert.Equal(SendQueueItemState.Queued, queued.State);
        Assert.Equal(SendQueueItemKind.Welcome, queued.Kind);
        Assert.Equal(10001, queued.Uid);
        Assert.Equal("测试观众", queued.Nickname);
        Assert.Equal("欢迎舰长测试观众", queued.Text);
        Assert.Equal(condition, queued.WelcomeCondition);
        Assert.Equal(value, queued.WelcomeValue);
        Assert.Equal(queuedAt, queued.OccurredAt);
        Assert.Equal(TimeSpan.FromHours(8), queued.OccurredAt.Offset);

        var sending = await NextProgressAsync(h, updates);
        Assert.Equal(SendQueueItemState.Sending, sending.State);
        Assert.Equal(queued.Id, sending.Id);
        Assert.Equal((await NextSendAsync(sent))["msg"], sending.Text);
        Assert.Equal(queuedAt, sending.OccurredAt);

        h.Time.Advance(TimeSpan.FromSeconds(2));
        release.SetResult();
        var success = await NextProgressAsync(h, updates);
        Assert.Equal(SendQueueItemState.Sent, success.State);
        Assert.Equal(queued.Id, success.Id);
        Assert.Equal(sending.Text, success.Text);
        Assert.Equal(queuedAt.AddSeconds(2), success.OccurredAt);
        Assert.Equal(queuedAt.Offset, success.OccurredAt.Offset);
    }

    [Theory]
    [InlineData(true, 1, 5)]
    [InlineData(false, 1, 5)]
    [InlineData(true, 7, 7)]
    public async Task Rate_limit_reports_one_wait_then_a_second_attempt_and_its_result(bool succeeds, int interval, int seconds)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["send.interval_seconds"] = interval.ToString()
        });
        var sent = CaptureSends(h, RateLimited, succeeds ? Accepted : RateLimited);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message());
        var queued = await NextProgressAsync(h, updates);
        Assert.Equal(SendQueueItemState.Queued, queued.State);
        await StepAsync(h, updates, queued.Id, SendQueueItemState.Sending);
        await NextSendAsync(sent);
        var waiting = await StepAsync(h, updates, queued.Id, SendQueueItemState.WaitingToRetry);
        Assert.Equal("发送弹幕受频率限制（10031）：太快", waiting.Reason);
        Assert.Equal(seconds, waiting.RetrySeconds);
        Assert.Equal(h.Time.GetLocalNow(), waiting.OccurredAt);
        await h.WaitForLogAsync(LogLevel.Warning, $"{seconds} 秒后重试");
        Assert.Contains(h.Logger.Entries, log => log.Message.Contains(waiting.Reason!) && log.Message.Contains($"{seconds} 秒后重试"));
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(seconds));
        h.Time.Advance(TimeSpan.FromSeconds(seconds));
        var sending = await StepAsync(h, updates, queued.Id, SendQueueItemState.Sending);
        Assert.Equal((await NextSendAsync(sent))["msg"], sending.Text);
        var result = await StepAsync(h, updates, queued.Id, succeeds ? SendQueueItemState.Sent : SendQueueItemState.Discarded);
        Assert.Equal(h.Time.GetLocalNow(), result.OccurredAt);
        Assert.Equal(succeeds ? null : waiting.Reason, result.Reason);
        if (succeeds) Assert.Equal(sending.Text, result.Text);
        Assert.Equal(2, SendAttempts(h));
    }

    [Theory]
    [InlineData("{大航海}", 20, "welcome.uids", "[10001]", "UID", null, "欢迎语拼出来是空的", "")]
    [InlineData("{大航海}", 20, "welcome.fan_club_level", "21", "粉丝团等级", "47", "欢迎语拼出来是空的", "")]
    [InlineData("欢迎回家", 1, "welcome.wealth_level", "30", "荣耀等级", "45", "欢迎语超过弹幕字数上限 1", "欢迎回家")]
    public async Task Rejected_welcome_reports_only_not_queued_with_its_text_condition_and_reason(
        string text, int limit, string setting, string threshold, string condition, string? value, string reason, string expectedText)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.text"] = text, [setting] = threshold
        });
        h.Http.SetDanmakuLengthLimit(limit);
        var sent = CaptureSends(h);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message(guard: 0));
        var rejected = await NextProgressAsync(h, updates);
        Assert.Equal(SendQueueItemState.NotQueued, rejected.State);
        Assert.Equal(SendQueueItemKind.Welcome, rejected.Kind);
        Assert.Equal(10001, rejected.Uid);
        Assert.Equal("测试观众", rejected.Nickname);
        Assert.Equal(condition, rejected.WelcomeCondition);
        Assert.Equal(value, rejected.WelcomeValue);
        Assert.Equal(reason, rejected.Reason);
        Assert.Equal(expectedText, rejected.Text);
        Assert.Equal(h.Time.GetLocalNow(), rejected.OccurredAt);
        // The receive loop has handled the rejection; nothing was queued behind it.
        await h.PushAndSyncAsync(updates);
        await AssertNoSendAsync(sent);
        Assert.Single(h.Logger.Entries, log => log.Level == LogLevel.Warning && log.Message.Contains("未发送"));
    }

    [Fact]
    public async Task Welcome_waiting_thirty_seconds_reports_discarded_without_an_attempt()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.enabled"] = "true" });
        var sent = CaptureSends(h);
        var release = HoldSends(h, attempt: 1);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message());
        var first = await NextProgressAsync(h, updates);
        await StepAsync(h, updates, first.Id, SendQueueItemState.Sending);
        await NextSendAsync(sent);
        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message(uid: 20002, nickname: "排队观众"));
        var waiting = await NextProgressAsync(h, updates);
        Assert.Equal(SendQueueItemState.Queued, waiting.State);
        Assert.NotEqual(first.Id, waiting.Id);
        h.Time.Advance(TimeSpan.FromSeconds(29));
        release.SetResult();
        await StepAsync(h, updates, first.Id, SendQueueItemState.Sent);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        var discarded = await StepAsync(h, updates, waiting.Id, SendQueueItemState.Discarded);
        Assert.Equal("欢迎已过期，进场已满 30 秒", discarded.Reason);
        Assert.Equal(waiting.OccurredAt.AddSeconds(30), discarded.OccurredAt);
        Assert.Equal(1, SendAttempts(h));
        await AssertNoSendAsync(sent);
    }

    [Theory]
    [InlineData("{\"code\":1003212,\"message\":\"超长\"}", "1003212", "超长")]
    [InlineData(NetworkFailure, "test network failure", "test network failure")]
    public async Task Ordinary_failure_reports_discarded_without_waiting_to_retry(string response, string code, string reason)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h, response);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        await h.Server.PushRoomMessageAsync(Query);
        var queued = await NextProgressAsync(h, updates);
        await StepAsync(h, updates, queued.Id, SendQueueItemState.Sending);
        await NextSendAsync(sent);
        var discarded = await StepAsync(h, updates, queued.Id, SendQueueItemState.Discarded);
        Assert.Contains(code, discarded.Reason);
        Assert.Contains(reason, discarded.Reason);
        Assert.Equal(h.Time.GetLocalNow(), discarded.OccurredAt);
        Assert.Null(discarded.RetrySeconds);
        Assert.Empty(await SyncProgressAsync(h, updates));
        Assert.Equal(1, SendAttempts(h));
    }

    [Fact]
    public async Task Query_merges_while_queued_and_sending_and_reports_the_reply_calculated_before_sending()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["blind_box.announce"] = "false"
        });
        var sent = CaptureSends(h);
        var releaseWelcome = HoldSends(h, attempt: 1);
        var releaseQuery = HoldSends(h, attempt: 2);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message(uid: 20002));
        var welcome = await NextProgressAsync(h, updates);
        await StepAsync(h, updates, welcome.Id, SendQueueItemState.Sending);
        await NextSendAsync(sent);
        await h.Server.PushRoomMessageAsync(Query);
        Assert.Equal("今日盲盒", Assert.IsType<Danmaku>(await h.NextAsync(updates)).Content);
        var queued = Assert.IsType<SendQueueUpdate>(await h.NextAsync(updates));
        Assert.Equal(SendQueueItemState.Queued, queued.State);
        Assert.Equal(SendQueueItemKind.BlindBoxQuery, queued.Kind);
        Assert.Equal(10001, queued.Uid);
        Assert.Equal("查询观众", queued.Nickname);
        Assert.Null(queued.Text);
        Assert.NotEqual(welcome.Id, queued.Id);
        await h.Server.PushRoomMessageAsync(Query);
        await h.Server.PushRoomMessageAsync(Gift(h.Time.GetUtcNow()));
        Assert.Empty(await SyncProgressAsync(h, updates));
        releaseWelcome.SetResult();
        await StepAsync(h, updates, welcome.Id, SendQueueItemState.Sent);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        var sending = await StepAsync(h, updates, queued.Id, SendQueueItemState.Sending);
        Assert.Equal("投入50电池 亏35电池", sending.Text);
        Assert.Equal((await NextSendAsync(sent))["msg"], sending.Text);
        await h.Server.PushRoomMessageAsync(Query);
        Assert.Empty(await SyncProgressAsync(h, updates));
        releaseQuery.SetResult();
        var success = await StepAsync(h, updates, queued.Id, SendQueueItemState.Sent);
        Assert.Equal(sending.Text, success.Text);
        Assert.Equal(2, SendAttempts(h));
    }

    [Fact]
    public async Task Announcement_is_queued_only_after_three_quiet_seconds_and_inflight_boxes_get_a_new_id()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        var release = HoldSends(h, attempt: 1);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        await h.Server.PushRoomMessageAsync(Gift(h.Time.GetUtcNow(), nickname: "盲盒观众"));
        Assert.Empty(await SyncProgressAsync(h, updates));
        h.Time.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.Empty(await SyncProgressAsync(h, updates));
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        var queued = await NextProgressAsync(h, updates);
        Assert.Equal(SendQueueItemState.Queued, queued.State);
        Assert.Equal(SendQueueItemKind.BlindBoxAnnouncement, queued.Kind);
        Assert.Equal(10001, queued.Uid);
        Assert.Equal("盲盒观众", queued.Nickname);
        Assert.Equal(h.Time.GetLocalNow(), queued.OccurredAt);
        Assert.Null(queued.Text);
        var sending = await StepAsync(h, updates, queued.Id, SendQueueItemState.Sending);
        Assert.Equal("本次投入50电池 亏35电池", sending.Text);
        Assert.Equal((await NextSendAsync(sent))["msg"], sending.Text);

        await h.Server.PushRoomMessageAsync(Gift(h.Time.GetUtcNow(), price: 6000, nickname: "新昵称"));
        Assert.Empty(await SyncProgressAsync(h, updates));
        // Already quiet when the request ends: the next round is queued at once, but only after this one's outcome.
        h.Time.Advance(TimeSpan.FromSeconds(3));
        release.SetResult();
        Assert.Equal(sending.Text, (await StepAsync(h, updates, queued.Id, SendQueueItemState.Sent)).Text);
        var next = await NextProgressAsync(h, updates);
        Assert.Equal(SendQueueItemState.Queued, next.State);
        Assert.NotEqual(queued.Id, next.Id);
        Assert.Equal("新昵称", next.Nickname);
        Assert.Equal(h.Time.GetLocalNow(), next.OccurredAt);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        var nextSending = await StepAsync(h, updates, next.Id, SendQueueItemState.Sending);
        Assert.Equal("本次投入50电池 赚10电池", nextSending.Text);
        Assert.Equal((await NextSendAsync(sent))["msg"], nextSending.Text);
        Assert.Equal(nextSending.Text, (await StepAsync(h, updates, next.Id, SendQueueItemState.Sent)).Text);
        Assert.Empty(await SyncProgressAsync(h, updates));
    }

    [Theory]
    [InlineData(false, "投入50电池 亏35电池", "投入100电池 亏70电池")]
    [InlineData(true, "本次投入50电池 亏35电池", "本次投入100电池 亏70电池")]
    public async Task Retry_reports_the_current_amounts_under_the_original_id(bool announcement, string firstText, string retryText)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.announce"] = Settings.BooleanText(announcement) });
        var sent = CaptureSends(h, RateLimited, Accepted);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        await h.Server.PushRoomMessageAsync(Gift(h.Time.GetUtcNow()));
        Assert.Empty(await SyncProgressAsync(h, updates));
        if (announcement) h.Time.Advance(TimeSpan.FromSeconds(3));
        else await h.Server.PushRoomMessageAsync(Query);
        var queued = await NextProgressAsync(h, updates);
        var first = await StepAsync(h, updates, queued.Id, SendQueueItemState.Sending);
        Assert.Equal(firstText, first.Text);
        Assert.Equal((await NextSendAsync(sent))["msg"], first.Text);
        await StepAsync(h, updates, queued.Id, SendQueueItemState.WaitingToRetry);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        await h.Server.PushRoomMessageAsync(Gift(h.Time.GetUtcNow()));
        Assert.Empty(await SyncProgressAsync(h, updates));
        h.Time.Advance(TimeSpan.FromSeconds(5));
        var retry = await StepAsync(h, updates, queued.Id, SendQueueItemState.Sending);
        Assert.Equal(retryText, retry.Text);
        Assert.Equal((await NextSendAsync(sent))["msg"], retry.Text);
        Assert.Equal(retry.Text, (await StepAsync(h, updates, queued.Id, SendQueueItemState.Sent)).Text);
        Assert.Empty(await SyncProgressAsync(h, updates));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconnect_repeats_sending_with_the_same_id_without_reporting_discarded(bool announcement)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var sent = CaptureSends(h);
        HoldSends(h, attempt: 1);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        if (announcement)
        {
            await h.Server.PushRoomMessageAsync(Gift(h.Time.GetUtcNow()));
            Assert.Empty(await SyncProgressAsync(h, updates));
            h.Time.Advance(TimeSpan.FromSeconds(3));
        }
        else await h.Server.PushRoomMessageAsync(Query);
        var queued = await NextProgressAsync(h, updates);
        var sending = await StepAsync(h, updates, queued.Id, SendQueueItemState.Sending);
        await NextSendAsync(sent);
        Assert.Empty(await SyncProgressAsync(h, updates));
        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(4));
        h.Time.Advance(TimeSpan.FromSeconds(4));
        var retry = await StepAsync(h, updates, queued.Id, SendQueueItemState.Sending);
        Assert.Equal(sending.Text, retry.Text);
        Assert.Equal((await NextSendAsync(sent))["msg"], retry.Text);
        await StepAsync(h, updates, queued.Id, SendQueueItemState.Sent);
        await h.Server.PushRoomMessageAsync(Query);
        Assert.NotEqual(queued.Id, Assert.Single(await SyncProgressAsync(h, updates)).Id);
    }

    [Fact]
    public async Task Mixed_items_have_unique_ids()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.text"] = "{大航海}", ["welcome.uids"] = "[10001]"
        });
        var sent = CaptureSends(h);
        HoldSends(h, attempt: 1);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message(uid: 20002));
        await NextSendAsync(sent); // A paused update reader must not block sending.
        await h.Server.PushRoomMessageAsync(Query);
        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message(guard: 0));
        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message(uid: 20002));
        await h.Server.PushRoomMessageAsync(Gift(h.Time.GetUtcNow()));
        var progress = await SyncProgressAsync(h, updates);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        progress.AddRange(await SyncProgressAsync(h, updates));
        var items = progress.Where(update => update.State is SendQueueItemState.Queued or SendQueueItemState.NotQueued).ToArray();
        Assert.Equal(5, items.Length);
        Assert.Equal(5, items.Select(item => item.Id).Distinct().Count());
        Assert.All(items, item => Assert.True(item.Id > 0));
        Assert.Equal(2, items.Count(item => item.Kind == SendQueueItemKind.Welcome && item.State == SendQueueItemState.Queued));
        Assert.Single(items, item => item.State == SendQueueItemState.NotQueued);
        Assert.Single(items, item => item.Kind == SendQueueItemKind.BlindBoxQuery);
        Assert.Single(items, item => item.Kind == SendQueueItemKind.BlindBoxAnnouncement);
        Assert.DoesNotContain(progress, item => item.State is SendQueueItemState.Sent or SendQueueItemState.Discarded);
    }

    private static async Task<List<SendQueueUpdate>> SyncProgressAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates) =>
        [.. (await h.PushAndCollectAsync(updates)).OfType<SendQueueUpdate>()];

    private static async Task<SendQueueUpdate> StepAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates,
        long id, SendQueueItemState state)
    {
        var update = await NextProgressAsync(h, updates);
        Assert.Equal(id, update.Id);
        Assert.Equal(state, update.State);
        return update;
    }

    private static async Task<SendQueueUpdate> NextProgressAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates)
    {
        WatchUpdate update;
        do { update = await h.NextAsync(updates); } while (update is Danmaku);
        return Assert.IsType<SendQueueUpdate>(update);
    }
}
