using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryBrowserTests
{
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    [InlineData(800, 600)]
    public Task Star_summon_renders_both_modes_all_grades_and_the_offline_example_through_real_SSE(int width, int height) =>
        RenderBothModesAsync(width, height, LotteryTheme.StarSummon);

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    [InlineData(800, 600)]
    public Task Mecha_lock_renders_both_modes_all_grades_and_the_offline_example_through_real_SSE(int width, int height) =>
        RenderBothModesAsync(width, height, LotteryTheme.MechaLock);

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    [InlineData(800, 600)]
    public Task Capsule_gacha_renders_both_modes_all_grades_and_the_offline_example_through_real_SSE(int width, int height) =>
        RenderBothModesAsync(width, height, LotteryTheme.CapsuleGacha);

    private static async Task RenderBothModesAsync(int width, int height, LotteryTheme theme)
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
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, width, height);
        var portrait = width < height;
        Assert.Equal(portrait ? "portrait" : "landscape", (await browser.EvaluateAsync("document.getElementById('scene').dataset.orientation")).GetString());
        var lottery = h.Client.Lottery;
        var mecha = theme == LotteryTheme.MechaLock;
        var gacha = theme == LotteryTheme.CapsuleGacha;
        Assert.True((await browser.EvaluateAsync("document.getElementById('scene').hidden")).GetBoolean());
        lottery.New(LotteryMode.Entry);
        LotteryAward[] awards = [new("一等奖", "机械键盘", 1, PrizeGrade.Grand), new("二等奖", "周边抱枕", 1, PrizeGrade.Second), new("三等奖", "签名照", 1, PrizeGrade.Third)];
        lottery.StartEntry(awards, "抽奖");
        await h.PushAndCollectAsync(watch, Message(1, "柚子不酸"), Message(2, "星河入梦"), Message(3, "一只小鹿"));
        await browser.WaitAsync("document.getElementById('entryCount').textContent === '3'");
        lottery.SelectTheme(theme);
        await browser.WaitAsync($"document.getElementById('scene').dataset.theme === '{theme}'");
        Assert.Equal(gacha ? "扭蛋抽奖" : mecha ? "目标锁定" : "星辰召唤", (await browser.EvaluateAsync("document.getElementById('themeTitle').textContent")).GetString());
        await browser.WaitAsync(gacha ? "document.fonts.check('56px \"ZCOOL KuaiLe\"')" : "document.fonts.check('56px \"Smiley Sans\"')");
        Assert.Equal(gacha ? portrait ? "92px" : "120px" : mecha ? portrait ? "120px" : "136px" : portrait ? "116px" : "130px", (await browser.EvaluateAsync("getComputedStyle(document.getElementById('entryCount')).fontSize")).GetString());
        Assert.True((await browser.EvaluateAsync("getComputedStyle(document.body).backgroundColor === 'rgba(0, 0, 0, 0)'")).GetBoolean());
        if (!portrait)
            Assert.Equal(gacha ? width == 1920 ? 790 : 404.167 : width == 1920 ? 800 : 408.333, (await browser.EvaluateAsync("document.getElementById('entryBar').getBoundingClientRect().top")).GetDouble(), 2);
        Assert.Equal(portrait ? 3 : 2, (await browser.EvaluateAsync("Array.from(document.querySelectorAll('#latestEntries > div')).filter(e => e.getBoundingClientRect().height > 0).length")).GetInt32());
        await AssertLayoutAsync(browser);
        await browser.WaitAsync("Array.from(document.querySelectorAll('#registration *')).every(e => e.getAnimations().every(a => a.playState === 'finished'))");
        await browser.ScreenshotAsync($"{width}-entry-open");
        lottery.CloseEntries();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'winners'");
        await AssertLayoutAsync(browser);
        await browser.ScreenshotAsync($"{width}-entry-closed-empty");
        for (var index = 0; index < awards.Length; index++)
        {
            SyncClock();
            lottery.DrawEntry(index);
            await AssertDrawAsync(browser, awards[index].Grade, "entry", theme);
            SyncClock();
        }
        lottery.EndEntry();
        await browser.WaitAsync($"document.getElementById('winnerTitle').textContent === '{(gacha ? "中奖啦！" : mecha ? "作战结果" : "召唤结果")}'");
        await browser.WaitAsync("Array.from(document.querySelectorAll('.winnerCard')).every(card => card.getAnimations().every(animation => animation.playState === 'finished'))");
        await AssertLayoutAsync(browser);
        await browser.ScreenshotAsync($"{width}-entry-ended");
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
            await AssertLayoutAsync(browser);
            await browser.ScreenshotAsync($"{width}-pool-spinning");
            SyncClock();
            lottery.Stop();
            await AssertDrawAsync(browser, award.Grade, "pool", theme);
            SyncClock();
        }
        await watch.DisposeAsync();
        SyncClock();
        h.Client.WebService.Lottery.PlayExample();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'registration'");
        await browser.ScreenshotAsync($"{width}-example-entry");
        // Advance Core with real time so CSS, JS timers and SSE deadlines are checked together.
        for (var second = 0; second < 22; second++)
        {
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            SyncClock();
            if (second == 7) await browser.ScreenshotAsync($"{width}-example-entry-draw");
            if (second == 17) await browser.ScreenshotAsync($"{width}-example-pool-draw");
        }
        await browser.WaitAsync("document.getElementById('scene').hidden");
        Assert.False(lottery.State.IsExamplePlaying);
        Assert.Equal(2, lottery.GetHistory(7734200).Length);
        Assert.Empty(browser.Errors);
        Assert.Contains(browser.Requests, url => url.EndsWith(gacha ? "/lottery/ZCOOLKuaiLe-Regular.ttf" : "/lottery/SmileySans-Oblique.woff2"));
        Assert.All(browser.Requests.Where(url => !url.StartsWith("data:")), url => Assert.Equal("localhost", new Uri(url).Host));
    }

    [Theory]
    [InlineData(LotteryTheme.MechaLock, PrizeGrade.Grand)]
    [InlineData(LotteryTheme.CapsuleGacha, PrizeGrade.Grand)]
    [InlineData(LotteryTheme.CapsuleGacha, PrizeGrade.Second)]
    [InlineData(LotteryTheme.CapsuleGacha, PrizeGrade.Third)]
    public async Task Portrait_transition_frames_keep_text_inside_the_safe_area(LotteryTheme theme, PrizeGrade grade)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new LotteryBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, 1080, 1920);
        h.Client.Lottery.SelectTheme(theme);
        h.Client.Lottery.Start([new("机械键盘", 1, grade)], "停", 42);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'spinning'");
        await browser.ScreenshotAsync("portrait-scanning");
        h.Client.Lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await browser.EvaluateAsync("document.getAnimations().forEach(a => a.pause()); true");
        foreach (var milliseconds in new[] { 100, 300, 1400, 1700, 1960, 2080, 2240, 2450, 2800, 4000, 6800 })
        {
            await browser.EvaluateAsync($"document.getAnimations().forEach(a => a.currentTime = {milliseconds}); true");
            await AssertLayoutAsync(browser);
        }
        Assert.Empty(browser.Errors);
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon)]
    [InlineData(LotteryTheme.MechaLock)]
    [InlineData(LotteryTheme.CapsuleGacha)]
    public async Task Portrait_keeps_five_awards_long_names_and_paged_results_inside_the_safe_area_after_resizing(LotteryTheme theme)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new LotteryBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, 1080, 1920);
        var lottery = h.Client.Lottery;
        lottery.SelectTheme(theme);
        lottery.New(LotteryMode.Entry);
        const string keyword = "欢迎参加星辰召唤特别纪念限定抽奖活动";
        lottery.StartEntry(Enumerable.Range(1, 5).Select(i => new LotteryAward($"第{i}个特别纪念奖项", "星辰召唤限量纪念版机械键盘", 10, PrizeGrade.Grand)).ToArray(), keyword);
        var messages = Enumerable.Range(1, 7).Select(i => Message(i, $"观众{i}的超长中文昵称星河入梦", keyword)).ToArray();
        await h.PushAndCollectAsync(watch, messages);
        await browser.WaitAsync("document.getElementById('entryCount').textContent === '7'");
        await browser.WaitAsync("document.getElementById('entryCount').getAnimations().every(a => a.playState === 'finished')");
        var keywordSize = await browser.EvaluateAsync("(() => { const k = document.getElementById('keyword'); return {width:k.clientWidth,scrollWidth:k.scrollWidth,height:k.clientHeight,scrollHeight:k.scrollHeight,font:getComputedStyle(k).fontSize}; })()");
        Assert.True(keywordSize.GetProperty("scrollWidth").GetInt32() <= keywordSize.GetProperty("width").GetInt32() + 1
            && keywordSize.GetProperty("scrollHeight").GetInt32() <= keywordSize.GetProperty("height").GetInt32() + 1, keywordSize.GetRawText());
        await AssertLayoutAsync(browser);
        await browser.ScreenshotAsync("1080-entry-five-awards");
        foreach (var (width, height) in new[] { (800, 600), (800, 800), (540, 960), (1080, 1920) })
        {
            await browser.CommandAsync("Emulation.setDeviceMetricsOverride", new { width, height, deviceScaleFactor = 1, mobile = false });
            var portrait = width < height;
            await browser.WaitAsync($"document.getElementById('scene').dataset.orientation === '{(portrait ? "portrait" : "landscape")}'");
            await AssertLayoutAsync(browser);
            Assert.Equal(portrait ? 3 : 2, (await browser.EvaluateAsync("Array.from(document.querySelectorAll('#latestEntries > div')).filter(e => e.getBoundingClientRect().height > 0).length")).GetInt32());
        }
        lottery.CloseEntries();
        // Build seven real results without waiting for seven animations; the other test checks their timing.
        for (var i = 0; i < 7; i++) { lottery.DrawEntry(0); h.Time.Advance(LotteryController.DrawDuration); }
        await browser.WaitAsync("document.getElementById('pageNumber').textContent === '1 / 2'");
        await browser.WaitAsync("Array.from(document.querySelectorAll('.winnerCard')).every(c => c.getAnimations().every(a => a.playState === 'finished'))");
        await AssertLayoutAsync(browser);
        await browser.ScreenshotAsync("1080-winners-page-1");
        var clippedNames = await browser.EvaluateAsync("Array.from(document.querySelectorAll('.winnerName')).filter(e => e.scrollHeight > e.clientHeight + 1 || e.scrollWidth > e.clientWidth + 1).map(e => ({text:e.textContent,width:e.clientWidth,height:e.clientHeight,scrollWidth:e.scrollWidth,scrollHeight:e.scrollHeight,font:getComputedStyle(e).fontSize}))");
        Assert.True(clippedNames.GetArrayLength() == 0, clippedNames.GetRawText());
        await browser.WaitAsync("document.getElementById('pageNumber').textContent === '2 / 2'");
        await browser.WaitAsync("Array.from(document.querySelectorAll('.winnerCard')).every(c => c.getAnimations().every(a => a.playState === 'finished'))");
        await AssertLayoutAsync(browser);
        await browser.ScreenshotAsync("1080-winners-page-2");
        lottery.EndEntry();
        await browser.WaitAsync($"document.getElementById('winnerTitle').textContent === '{(theme == LotteryTheme.CapsuleGacha ? "中奖啦！" : theme == LotteryTheme.MechaLock ? "作战结果" : "召唤结果")}'");
        await AssertLayoutAsync(browser);
        Assert.Empty(browser.Errors);
    }

    [Theory]
    [InlineData(1920, 1080, false)]
    [InlineData(1920, 1080, true)]
    [InlineData(1080, 1920, false)]
    [InlineData(1080, 1920, true)]
    public async Task Capsule_gacha_keeps_long_draw_text_readable(int width, int height, bool entry)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new LotteryBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, width, height);
        var lottery = h.Client.Lottery;
        const string name = "观众的超长中文昵称星河入梦与漫天星光", prize = "星辰召唤特别纪念限量联名豪华版无线机械键盘和配套主题礼盒";
        lottery.SelectTheme(LotteryTheme.CapsuleGacha);
        if (entry)
        {
            lottery.New(LotteryMode.Entry);
            lottery.StartEntry([new("一等奖", prize, 1, PrizeGrade.Grand)], "抽奖");
        }
        else lottery.Start([new(prize, 1, PrizeGrade.Grand)], "停", 42);
        await h.PushAndCollectAsync(watch, Message(42, name, entry ? "抽奖" : "停"));
        if (entry) { lottery.CloseEntries(); lottery.DrawEntry(0); }
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing' && document.fonts.status === 'loaded'");
        await browser.EvaluateAsync("document.getAnimations().forEach(a => { a.pause(); a.currentTime = 3000; }); true");
        await browser.ScreenshotAsync($"{width}-long-{(entry ? "entry" : "pool")}-draw");
        var clipped = await browser.EvaluateAsync("['prize','viewer'].map(id => document.getElementById(id)).filter(e => e.scrollHeight > e.clientHeight + 1 || e.scrollWidth > e.clientWidth + 1).map(e => ({id:e.id,text:e.textContent,height:e.clientHeight,scrollHeight:e.scrollHeight,font:getComputedStyle(e).fontSize}))");
        Assert.True(clipped.GetArrayLength() == 0, clipped.GetRawText());
        Assert.True((await browser.EvaluateAsync($"['prize','viewer'].every(id => parseFloat(getComputedStyle(document.getElementById(id)).fontSize) >= {(width < height ? 40 : 56)})")).GetBoolean());
        await AssertLayoutAsync(browser);
        Assert.Empty(browser.Errors);
    }

    private static async Task AssertDrawAsync(LotteryBrowser browser, PrizeGrade grade, string mode, LotteryTheme theme)
    {
        var mecha = theme == LotteryTheme.MechaLock;
        var gacha = theme == LotteryTheme.CapsuleGacha;
        var width = (await browser.EvaluateAsync("window.innerWidth")).GetInt32();
        await browser.WaitAsync($"document.getElementById('summon').className === 'drawing {grade}'");
        // The board's banner pops in from 1.6x and drifts up as it fades; check it while it holds still.
        await browser.WaitAsync("(p => p > .05 && p < .25)(document.getElementById('banner').getAnimations()[0].effect.getComputedTiming().progress)");
        await AssertLayoutAsync(browser);
        if (mecha)
        {
            Assert.Contains("linear-gradient", (await browser.EvaluateAsync("getComputedStyle(document.querySelector('.sky')).backgroundImage")).GetString());
            await browser.WaitAsync("document.querySelector('[data-lock=\"LOCK ON\"]')");
            Assert.Equal((await browser.EvaluateAsync("document.getElementById('prize').textContent")).GetString(),
                (await browser.EvaluateAsync("document.querySelector('[data-lock=\"LOCK ON\"] .candidateName').textContent")).GetString());
            await browser.ScreenshotAsync($"{width}-{mode}-{grade}-locked");
            await browser.WaitAsync("document.getElementById('result').getAnimations()[0].effect.getComputedTiming().progress >= .4");
            Assert.Contains(grade == PrizeGrade.Grand ? "S 级" : grade == PrizeGrade.Second ? "A 级" : "B 级",
                (await browser.EvaluateAsync("document.getElementById('rank').textContent")).GetString());
            Assert.Equal(grade != PrizeGrade.Third, (await browser.EvaluateAsync("getComputedStyle(document.querySelector('.hazard')).display !== 'none'")).GetBoolean());
        }
        else if (gacha)
        {
            await browser.WaitAsync("document.getElementById('result').getAnimations()[0].effect.getComputedTiming().progress >= .4");
            Assert.Equal(grade == PrizeGrade.Grand ? "大奖!!" : grade == PrizeGrade.Second ? "POP!" : "pop",
                (await browser.EvaluateAsync("document.getElementById('shout').textContent")).GetString());
            Assert.Equal(grade == PrizeGrade.Grand, (await browser.EvaluateAsync("getComputedStyle(document.querySelector('.gachaRainbow')).display !== 'none'")).GetBoolean());
        }
        else await browser.WaitAsync("getComputedStyle(document.querySelector('.flip')).transform === 'matrix(1, 0, 0, 1, 0, 0)'");
        var revealedAt = DateTimeOffset.UtcNow;
        Assert.Equal(gacha ? width == 1080 ? "100px" : "120px" : mecha ? width == 1080 ? "116px" : "150px" : width == 1080 ? "96px" : "112px", (await browser.EvaluateAsync("getComputedStyle(document.getElementById('prize')).fontSize")).GetString());
        if (width == 1080 && grade == PrizeGrade.Grand)
        {
            var animationTimes = mecha || gacha ? "['#banner','#result'].map(s => document.querySelector(s).getAnimations()[0].currentTime)" : "['#banner','.meteor'].map(s => document.querySelector(s).getAnimations()[0].currentTime)";
            var before = await browser.EvaluateAsync(animationTimes);
            foreach (var (w, h) in new[] { (1920, 1080), (1080, 1920) })
            {
                await browser.CommandAsync("Emulation.setDeviceMetricsOverride", new { width = w, height = h, deviceScaleFactor = 1, mobile = false });
                await browser.WaitAsync($"document.getElementById('scene').dataset.orientation === '{(w < h ? "portrait" : "landscape")}'");
                var after = await browser.EvaluateAsync(animationTimes);
                for (var i = 0; i < 2; i++) Assert.True(after[i].GetDouble() >= before[i].GetDouble(), "Resizing restarted the draw animation.");
            }
        }
        if (!mecha && !gacha && grade == PrizeGrade.Grand)
            await browser.WaitAsync("getComputedStyle(document.getElementById('shout')).opacity === '1'");
        await AssertLayoutAsync(browser);
        await browser.ScreenshotAsync($"{width}-{mode}-{grade}");
        var wait = revealedAt.AddSeconds(4) - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Ceiling(wait.TotalMilliseconds) + 20), TestContext.Current.CancellationToken);
        Assert.True((await browser.EvaluateAsync("!document.getElementById('scene').hidden && document.getElementById('scene').dataset.view === 'drawing'")).GetBoolean());
        Assert.True(DateTimeOffset.UtcNow - revealedAt >= TimeSpan.FromSeconds(4));
        await browser.WaitAsync(mode == "entry" ? "document.getElementById('scene').dataset.view === 'winners'" : "document.getElementById('scene').hidden");
        if (mode == "entry")
        {
            await browser.WaitAsync("Array.from(document.querySelectorAll('.winnerCard')).every(card => card.getAnimations().every(animation => animation.playState === 'finished'))");
            await AssertLayoutAsync(browser);
            await browser.ScreenshotAsync($"{width}-entry-between-{grade}");
        }
    }

    private static async Task AssertLayoutAsync(LotteryBrowser browser)
    {
        // Measure rendered text, including the 3D ring, against SA1 in design coordinates; the board's labels stay at 28–38px.
        var violations = await browser.EvaluateAsync("""
            (() => {
              const scene = document.getElementById('scene'), rect = scene.getBoundingClientRect();
              const portrait = scene.dataset.orientation === 'portrait';
              const scale = rect.width / (portrait ? 1080 : 1920);
              const bounds = portrait ? [54,288,864,1248] : [0,0,1920,1080];
              const labels = '.label,#registrationTitle span+span,.countLabel,.awardName,.capacity,.candidateLabel,.candidateName,.winnerAward,#warning,.pilotCode,.pilotStatus';
              return Array.from(scene.querySelectorAll('*')).filter(e =>
                e.childNodes.length && Array.from(e.childNodes).some(n => n.nodeType === 3 && n.textContent.trim())
              ).flatMap(e => {
                const r = e.getBoundingClientRect();
                if (!r.width || !r.height) return [];
                for (let p = e; p && p !== scene; p = p.parentElement)
                  if (getComputedStyle(p).opacity === '0' || getComputedStyle(p).visibility === 'hidden' || getComputedStyle(p).display === 'none') return [];
                const x = (r.left-rect.left)/scale, y = (r.top-rect.top)/scale;
                const outside = x < bounds[0]-1 || y < bounds[1]-1 || x+r.width/scale > bounds[2]+1 || y+r.height/scale > bounds[3]+1;
                const small = portrait && !e.matches(labels) && parseFloat(getComputedStyle(e).fontSize) < 40;
                return outside || small ? [{text:e.textContent,x,y,width:r.width/scale,height:r.height/scale,small}] : [];
              });
            })()
            """);
        Assert.True(violations.GetArrayLength() == 0, violations.GetRawText());
    }

    private static string Message(long uid, string name, string keyword = "抽奖") => JsonSerializer.Serialize(new
    { cmd = "DANMU_MSG", info = new object[] { Array.Empty<object>(), keyword, new object[] { uid, name } } });
}
