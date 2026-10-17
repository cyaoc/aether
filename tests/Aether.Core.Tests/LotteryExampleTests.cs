using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryExampleTests
{
    [Fact]
    public async Task Ordinary_chat_keeps_the_example_playing_but_a_real_draw_takes_the_screen_back()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await h.LoginAsync();
        await using var watch = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start([new("贴纸", 1, PrizeGrade.Third)], "停", 42);
        h.Client.WebService.Lottery.PlayExample();
        await h.PushAndCollectAsync(watch, """{"cmd":"DANMU_MSG","info":[[],"你好",[42,"新观众"]]}""");
        Assert.True(lottery.State.IsExamplePlaying);
        await h.PushAndCollectAsync(watch, """{"cmd":"DANMU_MSG","info":[[],"停",[42,"新观众"]]}""");
        Assert.False(lottery.State.IsExamplePlaying);
        Assert.Equal(PrizeGrade.Third, Assert.Single(lottery.State.Lottery!.Draws).Prize.Grade);
    }

    [Fact]
    public async Task Lottery_serves_its_font_license_styles_and_script_locally_and_example_snapshots_reach_SSE()
    {
        await using var h = new WatchHarness();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Address) };
        foreach (var path in new[] { "lottery", "lottery/style.css", "lottery/page.js", "lottery/SmileySans-Oblique.woff2", "lottery/OFL.txt" })
        {
            using var asset = await http.GetAsync(path, h.Stop.Token);
            Assert.Equal(System.Net.HttpStatusCode.OK, asset.StatusCode);
            Assert.NotEmpty(await asset.Content.ReadAsByteArrayAsync(h.Stop.Token));
        }
        Assert.Contains("SIL OPEN FONT LICENSE", await http.GetStringAsync("lottery/OFL.txt", h.Stop.Token));
        h.Client.WebService.Lottery.PlayExample();
        using var response = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string? line;
        do { line = await reader.ReadLineAsync(timeout.Token); } while (line is not null && !line.StartsWith("data: "));
        Assert.NotNull(line);
        using var json = System.Text.Json.JsonDocument.Parse(line[6..]);
        Assert.Equal("Open", json.RootElement.GetProperty("entryLottery").GetProperty("phase").GetString());
        Assert.True(json.RootElement.GetProperty("isExamplePlaying").GetBoolean());
        Assert.Equal("StarSummon", json.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task A_live_change_cancels_the_example_and_keeps_real_history_and_stale_timers_separate()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await h.LoginAsync();
        await using var watch = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        var original = lottery.State.EntryLottery;
        var history = lottery.GetHistory(7734200);
        h.Client.WebService.Lottery.PlayExample();
        Assert.Same(original, lottery.State.EntryLottery);
        Assert.Equal(history.Select(item => (item.Id, item.DrawCount, item.EntryCount)),
            lottery.GetHistory(7734200).Select(item => (item.Id, item.DrawCount, item.EntryCount)));
        h.Time.HoldCallbacksFor = TimeSpan.FromSeconds(3);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        lottery.StartEntry([new("奖项", "贴纸", 1, PrizeGrade.Third)], "报名");
        Assert.False(lottery.State.IsExamplePlaying);
        h.Client.WebService.Lottery.PlayExample();
        h.Time.HoldCallbacksFor = null;
        h.Time.RunHeldCallbacks();
        await using var stream = lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.Equal(EntryLotteryPhase.Open, (await h.NextAsync(stream)).Example!.EntryLottery!.Phase);
        await h.Client.WebService.StopAsync();
        Assert.False(lottery.State.IsExamplePlaying);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(EntryLotteryPhase.Open, lottery.State.EntryLottery!.Phase);
        Assert.Empty(lottery.State.EntryLottery.Draws);
        Assert.Single(lottery.GetHistory(7734200));
    }

    [Fact]
    public async Task Example_plays_both_modes_offline_then_restores_the_page_without_creating_a_database()
    {
        await using var h = new WatchHarness();
        var service = h.Client.WebService;
        Assert.Contains("网页服务", Assert.Throws<InvalidOperationException>(service.Lottery.PlayExample).Message);
        await service.StartAsync(WelcomeAnimationTests.FreePort());
        await using var updates = h.Client.Lottery.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.Equal(LotteryTheme.StarSummon, (await h.NextAsync(updates)).State.Theme);
        service.Lottery.PlayExample();
        var first = await h.NextAsync(updates);
        Assert.True(first.State.IsExamplePlaying);
        Assert.Null(first.State.EntryLottery);
        Assert.Equal(EntryLotteryPhase.Open, first.Example!.EntryLottery!.Phase);
        Assert.Throws<InvalidOperationException>(service.Lottery.PlayExample);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(EntryLotteryPhase.Closed, (await h.NextAsync(updates)).Example!.EntryLottery!.Phase);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        var entry = (await h.NextAsync(updates)).Example!.EntryLottery!;
        Assert.Equal(PrizeGrade.Grand, Assert.Single(entry.Draws).Award.Grade);
        Assert.Equal(TimeSpan.FromSeconds(7), entry.DrawUntil - entry.Draws[0].DrawnAt);
        h.Time.Advance(TimeSpan.FromSeconds(7));
        Assert.Equal(EntryLotteryPhase.Ended, (await h.NextAsync(updates)).Example!.EntryLottery!.Phase);
        h.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(PoolLotteryPhase.Spinning, (await h.NextAsync(updates)).Example!.Lottery!.Phase);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        var pool = (await h.NextAsync(updates)).Example!.Lottery!;
        Assert.Equal(PrizeGrade.Second, Assert.Single(pool.Draws).Prize.Grade);
        h.Time.Advance(TimeSpan.FromSeconds(7));
        var last = await h.NextAsync(updates);
        Assert.False(last.State.IsExamplePlaying);
        Assert.Null(last.Example);
        Assert.Null(last.State.Lottery);
        Assert.False(File.Exists(Path.Combine(h.DataDirectory, "aether.db")));
        Assert.Empty(h.Http.Requests);
    }
}
