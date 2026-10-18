using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class EntryEffectTests
{
    [Theory]
    // First two orders cover all five local captures from 2026-10-09 through 2026-10-16 (#154).
    [InlineData("ENTRY_EFFECT_MUST_RECEIVE", "INTERACT_WORD_V2", "ENTRY_EFFECT")]
    [InlineData("ENTRY_EFFECT_MUST_RECEIVE", "ENTRY_EFFECT", "INTERACT_WORD_V2")]
    [InlineData("ENTRY_EFFECT", "INTERACT_WORD_V2", "ENTRY_EFFECT_MUST_RECEIVE")]
    [InlineData("ENTRY_EFFECT", "ENTRY_EFFECT_MUST_RECEIVE", "INTERACT_WORD_V2")]
    [InlineData("INTERACT_WORD_V2", "ENTRY_EFFECT", "ENTRY_EFFECT_MUST_RECEIVE")]
    [InlineData("INTERACT_WORD_V2", "ENTRY_EFFECT_MUST_RECEIVE", "ENTRY_EFFECT")]
    public async Task Same_entry_welcomes_once_using_the_first_notification(string first, string second, string third)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome_animation.enabled"] = "true",
            ["web_service.port"] = WelcomeAnimationTests.FreePort().ToString()
        });
        Directory.CreateDirectory(Settings.VideoDirectory(h.DataDirectory));
        File.WriteAllText(Path.Combine(Settings.VideoDirectory(h.DataDirectory), "entry.mp4"), "video");
        Settings.SaveWelcomeAnimationVideo(h.DataDirectory, 10001, "entry.mp4");
        await h.Client.ApplyWebSettingsAsync(Settings.Load(h.DataDirectory));
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var page = await http.GetAsync(h.Client.WebService.Status.VideoAddress + "/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        var progress = await h.PushAndCollectAsync(updates, Message(first, "先到"), Message(second, "后到"), Message(third, "最后"));
        Assert.Equal("欢迎舰长先到", Assert.Single(progress.OfType<SendQueueUpdate>(), item => item.State == SendQueueItemState.Queued).Text);
        Assert.Single(h.Logger.Entries, log => log.Message.Contains("[欢迎动画") && log.Message.Contains("排队中"));
        foreach (var command in new[] { second, third })
            Assert.Contains(h.Logger.Entries, log => log.Level == Microsoft.Extensions.Logging.LogLevel.Debug
                && log.Message.Contains("同一次进场") && log.Message.Contains(command));

        static string Message(string command, string name) => command == "INTERACT_WORD_V2"
            ? EntryV2Fixture.Message(nickname: name, triggerTime: 1700000000000)
            : EntryEffectFixture.Message(command, nickname: name, triggerTime: 1700000000000);
    }

    [Fact]
    public async Task Merge_uses_only_each_viewers_last_trigger_and_never_merges_zero()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.enabled"] = "true" });
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        var progress = await h.PushAndCollectAsync(updates,
            EntryEffectFixture.Message(triggerTime: 10),
            EntryV2Fixture.Message(uid: 20002, triggerTime: 10),
            EntryV2Fixture.Message(triggerTime: 11),
            EntryEffectFixture.Message(triggerTime: 11),
            EntryV2Fixture.Message(triggerTime: 0),
            EntryEffectFixture.Message(triggerTime: 0),
            EntryV2Fixture.Message(triggerTime: 11),
            EntryEffectFixture.Message(triggerTime: 10));
        Assert.Equal(7, progress.OfType<SendQueueUpdate>().Count(item => item.State == SendQueueItemState.Queued));
    }

    [Fact]
    public async Task Reconnect_keeps_last_trigger_but_a_new_room_connection_starts_fresh()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.enabled"] = "true" });
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryEffectFixture.Message(triggerTime: 10));
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(triggerTime: 10));
            Assert.Single(h.Logger.Entries, log => log.Message.Contains("进队：") && log.Message.Contains("命中大航海"));
        }
        await using var next = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(next, EntryV2Fixture.Message(triggerTime: 10));
        Assert.Equal(2, h.Logger.Entries.Count(log => log.Message.Contains("进队：") && log.Message.Contains("命中大航海")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"guard\":null,\"wealth\":null,\"medal\":null}")]
    public async Task Missing_identity_does_not_satisfy_level_conditions(string userInfo)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.guard"] = "舰长",
            ["welcome.fan_club_level"] = "1", ["welcome.wealth_level"] = "1"
        });
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        var progress = await h.PushAndCollectAsync(updates,
            "{\"cmd\":\"ENTRY_EFFECT\",\"data\":{\"uid\":10001,\"target_id\":90001,\"privilege_type\":3,\"uinfo\":" + userInfo + "}}");
        Assert.Empty(progress.OfType<SendQueueUpdate>());
        Assert.DoesNotContain(h.Logger.Entries, log => log.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Theory]
    [InlineData("ENTRY_EFFECT", "welcome.uids", "[10001]", 90001, true)]
    [InlineData("ENTRY_EFFECT_MUST_RECEIVE", "welcome.guard", "舰长", 90001, true)]
    [InlineData("ENTRY_EFFECT", "welcome.wealth_level", "45", 90001, true)]
    [InlineData("ENTRY_EFFECT_MUST_RECEIVE", "welcome.fan_club_level", "47", 90001, true)]
    [InlineData("ENTRY_EFFECT", "welcome.fan_club_level", "47", 90002, false)]
    public async Task Effect_alone_uses_identity_and_only_this_rooms_fan_club(
        string command, string condition, string value, long medalAnchor, bool welcomed)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var settings = new Dictionary<string, string>
        {
            ["welcome.enabled"] = "true", ["welcome.uids"] = "[]", ["welcome.guard"] = "关闭",
            ["welcome.wealth_level"] = "0", ["welcome.fan_club_level"] = "0"
        };
        settings[condition] = value;
        Settings.Save(h.DataDirectory, settings);
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        var progress = await h.PushAndCollectAsync(updates, EntryEffectFixture.Message(command, medalAnchor: medalAnchor));
        var queued = progress.OfType<SendQueueUpdate>().Where(item => item.State == SendQueueItemState.Queued).ToArray();
        if (welcomed) Assert.Equal("欢迎舰长测试观众", Assert.Single(queued).Text);
        else Assert.Empty(queued);
    }

    [Fact]
    public async Task Negative_fan_club_level_is_malformed_even_for_another_rooms_fan_club()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome.enabled"] = "true", ["welcome.guard"] = "舰长" });
        await using var updates = await h.WatchConnectedAsync(includeSendQueueUpdates: true);
        var progress = await h.PushAndCollectAsync(updates, EntryEffectFixture.Message(fanClubLevel: -1, medalAnchor: 90002));
        Assert.Empty(progress.OfType<SendQueueUpdate>());
        Assert.Contains(h.Logger.Entries, log => log.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && log.Message.Contains("跳过无法解析的直播间消息") && log.Message.Contains("ENTRY_EFFECT"));
    }
}
