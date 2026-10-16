using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class WelcomeAnimationTests
{
    [Fact]
    public async Task Animation_only_entries_push_mapped_viewers_including_the_bot_account()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Start(h, 10001, 9876543210);
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(uid: 404), EntryV2Fixture.Message());
        var first = await page.NextAsync();
        Assert.Equal("10001", first.Uid);
        await page.ReportAsync(first);
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(uid: 9876543210));
        Assert.Equal("9876543210", (await page.NextAsync()).Uid);
        Assert.DoesNotContain(h.Http.Requests, request => request.Uri.AbsolutePath.EndsWith("getInfoByUser"));
        Assert.DoesNotContain(h.Http.Requests, request => request.Uri.AbsolutePath.EndsWith("/send"));
    }

    [Fact]
    public async Task Saving_the_switch_off_mid_connection_hands_over_nothing_and_logs_no_skips()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Start(h, 10001);
        await using var updates = await h.WatchConnectedAsync();
        await h.Client.AnimationPage.StopAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome_animation.enabled"] = "false" });
        h.Client.AnimationPage.Start(Settings.Load(h.DataDirectory));
        // The room connection still runs on the settings it started with, where the switch was on.
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        Assert.DoesNotContain(h.Logger.Entries, entry => entry.Message.Contains("[欢迎动画"));
    }

    [Theory]
    [InlineData(599999, false)]
    [InlineData(600000, true)]
    public async Task Ten_minute_interval_starts_only_after_delivery(int elapsedMilliseconds, bool plays)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Start(h, 10001);
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        await page.ReportAsync(await page.NextAsync());
        await h.WaitForLogAsync("已播放");
        await AdvanceAsync(h, updates, TimeSpan.FromMilliseconds(elapsedMilliseconds));
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        if (plays) Assert.Equal("10001", (await page.NextAsync()).Uid);
        else
        {
            await h.WaitForLogAsync("10 分钟内播过");
            await page.AssertNoPlayAsync();
        }
    }

    [Fact]
    public async Task No_page_does_not_count_and_test_plays_neither_check_nor_update_the_interval()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Start(h, 10001);
        await using var observed = new Observations(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        await h.WaitForLogAsync("没有推流软件连着");
        using var page = await Page.ConnectAsync(h);
        await page.AssertNoPlayAsync(); // The entry without a page is never replayed.
        h.Client.AnimationPage.TestPlay(10001);
        await page.ReportAsync(await page.NextAsync());
        await observed.WaitAsync(u => u is WelcomeAnimationPlayback { IsTest: true, State: WelcomeAnimationState.Completed });
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        await page.ReportAsync(await page.NextAsync());
        await observed.WaitAsync(u => u is WelcomeAnimationPlayback { IsTest: false, State: WelcomeAnimationState.Completed });
        await AdvanceAsync(h, updates, TimeSpan.FromMinutes(5));
        h.Client.AnimationPage.TestPlay(10001);
        await page.ReportAsync(await page.NextAsync());
        await Eventually.TrueAsync(() => observed.Items.OfType<WelcomeAnimationPlayback>().Count(u => u.IsTest && u.State == WelcomeAnimationState.Completed) == 2);
        await AdvanceAsync(h, updates, TimeSpan.FromMinutes(5));
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
        Assert.Equal("10001", (await page.NextAsync()).Uid);
    }

    [Theory]
    [InlineData(30000, true)]
    [InlineData(30001, false)]
    public async Task Queue_waits_for_report_and_skips_only_after_thirty_seconds(int milliseconds, bool plays)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Start(h, 10001, 20002);
        using var page = await Page.ConnectAsync(h);
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, EntryV2Fixture.Message(), EntryV2Fixture.Message(uid: 20002));
        var first = await page.NextAsync();
        await page.AssertNoPlayAsync();
        await AdvanceAsync(h, updates, TimeSpan.FromMilliseconds(milliseconds));
        await page.ReportAsync(first);
        if (plays) Assert.Equal("20002", (await page.NextAsync()).Uid);
        else
        {
            await h.WaitForLogAsync("排队超过 30 秒");
            await page.AssertNoPlayAsync();
        }
    }

    [Fact]
    public async Task Any_page_can_report_failure_and_late_reports_cannot_finish_the_next_playback()
    {
        await using var h = new WatchHarness();
        Start(h, 10001, 20002);
        await using var observed = new Observations(h);
        using var firstPage = await Page.ConnectAsync(h);
        using var secondPage = await Page.ConnectAsync(h);
        await observed.WaitAsync(u => u is AnimationPageStatus { ConnectedPages: 2 });
        h.Client.AnimationPage.TestPlay(10001);
        h.Client.AnimationPage.TestPlay(20002);
        var first = await firstPage.NextAsync();
        Assert.Equal(first, await secondPage.NextAsync());
        await secondPage.ReportAsync(first, false, "格式不支持");
        var next = await firstPage.NextAsync();
        Assert.Equal("20002", next.Uid);
        await secondPage.ReportAsync(first);
        await observed.WaitAsync(u => u is WelcomeAnimationPlayback { State: WelcomeAnimationState.Failed, Reason: "格式不支持", IsTest: true });
        Assert.Contains(h.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("格式不支持"));
        Assert.DoesNotContain(observed.Items, u => u is WelcomeAnimationPlayback p && p.Id == next.Id && p.State == WelcomeAnimationState.Completed);
        await firstPage.ReportAsync(next);
        await observed.WaitAsync(u => u is WelcomeAnimationPlayback p && p.Id == next.Id && p.State == WelcomeAnimationState.Completed);
    }

    [Fact]
    public async Task Heartbeats_drop_disconnected_pages_and_release_the_current_playback()
    {
        await using var h = new WatchHarness();
        Start(h, 10001, 20002);
        await using var observed = new Observations(h);
        var page = await Page.ConnectAsync(h);
        await observed.WaitAsync(u => u is AnimationPageStatus { ConnectedPages: 1 });
        h.Client.AnimationPage.TestPlay(10001);
        await page.NextAsync();
        h.Client.AnimationPage.TestPlay(20002);
        page.Dispose();
        // TCP may discover a graceful close on its next write, or on the write after that.
        for (var i = 0; i < 3 && observed.Items.OfType<AnimationPageStatus>().Last().ConnectedPages != 0; i++)
        {
            await h.Time.WaitForTimerAsync(TimeSpan.FromSeconds(15), i + 1);
            h.Time.Advance(TimeSpan.FromSeconds(15));
            await Task.Delay(50, h.Stop.Token);
        }
        // Already pushed and counted in the interval, so it reads as unreported, not as a skip.
        await observed.WaitAsync(u => u is WelcomeAnimationPlayback { State: WelcomeAnimationState.NoReport, Reason: "推流软件已全部断开" });
        await observed.WaitAsync(u => u is AnimationPageStatus { Running: true, ConnectedPages: 0 });
        using var replacement = await Page.ConnectAsync(h);
        h.Client.AnimationPage.TestPlay(20002);
        Assert.Equal("20002", (await replacement.NextAsync()).Uid);
    }

    [Fact]
    public async Task Unreported_playback_times_out_after_two_minutes_and_page_can_play_again()
    {
        await using var h = new WatchHarness();
        Start(h, 10001);
        await using var observed = new Observations(h);
        using var page = await Page.ConnectAsync(h);
        h.Client.AnimationPage.TestPlay(10001);
        await page.NextAsync();
        await h.Time.WaitForTimerAsync(TimeSpan.FromMinutes(2));
        h.Time.Advance(TimeSpan.FromMinutes(2));
        await observed.WaitAsync(u => u is WelcomeAnimationPlayback { State: WelcomeAnimationState.NoReport, Reason: "等了 2 分钟" });
        Assert.Contains(h.Logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning && entry.Message.Contains("没收到播放回报"));
        h.Client.AnimationPage.TestPlay(10001);
        Assert.Equal("10001", (await page.NextAsync()).Uid);
    }

    [Fact]
    public async Task A_new_room_connection_gets_its_own_interval_and_discards_old_pending_entries()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Start(h, 10001, 20002);
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
        await observed.WaitAsync(u => u is WelcomeAnimationPlayback { Uid: 10001, State: WelcomeAnimationState.Completed });
        await using var nextRoom = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(nextRoom, EntryV2Fixture.Message());
        Assert.Equal("10001", (await page.NextAsync()).Uid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Port_in_use_reports_warning_but_the_room_still_connects(bool rawSocket)
    {
        await using var first = new WatchHarness();
        Start(first, 10001);
        using var tcp = new TcpListener(Dns.GetHostAddresses("localhost")[0], 0) { ExclusiveAddressUse = true };
        tcp.Start();
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var port = rawSocket ? ((IPEndPoint)tcp.LocalEndpoint).Port : new Uri(first.Client.AnimationPage.Address).Port;
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome_animation.enabled"] = "true", ["welcome_animation.port"] = port.ToString()
        });
        await using var observed = new Observations(h);
        h.Client.AnimationPage.Start(Settings.Load(h.DataDirectory));
        // Each platform reports a taken port with its own error code; an unrecognised one would fall back to the raw error.
        await observed.WaitAsync(u => u is AnimationPageStatus { Running: false } status && status.Error == $"端口 {port} 被别的程序占用");
        await using var updates = await h.WatchConnectedAsync();
        Assert.Contains(h.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains($"端口 {port} 被别的程序占用"));
    }

    [Fact]
    public async Task Missing_video_is_skipped_without_counting_and_page_can_restart_on_the_same_port()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Start(h, 10001);
        await using var observed = new Observations(h);
        using (var page = await Page.ConnectAsync(h))
        await using (var updates = await h.WatchConnectedAsync())
        {
            var path = Path.Combine(h.DataDirectory, "welcome-animations", "10001.mp4");
            File.Delete(path);
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            await observed.WaitAsync(u => u is WelcomeAnimationPlayback { State: WelcomeAnimationState.Skipped, Reason: "视频文件不存在" });
            File.WriteAllText(path, "restored");
            await h.PushAndSyncAsync(updates, EntryV2Fixture.Message());
            await page.ReportAsync(await page.NextAsync());
        }
        var address = h.Client.AnimationPage.Address;
        await h.Client.AnimationPage.StopAsync();
        h.Client.AnimationPage.Start(Settings.Load(h.DataDirectory));
        Assert.Equal(address, h.Client.AnimationPage.Address);
        using var restarted = await Page.ConnectAsync(h);
        h.Client.AnimationPage.TestPlay(10001);
        Assert.Equal("10001", (await restarted.NextAsync()).Uid);
    }

    [Fact]
    public async Task Http_serves_only_mapped_videos_rejects_other_hosts_and_validates_reports()
    {
        await using var h = new WatchHarness();
        Start(h, 10001);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.AnimationPage.Address) };
        var html = await http.GetStringAsync("", h.Stop.Token);
        Assert.Contains("object-fit: contain", html);
        Assert.Contains("response.blob()", html);
        Assert.Contains("transparent", html);
        using var video = await http.GetAsync("videos/10001", h.Stop.Token);
        Assert.Equal("video/mp4", video.Content.Headers.ContentType?.MediaType);
        Assert.Equal("video", await video.Content.ReadAsStringAsync(h.Stop.Token));
        foreach (var path in new[] { "videos/404", "videos/10001.mp4", "videos/%2e%2e%2faether.yml", "videos/%2e%2e%5caether.yml", "videos/../aether.yml" })
        {
            using var denied = await http.GetAsync(path, h.Stop.Token);
            Assert.False(denied.IsSuccessStatusCode);
        }
        using var badHost = new HttpRequestMessage(HttpMethod.Get, "");
        badHost.Headers.Host = "evil.example:" + http.BaseAddress.Port;
        using var wrongHost = await http.SendAsync(badHost, h.Stop.Token);
        Assert.False(wrongHost.IsSuccessStatusCode);
        using var badReport = await http.PostAsync("report", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), h.Stop.Token);
        Assert.Equal(HttpStatusCode.BadRequest, badReport.StatusCode);
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "report") { Content = JsonContent.Create(new { id = "x", success = true }) };
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
        public System.Collections.Concurrent.ConcurrentQueue<AnimationPageUpdate> Items { get; } = new();
        private readonly CancellationTokenSource stop;
        private readonly Task reading;
        public Observations(WatchHarness h)
        {
            stop = CancellationTokenSource.CreateLinkedTokenSource(h.Stop.Token);
            reading = ReadAsync(h.Client.AnimationPage);
        }
        private async Task ReadAsync(AnimationPage page)
        {
            try { await foreach (var item in page.GetUpdatesAsync(stop.Token)) Items.Enqueue(item); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        public Task WaitAsync(Func<AnimationPageUpdate, bool> match) => Eventually.TrueAsync(() => Items.Any(match));
        public async ValueTask DisposeAsync() { await stop.CancelAsync(); await reading; stop.Dispose(); }
    }

    private static void Start(WatchHarness h, params long[] uids)
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var number = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome_animation.enabled"] = "true", ["welcome_animation.port"] = number.ToString()
        });
        Directory.CreateDirectory(Path.Combine(h.DataDirectory, "welcome-animations"));
        foreach (var uid in uids)
        {
            File.WriteAllText(Path.Combine(h.DataDirectory, "welcome-animations", $"{uid}.mp4"), "video");
            Settings.SaveWelcomeAnimationVideo(h.DataDirectory, uid, $"{uid}.mp4");
        }
        h.Client.AnimationPage.Start(Settings.Load(h.DataDirectory));
    }

    private sealed record Play(string Id, string Uid);

    private sealed class Page(HttpClient http, HttpResponseMessage response, StreamReader reader) : IDisposable
    {
        private readonly System.Threading.Channels.Channel<Play> received = System.Threading.Channels.Channel.CreateUnbounded<Play>();
        private readonly CancellationTokenSource stop = new();
        private Task reading = Task.CompletedTask;

        public static async Task<Page> ConnectAsync(WatchHarness h)
        {
            var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(h.Client.AnimationPage.Address) };
            var response = await http.GetAsync("events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
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
                while (await reader.ReadLineAsync(stop.Token) is { } line)
                    if (line.StartsWith("data: "))
                        received.Writer.TryWrite(JsonSerializer.Deserialize<Play>(line[6..], new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { }
            finally { received.Writer.TryComplete(); }
        }

        public Task<Play> NextAsync() => received.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public async Task AssertNoPlayAsync()
        {
            try { Assert.False(await received.Reader.WaitToReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromMilliseconds(80))); }
            catch (TimeoutException) { }
        }

        public async Task ReportAsync(Play play, bool success = true, string? reason = null)
        {
            using var result = await http.PostAsJsonAsync("report", new { id = play.Id, success, reason }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        }

        public void Dispose() { stop.Cancel(); response.Dispose(); http.Dispose(); reading.GetAwaiter().GetResult(); reader.Dispose(); stop.Dispose(); }
    }
}
