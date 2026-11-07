using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class WelcomeAnimationTests
{
    [Fact]
    public async Task Gift_rounds_use_settings_at_completion_and_same_viewer_can_queue_twice()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h);
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        var gift = BlindGiftFixture.With(data => { data["blind_gift"] = null; data["total_coin"] = 10000; });
        await h.PushAndSyncAsync(updates, gift);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["gift_animation.enabled"] = "true", ["gift_animation.tier_30_spend"] = "50" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        var first = await page.NextAsync();
        await h.PushAndSyncAsync(updates, gift);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        await page.AssertNoPlayAsync();
        await page.ReportAsync(first);
        var second = await page.NextAsync();
        Assert.Equal("general-30", second.Content);
        Assert.NotEqual(first.Id, second.Id);
        await page.ReportAsync(second);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["gift_animation.enabled"] = "false" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        await h.PushAndSyncAsync(updates, gift);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        await page.AssertNoPlayAsync();
    }

    [Theory]
    [InlineData(30000, true)]
    [InlineData(30001, false)]
    public async Task Gift_queue_timeout_starts_when_round_ends(int milliseconds, bool plays)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["gift_animation.enabled"] = "true" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        using var page = await Page.ConnectAsync(h);
        await using var observed = new Observations(h);
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        var first = await page.NextAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.With(data => { data["blind_gift"] = null; data["total_coin"] = 10000; }));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        h.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        await page.ReportAsync(first);
        if (plays) Assert.Equal("general-30", (await page.NextAsync()).Content);
        else
        {
            await observed.WaitAsync(update => update is SourcePlayback { Source: "送礼动画", Reason: "排队超过 30 秒" });
            await page.AssertNoPlayAsync();
        }
    }

    [Fact]
    public async Task Gift_animation_test_play_ignores_switch_and_can_stop()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h);
        using var page = await Page.ConnectAsync(h);
        await using var observed = new Observations(h);
        await h.Client.WebService.TestPlayGiftAsync(EntryTier.All[0], "试播昵称");
        var play = await page.NextAsync();
        await observed.WaitAsync(update => update is SourcePlayback { Source: "送礼动画", State: PlaybackState.Playing });
        await h.Client.WebService.StopTestPlayAsync(play.Id);
        Assert.Equal(play.Id, await page.NextStopAsync());
    }

    [Theory]
    [InlineData(9999, null)]
    [InlineData(10000, "general-30")]
    [InlineData(50000, "general-40")]
    [InlineData(200001, "general-50")]
    [InlineData(1000000, "general-60")]
    public Task Gift_animation_uses_exact_spend_and_ignores_entry_list(long coins, string? expected) =>
        AssertGiftAnimationAsync(BlindGiftFixture.With(data => { data["blind_gift"] = null; data["total_coin"] = coins; }), expected);

    // 198000 金瓜子 is 1980 电池: past the default 500 of tier 40, short of the 2000 of tier 50.
    [Fact]
    public Task A_guard_purchase_plays_the_gift_animation_for_what_it_cost() =>
        AssertGiftAnimationAsync(UserToastFixture.Message(), "general-40");

    private static async Task AssertGiftAnimationAsync(string gift, string? expected)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> {
            ["gift_animation.enabled"] = "true", ["entry_animation_viewers.10001"] = "0" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        await h.PushAndSyncAsync(updates, gift);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        if (expected is null) await page.AssertNoPlayAsync();
        else { var play = await page.NextAsync(); Assert.Equal(expected, play.Content); await page.ReportAsync(play); }
    }

    [Theory]
    [InlineData(29, null, null, 0)]
    [InlineData(39, null, "general-30", 0)]
    [InlineData(99, null, "general-60", 0)]
    [InlineData(60, "40", "general-40", 0)]
    [InlineData(60, "0", null, 0)]
    [InlineData(50, null, "general-50", 1)]
    [InlineData(50, null, "general-50", 2)]
    [InlineData(50, null, "general-50", 3)]
    public async Task Entry_animation_uses_list_before_fan_club_level(int level, string? choice, string? expected, int guard)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h);
        if (choice is not null) Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation_viewers.10001"] = choice });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryEffectFixture.Message("ENTRY_EFFECT", fanClubLevel: level, guard: guard));
        if (expected is null) await page.AssertNoPlayAsync();
        else { var play = await page.NextAsync(); Assert.Equal(expected, play.Content); await page.ReportAsync(play); }
    }

    [Fact]
    public async Task A_room_connection_without_the_stream_overlay_plays_no_entry_and_ends_no_gift_round()
    {
        // As the CLI watches (ADR 0003): even with the video page up and both animations on, nothing reaches it.
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await Start(h);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["gift_animation.enabled"] = "true", ["gift_animation.tier_30_spend"] = "50" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        await h.PushAndSyncAsync(updates, EntryEffectFixture.Message("ENTRY_EFFECT", fanClubLevel: 50),
            BlindGiftFixture.With(data => { data["blind_gift"] = null; data["total_coin"] = 10000; }));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await h.PushAndSyncAsync(updates);
        await page.AssertNoPlayAsync();
    }

    [Theory]
    [InlineData(30000, true)]
    [InlineData(30001, false)]
    public async Task Animation_uses_the_same_queue_timeout(int milliseconds, bool plays)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        using var page = await Page.ConnectAsync(h);
        await using var observed = new Observations(h);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        var first = await page.NextAsync();
        await h.Client.WebService.TestPlayEntryAsync(EntryTier.Parse(30)!.Value, "测试");
        h.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        await page.ReportAsync(first);
        if (plays) await page.ReportAsync(await page.NextAsync());
        else
        {
            await observed.WaitAsync(update => update is SourcePlayback { Source: "进场动画", Reason: "排队超过 30 秒" });
            await page.AssertNoPlayAsync();
        }
    }

    [Fact]
    public async Task Animation_and_video_share_queue_reports_stop_and_latest_volume()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        using var page = await Page.ConnectAsync(h);
        await h.Client.WebService.TestPlayEntryAsync(EntryTier.Parse(30)!.Value, "真实昵称");
        var first = await page.NextAsync();
        Assert.Equal("general-30", first.Content);
        Assert.Equal("真实昵称", first.Nickname);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.volume"] = "0" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        await page.ReportAsync(first, false, "模拟动画失败");
        var second = await page.NextAsync();
        Assert.Equal("10001", second.Content);
        Assert.Equal(0, second.Volume);
        await page.ReportAsync(second);
        await using var playingObserved = new Observations(h);
        await h.Client.WebService.TestPlayEntryAsync(EntryTier.Parse(40)!.Value, "测试");
        var third = await page.NextAsync();
        await playingObserved.WaitAsync(update => update is SourcePlayback { State: PlaybackState.Playing } playing && playing.Id == third.Id);
        await h.Client.WebService.StopTestPlayAsync(third.Id);
        Assert.Equal(third.Id, await page.NextStopAsync());
    }

    [Fact]
    public async Task Enabling_animation_during_connection_applies_to_the_next_entry()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.enabled"] = "false" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.enabled"] = "true" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        var playback = await page.NextAsync();
        Assert.Equal("10001", playback.Content);
        await page.ReportAsync(playback);
    }

    [Fact]
    public async Task Disabled_animation_can_still_be_test_played()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.enabled"] = "false" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        using var page = await Page.ConnectAsync(h);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        var playback = await page.NextAsync();
        Assert.Equal("10001", playback.Content);
        await page.ReportAsync(playback);
    }

    [Fact]
    public async Task Stopping_a_test_play_clears_every_video_page_advances_the_playback_queue_and_cannot_stop_real_entries()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001, 20002);
        await using var observed = new Observations(h);
        using var page = await Page.ConnectAsync(h);
        using var otherPage = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        var trial = await page.NextAsync();
        Assert.Equal(trial, await otherPage.NextAsync());
        await observed.WaitAsync(u => u is SourcePlayback { IsTest: true, State: PlaybackState.Playing });
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(uid: 20002));
        await h.Client.WebService.StopTestPlayAsync(trial.Id);
        Assert.Equal(trial.Id, await page.NextStopAsync());
        Assert.Equal(trial.Id, await otherPage.NextStopAsync());
        await observed.WaitAsync(u => u is SourcePlayback { IsTest: true, State: PlaybackState.Stopped });
        var real = await page.NextAsync();
        Assert.Equal(real, await otherPage.NextAsync());
        Assert.Equal("20002", real.Content);
        await h.Client.WebService.StopTestPlayAsync(trial.Id); // A late click must not stop the next item.
        await h.Client.WebService.StopTestPlayAsync(real.Id);
        await page.ReportAsync(trial); // Neither may a late report from the stopped test finish it.
        await page.ReportAsync(real);
        await observed.WaitAsync(u => u is SourcePlayback p && p.Id == real.Id && p.State == PlaybackState.Completed);
        Assert.Single(observed.Items.OfType<SourcePlayback>(), p => p.State == PlaybackState.Stopped);
        Assert.DoesNotContain(observed.Items, u => u is SourcePlayback p && p.Id == trial.Id && p.State == PlaybackState.Completed);
    }

    [Fact]
    public async Task Web_service_runs_with_welcome_animation_off_and_root_is_not_a_page()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        using var connected = await Page.ConnectAsync(h);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.enabled"] = "false" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        Assert.True(h.Client.WebService.Status.Running);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.WebService.Status.Origin) };
        using var root = await http.GetAsync("/", h.Stop.Token);
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
        Assert.Contains("<video", await http.GetStringAsync("/video", h.Stop.Token));
        Assert.Contains("/lottery/page.js", await http.GetStringAsync("/lottery", h.Stop.Token));
    }

    [Fact]
    public async Task Pages_count_separately_and_feature_changes_keep_existing_streams_connected()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.WebService.Status.Origin) };
        using var lottery = await http.GetAsync("/lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var lotteryReader = new StreamReader(await lottery.Content.ReadAsStreamAsync(h.Stop.Token));
        Assert.StartsWith("retry:", await lotteryReader.ReadLineAsync(h.Stop.Token));
        Assert.Equal("", await lotteryReader.ReadLineAsync(h.Stop.Token));
        Assert.Equal("event: state", await lotteryReader.ReadLineAsync(h.Stop.Token));
        Assert.StartsWith("data:", await lotteryReader.ReadLineAsync(h.Stop.Token));
        Assert.Equal(0, h.Client.WebService.Status.VideoConnections);
        Assert.Equal(1, h.Client.WebService.Status.LotteryConnections);
        using var video = await Page.ConnectAsync(h);
        await using var observed = new Observations(h);
        await observed.WaitAsync(update => update is WebServiceStatus { VideoConnections: 1, LotteryConnections: 1 });
        foreach (var enabled in new[] { false, true })
        {
            Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.enabled"] = Settings.BooleanText(enabled) });
            await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
            Assert.Equal(1, h.Client.WebService.Status.VideoConnections);
            Assert.Equal(1, h.Client.WebService.Status.LotteryConnections);
            await h.Client.WebService.TestPlayWelcomeAsync(10001);
            await video.ReportAsync(await video.NextAsync());
        }
        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(15));
        h.Time.Advance(TimeSpan.FromSeconds(15));
        string? line;
        do { line = await lotteryReader.ReadLineAsync(h.Stop.Token); } while (line == "");
        Assert.Equal(": heartbeat", line); // Lottery receives no video playback events.
        Assert.DoesNotContain(observed.Items, update => update is WebServiceStatus { Running: false });
    }

    [Fact]
    public async Task Finishing_the_lottery_stream_closes_only_its_connection_and_video_keeps_receiving_broadcasts()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        using var video = await Page.ConnectAsync(h);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.WebService.Status.Origin) };
        using var lottery = await http.GetAsync("/lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await lottery.Content.ReadAsStreamAsync(h.Stop.Token));
        string? line;
        do { line = await reader.ReadLineAsync(h.Stop.Token); } while (line is not null && !line.StartsWith("data: "));
        Assert.NotNull(line);
        Assert.Equal("", await reader.ReadLineAsync(h.Stop.Token));
        Assert.Equal(1, h.Client.WebService.Status.LotteryConnections);

        h.Client.Lottery.Dispose();
        Assert.Null(await reader.ReadLineAsync(h.Stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        await Eventually.TrueAsync(() => h.Client.WebService.Status.LotteryConnections == 0);
        Assert.Equal(1, h.Client.WebService.Status.VideoConnections);
        await h.Client.WebService.PlayVideoAsync("10001.mp4", "测试");
        var playing = await video.NextAsync();
        Assert.Equal("10001", playing.Content);
        await video.ReportAsync(playing);
    }

    [Fact]
    public async Task Event_streams_preserve_retry_heartbeat_and_page_event_bytes()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.WebService.Status.Origin) };
        using var lottery = await http.GetAsync("/lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        await using var lotteryStream = await lottery.Content.ReadAsStreamAsync(h.Stop.Token);
        await AssertFrameAsync(lotteryStream, "retry: 2000\n\n");
        await AssertFrameAsync(lotteryStream,
            "event: state\ndata: {\"roomId\":null,\"connected\":false,\"lottery\":null,\"canControl\":false,\"theme\":\"StarSummon\",\"pinnedResults\":false,\"isExamplePlaying\":false}\n\n");

        using var video = await http.GetAsync("/video/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        await using var videoStream = await video.Content.ReadAsStreamAsync(h.Stop.Token);
        await AssertFrameAsync(videoStream, "retry: 2000\n\n");
        await using var observed = new Observations(h);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        await observed.WaitAsync(update => update is SourcePlayback { State: PlaybackState.Playing });
        var id = Assert.Single(observed.Items.OfType<SourcePlayback>(), update => update.State == PlaybackState.Playing).Id;
        await AssertFrameAsync(videoStream, "data: {\"id\":\"" + id + "\",\"volume\":80}\n\n");
        await h.Client.WebService.StopTestPlayAsync(id);
        await AssertFrameAsync(videoStream, "event: stop\ndata: {\"id\":\"" + id + "\"}\n\n");

        await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(15));
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await AssertFrameAsync(lotteryStream, ": heartbeat\n\n");
        await AssertFrameAsync(videoStream, ": heartbeat\n\n");

        async Task AssertFrameAsync(Stream stream, string expected)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(expected);
            var actual = new byte[bytes.Length];
            await stream.ReadExactlyAsync(actual, h.Stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
            Assert.Equal(bytes, actual);
        }
    }

    [Fact]
    public async Task Changing_the_port_restarts_the_service_and_drops_existing_streams()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.WebService.Status.Origin) };
        using var page = await Page.ConnectAsync(h);
        var port = FreePort();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["web_service.port"] = port.ToString() });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        Assert.Equal(new WebServiceStatus(true, port, 0, 0, 0), h.Client.WebService.Status);
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("/video", h.Stop.Token));
        using var restarted = await Page.ConnectAsync(h);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        Assert.Equal("10001", (await restarted.NextAsync()).Content);
    }

    [Fact]
    public async Task Concurrent_port_changes_keep_one_listener_and_stop_releases_every_port()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = new PausedTimerProvider(() => { entered.SetResult(); release.Wait(); });
        var service = new WebServiceFacade(new Regions(time, h.Logger), h.DataDirectory, h.Client.Lottery, h.Client.GiftBoard);
        var ports = new HashSet<int>();
        while (ports.Count < 3) ports.Add(FreePort());
        var candidates = ports.ToArray();
        await service.StartAsync(candidates[0]);
        try
        {
            // Keep the old service finishing while both callers request a new port.
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token);
            var first = service.StartAsync(candidates[1]);
            var second = service.StartAsync(candidates[2]);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            release.Set();
            Exception?[] errors = [await Record.ExceptionAsync(() => first), await Record.ExceptionAsync(() => second)];
            Assert.Single(errors, error => error is null);
            Assert.IsType<InvalidOperationException>(Assert.Single(errors, error => error is not null));
            Assert.True(service.Status.Running);
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
            foreach (var port in candidates)
            {
                var address = $"http://localhost:{port}/video";
                if (port == service.Status.Port) Assert.Contains("<video", await http.GetStringAsync(address, h.Stop.Token));
                else await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(address, h.Stop.Token));
            }
            await service.StopAsync();
            foreach (var port in candidates)
                await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync($"http://localhost:{port}/video", h.Stop.Token));
        }
        finally
        {
            release.Set();
            await service.StopAsync();
        }
    }

    [Fact]
    public async Task Video_page_plays_an_unmapped_file_by_playback_id_and_retires_it_after_report()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h);
        File.WriteAllText(Path.Combine(h.DataDirectory, "videos", "shared.webm"), "shared bytes");
        using var page = await Page.ConnectAsync(h);
        await using var observed = new Observations(h);
        await h.Client.WebService.PlayVideoAsync("shared.webm", "抽奖");
        var playback = await page.NextAsync();
        Assert.Equal("shared bytes", playback.Content);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.WebService.Status.Origin) };
        using var file = await http.GetAsync("/video/files/" + playback.Id, h.Stop.Token);
        Assert.Equal("video/webm", file.Content.Headers.ContentType?.MediaType);
        await page.ReportAsync(playback);
        await observed.WaitAsync(update => update is VideoPlayback { State: PlaybackState.Completed, Source: "抽奖" });
        using var retired = await http.GetAsync("/video/files/" + playback.Id, h.Stop.Token);
        Assert.Equal(HttpStatusCode.NotFound, retired.StatusCode);
        Assert.Empty(observed.Items.OfType<SourcePlayback>());
        await Assert.ThrowsAsync<ArgumentException>(() => h.Client.WebService.PlayVideoAsync("../aether.yml", "抽奖"));
    }

    [Fact]
    public async Task Animation_only_entries_push_mapped_viewers_including_the_bot_account()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001, 9876543210);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.fan_club_level"] = "0" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(uid: 404), EntryV2Fixture.Message());
        var first = await page.NextAsync();
        Assert.Equal("10001", first.Content);
        await page.ReportAsync(first);
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(uid: 9876543210));
        Assert.Equal("9876543210", (await page.NextAsync()).Content);
        Assert.DoesNotContain(h.Http.Requests, request => request.Uri.AbsolutePath.EndsWith("getInfoByUser"));
        Assert.DoesNotContain(h.Http.Requests, request => request.Uri.AbsolutePath.EndsWith("/send"));
    }

    [Fact]
    public async Task Saving_the_switch_off_mid_connection_hands_over_nothing_and_logs_no_skips()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001);
        await using var updates = await h.WatchConnectedAsync();
        using var connected = await Page.ConnectAsync(h);
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.enabled"] = "false" });
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        // The room connection still runs on the settings it started with, where the switch was on.
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        Assert.DoesNotContain(h.Logger.Entries, entry => entry.Message.Contains("[欢迎动画"));
    }

    [Fact]
    public async Task Reentry_while_queued_or_playing_is_skipped_but_can_play_immediately_after_completion()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001, 20002);
        using var page = await Page.ConnectAsync(h);
        await using var observed = new Observations(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(), EntryV2Fixture.Message(uid: 20002));
        var first = await page.NextAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(), EntryV2Fixture.Message(uid: 20002));
        await Eventually.TrueAsync(() => observed.Items.OfType<SourcePlayback>()
            .Count(item => item.State == PlaybackState.Skipped && item.Reason == "上一个还没播完") == 2);
        Assert.Equal(2, observed.Items.OfType<SourcePlayback>().Count(item => item.State == PlaybackState.Queued));
        await page.ReportAsync(first);
        var second = await page.NextAsync();
        Assert.Equal("20002", second.Content);
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        await page.ReportAsync(second);
        Assert.Equal("10001", (await page.NextAsync()).Content);
    }

    [Fact]
    public async Task No_page_does_not_block_later_entries_or_test_plays()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001);
        await using var observed = new Observations(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        await h.WaitForLogAsync("没有推流软件连着");
        using var page = await Page.ConnectAsync(h);
        await page.AssertNoPlayAsync(); // The entry without a page is never replayed.
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        await page.ReportAsync(await page.NextAsync());
        await observed.WaitAsync(u => u is SourcePlayback { IsTest: true, State: PlaybackState.Completed });
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        await page.ReportAsync(await page.NextAsync());
        await observed.WaitAsync(u => u is SourcePlayback { IsTest: false, State: PlaybackState.Completed });
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        await page.ReportAsync(await page.NextAsync());
        await Eventually.TrueAsync(() => observed.Items.OfType<SourcePlayback>().Count(u => u.IsTest && u.State == PlaybackState.Completed) == 2);
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        Assert.Equal("10001", (await page.NextAsync()).Content);
    }

    [Fact]
    public async Task Test_plays_neither_check_nor_hold_the_viewers_pending_entry()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001);
        using var page = await Page.ConnectAsync(h);
        await using var observed = new Observations(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        var trial = await page.NextAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        await page.ReportAsync(trial);
        var real = await page.NextAsync();
        await observed.WaitAsync(u => u is SourcePlayback p && p.Id == real.Id && !p.IsTest && p.State == PlaybackState.Playing);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        await page.ReportAsync(real);
        await page.ReportAsync(await page.NextAsync());
        await page.ReportAsync(await page.NextAsync());
        await Eventually.TrueAsync(() => observed.Items.OfType<SourcePlayback>().Count(p => p.State == PlaybackState.Completed) == 4);
        Assert.DoesNotContain(observed.Items, u => u is SourcePlayback { State: PlaybackState.Skipped });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_unreported_entry_releases_the_viewer_immediately(bool timeout)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001);
        using var page = await Page.ConnectAsync(h);
        await using var observed = new Observations(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        var first = await page.NextAsync();
        if (timeout)
        {
            await h.Time.WaitForTimerAsync(TimeSpan.FromMinutes(2));
            await AdvanceAsync(h, updates, TimeSpan.FromMinutes(2));
        }
        else await page.ReportAsync(first, false, "格式不支持");
        await observed.WaitAsync(u => u is SourcePlayback p && p.Id == first.Id
            && p.State == (timeout ? PlaybackState.NoReport : PlaybackState.Failed));
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        Assert.Equal("10001", (await page.NextAsync()).Content);
    }

    [Theory]
    [InlineData(30000, true)]
    [InlineData(30001, false)]
    public async Task Queue_waits_for_report_and_skips_only_after_thirty_seconds(int milliseconds, bool plays)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001, 20002);
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(), EntryV2Fixture.Message(uid: 20002));
        var first = await page.NextAsync();
        await page.AssertNoPlayAsync();
        await AdvanceAsync(h, updates, TimeSpan.FromMilliseconds(milliseconds));
        await page.ReportAsync(first);
        if (plays) Assert.Equal("20002", (await page.NextAsync()).Content);
        else
        {
            await h.WaitForLogAsync("排队超过 30 秒");
            await page.AssertNoPlayAsync();
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(uid: 20002));
            Assert.Equal("20002", (await page.NextAsync()).Content);
        }
    }

    [Fact]
    public async Task Any_page_can_report_failure_and_late_reports_cannot_finish_the_next_playback()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001, 20002);
        await using var observed = new Observations(h);
        using var firstPage = await Page.ConnectAsync(h);
        using var secondPage = await Page.ConnectAsync(h);
        await observed.WaitAsync(u => u is WebServiceStatus { VideoConnections: 2 });
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        await h.Client.WebService.TestPlayWelcomeAsync(20002);
        var first = await firstPage.NextAsync();
        Assert.Equal(first, await secondPage.NextAsync());
        await secondPage.ReportAsync(first, false, "格式不支持");
        var next = await firstPage.NextAsync();
        Assert.Equal(next, await secondPage.NextAsync());
        Assert.Equal("20002", next.Content);
        await secondPage.ReportAsync(first);
        await observed.WaitAsync(u => u is SourcePlayback { State: PlaybackState.Failed, Reason: "格式不支持", IsTest: true });
        Assert.Contains(h.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("格式不支持"));
        Assert.DoesNotContain(observed.Items, u => u is SourcePlayback p && p.Id == next.Id && p.State == PlaybackState.Completed);
        await firstPage.ReportAsync(next);
        await observed.WaitAsync(u => u is SourcePlayback p && p.Id == next.Id && p.State == PlaybackState.Completed);
    }

    [Fact]
    public async Task Heartbeats_drop_disconnected_pages_and_release_the_current_playback()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001, 20002);
        await using var observed = new Observations(h);
        var page = await Page.ConnectAsync(h);
        await observed.WaitAsync(u => u is WebServiceStatus { VideoConnections: 1 });
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        await page.NextAsync();
        await h.Client.WebService.TestPlayWelcomeAsync(20002);
        page.Dispose();
        // How many heartbeats TCP needs to notice the close varies by OS (Windows can take more), and a write that hangs
        // only ends on its 5-second timeout, so keep advancing; seven 15-second steps stay under the 2-minute report timeout.
        for (var i = 0; i < 7 && observed.Items.OfType<WebServiceStatus>().Last().VideoConnections != 0; i++)
        {
            h.Time.Advance(TimeSpan.FromSeconds(15));
            await Task.Delay(200, h.Stop.Token);
        }
        // Already pushed, so it reads as unreported, not as a skip.
        await observed.WaitAsync(u => u is SourcePlayback { State: PlaybackState.NoReport, Reason: "推流软件已全部断开" });
        await observed.WaitAsync(u => u is WebServiceStatus { Running: true, VideoConnections: 0 });
        using var replacement = await Page.ConnectAsync(h);
        await h.Client.WebService.TestPlayWelcomeAsync(20002);
        Assert.Equal("20002", (await replacement.NextAsync()).Content);
    }

    [Fact]
    public async Task Unreported_playback_times_out_after_two_minutes_and_page_can_play_again()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        await using var observed = new Observations(h);
        using var page = await Page.ConnectAsync(h);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        await page.NextAsync();
        await h.Time.WaitForTimerAsync(TimeSpan.FromMinutes(2));
        h.Time.Advance(TimeSpan.FromMinutes(2));
        await observed.WaitAsync(u => u is SourcePlayback { State: PlaybackState.NoReport, Reason: "等了 2 分钟" });
        Assert.Contains(h.Logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Message.Contains("没收到播放回报"));
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        Assert.Equal("10001", (await page.NextAsync()).Content);
    }

    [Fact]
    public async Task A_new_room_connection_discards_old_pending_entries()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001, 20002);
        using var page = await Page.ConnectAsync(h);
        await using var observed = new Observations(h);
        Play playing;
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(), EntryV2Fixture.Message(uid: 20002));
            playing = await page.NextAsync();
        }
        await h.WaitForLogAsync("直播间连接结束");
        // The page outlives the room connection, so what it is already playing still finishes.
        await page.ReportAsync(playing);
        await observed.WaitAsync(u => u is SourcePlayback { Uid: 10001, State: PlaybackState.Completed });
        await using var nextRoom = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(nextRoom, EntryV2Fixture.Message());
        Assert.Equal("10001", (await page.NextAsync()).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Port_in_use_reports_warning_but_the_room_still_connects(bool rawSocket)
    {
        await using var first = new WatchHarness(enableStreamOverlay: true);
        await Start(first, 10001);
        using var tcp = new TcpListener(Dns.GetHostAddresses("localhost")[0], 0) { ExclusiveAddressUse = true };
        tcp.Start();
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        var port = rawSocket ? ((IPEndPoint)tcp.LocalEndpoint).Port : new Uri(first.Client.WebService.Status.Origin).Port;
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["entry_animation.enabled"] = "true", ["web_service.port"] = port.ToString()
        });
        await using var observed = new Observations(h);
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        // Each platform reports a taken port with its own error code; an unrecognised one would fall back to the raw error.
        await observed.WaitAsync(u => u is WebServiceStatus { Running: false } status && status.Error == $"端口 {port} 被别的程序占用");
        await using var updates = await h.WatchConnectedAsync();
        Assert.Contains(h.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains($"端口 {port} 被别的程序占用"));
    }

    [Fact]
    public async Task Missing_video_is_skipped_without_counting_and_page_can_restart_on_the_same_port()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await Start(h, 10001);
        await using var observed = new Observations(h);
        using (var page = await Page.ConnectAsync(h))
        await using (var updates = await h.WatchConnectedAsync())
        {
            var path = Path.Combine(h.DataDirectory, "videos", "10001.mp4");
            File.Delete(path);
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            await observed.WaitAsync(u => u is SourcePlayback { State: PlaybackState.Skipped, Reason: "视频文件不存在" });
            File.WriteAllText(path, "10001");
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            await page.ReportAsync(await page.NextAsync());
        }
        var address = h.Client.WebService.Status.Origin;
        await h.Client.WebService.StopAsync();
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        Assert.Equal(address, h.Client.WebService.Status.Origin);
        using var restarted = await Page.ConnectAsync(h);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        Assert.Equal("10001", (await restarted.NextAsync()).Content);
    }

    [Fact]
    public async Task Reset_clients_do_not_prevent_the_page_from_restarting_and_playing()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        var address = new Uri(h.Client.WebService.Status.Origin);
        // A bodyless 404 writes its headers in Close(), after the client may have reset the connection.
        var request = System.Text.Encoding.ASCII.GetBytes($"GET /missing HTTP/1.1\r\nHost: {address.Authority}\r\nConnection: close\r\n\r\n");
        // 20 resets crashed .NET 10.0.12 on macOS (dotnet/runtime#121848) only 4 runs in 5; 100 crashed it every run.
        for (var i = 0; i < 100; i++)
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { LingerState = new LingerOption(true, 0) };
            await socket.ConnectAsync(address.Host, address.Port, h.Stop.Token);
            using var stream = new NetworkStream(socket);
            await stream.WriteAsync(request, h.Stop.Token);
        }

        await h.Client.WebService.StopAsync();
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
        using var page = await Page.ConnectAsync(h);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        Assert.Equal("10001", (await page.NextAsync()).Content);
    }

    [Fact]
    public async Task Http_serves_only_mapped_videos_rejects_other_hosts_and_validates_reports()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Start(h, 10001);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.WebService.Status.Origin) };
        var html = await http.GetStringAsync("/video", h.Stop.Token);
        Assert.Contains("object-fit: contain", html);
        Assert.Contains("response.blob()", html);
        Assert.Contains("transparent", html);
        using var page = await Page.ConnectAsync(h);
        await h.Client.WebService.TestPlayWelcomeAsync(10001);
        var playing = await page.NextAsync();
        using var video = await http.GetAsync("/video/files/" + playing.Id, h.Stop.Token);
        Assert.Equal("video/mp4", video.Content.Headers.ContentType?.MediaType);
        Assert.Equal("10001", await video.Content.ReadAsStringAsync(h.Stop.Token));
        foreach (var path in new[] { "/video/files/404", "/video/files/10001.mp4", "/video/files/%2e%2e%2faether.yml", "/video/files/%2e%2e%5caether.yml", "/video/files/../aether.yml" })
        {
            using var denied = await http.GetAsync(path, h.Stop.Token);
            Assert.False(denied.IsSuccessStatusCode);
        }
        using var badHost = new HttpRequestMessage(HttpMethod.Get, "");
        badHost.Headers.Host = "evil.example:" + http.BaseAddress.Port;
        // HttpListener can race cleanup of a rejected Host with the next request on that connection.
        badHost.Headers.ConnectionClose = true;
        using var wrongHost = await http.SendAsync(badHost, h.Stop.Token);
        Assert.False(wrongHost.IsSuccessStatusCode);
        using var badReport = await http.PostAsync("/video/report", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), h.Stop.Token);
        Assert.Equal(HttpStatusCode.BadRequest, badReport.StatusCode);
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/video/report") { Content = JsonContent.Create(new { id = "x", success = true }) };
        crossOrigin.Headers.Add("Origin", "https://evil.example");
        using var forbidden = await http.SendAsync(crossOrigin, h.Stop.Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static async Task AdvanceAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates, TimeSpan duration)
    {
        while (duration > TimeSpan.Zero)
        {
            var step = duration > TimeSpan.FromSeconds(20) ? TimeSpan.FromSeconds(20) : duration;
            h.Time.Advance(step);
            await h.PushAndSyncAsync(updates);
            duration -= step;
        }
    }

    private sealed class Observations : IAsyncDisposable
    {
        public System.Collections.Concurrent.ConcurrentQueue<WebServiceUpdate> Items { get; } = new();
        private readonly CancellationTokenSource stop;
        private readonly Task reading;
        public Observations(WatchHarness h)
        {
            stop = CancellationTokenSource.CreateLinkedTokenSource(h.Stop.Token);
            reading = ReadAsync(h.Client.WebService);
        }
        private async Task ReadAsync(WebServiceFacade page)
        {
            try { await foreach (var item in page.GetUpdatesAsync(stop.Token)) Items.Enqueue(item); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        public Task WaitAsync(Func<WebServiceUpdate, bool> match) => Eventually.TrueAsync(() => Items.Any(match));
        public async ValueTask DisposeAsync() { await stop.CancelAsync(); await reading; stop.Dispose(); }
    }

    internal static int FreePort()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var number = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        return number;
    }

    private sealed class PausedTimerProvider(Action pause) : TimeProvider
    {
        private int started;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (Interlocked.Exchange(ref started, 1) == 0) pause();
            return System.CreateTimer(callback, state, dueTime, period);
        }
    }

    private static async Task Start(WatchHarness h, params long[] uids)
    {
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["entry_animation.enabled"] = "true", ["web_service.port"] = FreePort().ToString()
        });
        Directory.CreateDirectory(Path.Combine(h.DataDirectory, "videos"));
        foreach (var uid in uids)
        {
            File.WriteAllText(Path.Combine(h.DataDirectory, "videos", $"{uid}.mp4"), uid.ToString());
            Settings.Save(h.DataDirectory, new Dictionary<string, string> { [$"entry_animation_viewers.{uid}"] = $"{uid}.mp4" });
        }
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));
    }

    private sealed record Play(string Id, string Content, int Volume = 80, string? Nickname = null);

    private sealed class Page(HttpClient http, HttpResponseMessage response, StreamReader reader) : IDisposable
    {
        private readonly System.Threading.Channels.Channel<Play> received = System.Threading.Channels.Channel.CreateUnbounded<Play>();
        private readonly System.Threading.Channels.Channel<string> stopped = System.Threading.Channels.Channel.CreateUnbounded<string>();
        private readonly CancellationTokenSource stop = new();
        private Task reading = Task.CompletedTask;

        public static async Task<Page> ConnectAsync(WatchHarness h)
        {
            var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.WebService.Status.Origin) };
            var response = await http.GetAsync("/video/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
            response.EnsureSuccessStatusCode();
            var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
            Assert.StartsWith("retry:", await reader.ReadLineAsync(h.Stop.Token));
            var page = new Page(http, response, reader);
            page.reading = page.ReadAsync();
            return page;
        }

        private async Task ReadAsync()
        {
            try
            {
                var stopEvent = false;
                while (await reader.ReadLineAsync(stop.Token) is { } line)
                    if (line == "event: stop") stopEvent = true;
                    else if (line.StartsWith("data: "))
                    {
                        using var payload = JsonDocument.Parse(line[6..]);
                        Assert.False(payload.RootElement.TryGetProperty("uid", out _));
                        var id = payload.RootElement.GetProperty("id").GetString()!;
                        if (stopEvent) stopped.Writer.TryWrite(id);
                        else
                        {
                            var root = payload.RootElement;
                            var animation = root.TryGetProperty("animation", out var identifier);
                            Assert.False(root.TryGetProperty("isTest", out _));
                            received.Writer.TryWrite(new(id, animation ? identifier.GetString()! : await http.GetStringAsync("/video/files/" + id, stop.Token),
                                root.GetProperty("volume").GetInt32(), animation ? root.GetProperty("nickname").GetString() : null));
                        }
                        stopEvent = false;
                    }
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { }
            finally { received.Writer.TryComplete(); }
        }

        public Task<Play> NextAsync() => received.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public Task<string> NextStopAsync() => stopped.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public async Task AssertNoPlayAsync()
        {
            try { Assert.False(await received.Reader.WaitToReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromMilliseconds(80))); }
            catch (TimeoutException) { }
        }

        public async Task ReportAsync(Play play, bool success = true, string? reason = null)
        {
            using var result = await http.PostAsJsonAsync("/video/report", new { id = play.Id, success, reason }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        }

        public void Dispose() { stop.Cancel(); response.Dispose(); http.Dispose(); reading.GetAwaiter().GetResult(); reader.Dispose(); stop.Dispose(); }
    }
}
