using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryBrowserTests
{
    [Fact]
    public async Task Star_summon_renders_both_modes_all_grades_and_the_offline_example_through_real_SSE()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        void SyncClock() => h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        SyncClock();
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new LotteryBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress);
        var lottery = h.Client.Lottery;
        Assert.True((await browser.EvaluateAsync("document.getElementById('scene').hidden")).GetBoolean());
        lottery.New(LotteryMode.Entry);
        LotteryAward[] awards = [new("一等奖", "机械键盘", 1, PrizeGrade.Grand), new("二等奖", "周边抱枕", 1, PrizeGrade.Second), new("三等奖", "签名照", 1, PrizeGrade.Third)];
        lottery.StartEntry(awards, "抽奖");
        await h.PushAndCollectAsync(watch, Message(1, "柚子不酸"), Message(2, "星河入梦"), Message(3, "一只小鹿"));
        await browser.WaitAsync("document.getElementById('entryCount').textContent === '3'");
        await browser.WaitAsync("document.fonts.check('56px \"Smiley Sans\"')");
        Assert.Equal("130px", (await browser.EvaluateAsync("getComputedStyle(document.getElementById('entryCount')).fontSize")).GetString());
        Assert.True((await browser.EvaluateAsync("getComputedStyle(document.body).backgroundColor === 'rgba(0, 0, 0, 0)'")).GetBoolean());
        Assert.True((await browser.EvaluateAsync("document.getElementById('entryBar').getBoundingClientRect().top === 800")).GetBoolean());
        await browser.ScreenshotAsync("entry-open");
        lottery.CloseEntries();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'winners'");
        await browser.ScreenshotAsync("entry-closed-empty");
        for (var index = 0; index < awards.Length; index++)
        {
            SyncClock();
            lottery.DrawEntry(index);
            await AssertDrawAsync(browser, awards[index].Grade, "entry");
            SyncClock();
        }
        lottery.EndEntry();
        await browser.WaitAsync("document.getElementById('winnerTitle').textContent === '召唤结果'");
        await browser.WaitAsync("Array.from(document.querySelectorAll('.winnerCard')).every(card => card.getAnimations().every(animation => animation.playState === 'finished'))");
        await browser.ScreenshotAsync("entry-ended");
        lottery.SetEntryHidden(true);
        await browser.WaitAsync("document.getElementById('scene').hidden");
        lottery.SetEntryHidden(false);
        await browser.WaitAsync("!document.getElementById('scene').hidden");
        lottery.New(LotteryMode.Pool);
        foreach (var award in awards)
        {
            SyncClock();
            lottery.Start([new(award.Prize, 1, award.Grade)], "停", 42);
            await browser.WaitAsync("document.getElementById('scene').dataset.view === 'spinning'");
            Assert.Equal("", (await browser.EvaluateAsync("document.getElementById('banner').textContent")).GetString());
            await browser.ScreenshotAsync("pool-spinning");
            SyncClock();
            lottery.Stop();
            await AssertDrawAsync(browser, award.Grade, "pool");
            SyncClock();
        }
        await watch.DisposeAsync();
        SyncClock();
        h.Client.WebService.Lottery.PlayExample();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'registration'");
        await browser.ScreenshotAsync("example-entry");
        // Advance Core with real time so CSS, JS timers and SSE deadlines are checked together.
        for (var second = 0; second < 22; second++)
        {
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            SyncClock();
            if (second == 7) await browser.ScreenshotAsync("example-entry-draw");
            if (second == 17) await browser.ScreenshotAsync("example-pool-draw");
        }
        await browser.WaitAsync("document.getElementById('scene').hidden");
        Assert.False(lottery.State.IsExamplePlaying);
        Assert.Equal(2, lottery.GetHistory(7734200).Length);
        Assert.Empty(browser.Errors);
        Assert.Contains(browser.Requests, url => url.EndsWith("/lottery/SmileySans-Oblique.woff2"));
        Assert.All(browser.Requests.Where(url => !url.StartsWith("data:")), url => Assert.Equal("localhost", new Uri(url).Host));
    }

    private static async Task AssertDrawAsync(LotteryBrowser browser, PrizeGrade grade, string mode)
    {
        await browser.WaitAsync($"document.getElementById('summon').className === 'drawing {grade}'");
        await browser.WaitAsync("getComputedStyle(document.querySelector('.flip')).transform === 'matrix(1, 0, 0, 1, 0, 0)'");
        var revealedAt = DateTimeOffset.UtcNow;
        Assert.Equal("112px", (await browser.EvaluateAsync("getComputedStyle(document.getElementById('prize')).fontSize")).GetString());
        await browser.ScreenshotAsync($"{mode}-{grade}");
        var wait = revealedAt.AddSeconds(4) - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds) + 20), TestContext.Current.CancellationToken);
        Assert.True((await browser.EvaluateAsync("!document.getElementById('scene').hidden && document.getElementById('scene').dataset.view === 'drawing'")).GetBoolean());
        Assert.True(DateTimeOffset.UtcNow - revealedAt >= TimeSpan.FromSeconds(4));
        await browser.WaitAsync(mode == "entry" ? "document.getElementById('scene').dataset.view === 'winners'" : "document.getElementById('scene').hidden");
        if (mode == "entry")
        {
            await browser.WaitAsync("Array.from(document.querySelectorAll('.winnerCard')).every(card => card.getAnimations().every(animation => animation.playState === 'finished'))");
            await browser.ScreenshotAsync("entry-between-" + grade);
        }
    }

    private static string Message(long uid, string name) => JsonSerializer.Serialize(new
    { cmd = "DANMU_MSG", info = new object[] { Array.Empty<object>(), "抽奖", new object[] { uid, name } } });
}
