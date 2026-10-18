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
    public async Task Mecha_lock_grand_warning_flashes_red_inside_the_dome(int width, int height)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, width, height);
        h.Client.Lottery.SelectTheme(LotteryTheme.MechaLock);
        h.Client.Lottery.Start([new("机械键盘", 1, Grade.Grand)], "停", 42);
        h.Client.Lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        // Freeze the deadline too: screenshot speed must not decide whether the scene is still visible.
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync("document.body.style.background = '#f0eae4'; document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); a.currentTime = 336; }); true");
        var lit = await browser.CommandAsync("Page.captureScreenshot", new { format = "png" });
        await browser.ScreenshotAsync($"{width}-grand-warning");
        await browser.EvaluateAsync("document.querySelector('.mechaAlert').hidden = true");
        var unlit = await browser.CommandAsync("Page.captureScreenshot", new { format = "png" });
        var difference = await browser.EvaluateAsync("""
            (async () => {
              const samples = [];
              for (const png of
            """ + JsonSerializer.Serialize(new[] { lit.GetProperty("data").GetString(), unlit.GetProperty("data").GetString() }) + """
              ) {
                const image = new Image(); image.src = 'data:image/png;base64,' + png; await image.decode();
                const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
                const ctx = canvas.getContext('2d'); ctx.drawImage(image,0,0);
                // Between the portrait tile columns, or below the landscape tiles; no text covers this point.
                const {data} = ctx.getImageData(image.width/2, image.width < image.height ? 1220 : 820, 1, 1);
                samples.push(data[0] - data[1]);
              }
              return samples[0] - samples[1];
            })()
            """);
        Assert.True(difference.GetInt32() > 30, $"The central red warning was barely visible: red-green change {difference}.");
        Assert.Empty(browser.Errors);
    }

    [Theory]
    [InlineData(1920, 1080, Grade.Grand)]
    [InlineData(1920, 1080, Grade.Second)]
    [InlineData(1920, 1080, Grade.Third)]
    [InlineData(1080, 1920, Grade.Grand)]
    [InlineData(1080, 1920, Grade.Second)]
    [InlineData(1080, 1920, Grade.Third)]
    public async Task Mecha_lock_scans_then_clamps_the_panel_and_sweeps_it_twice(int width, int height, Grade grade)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, width, height);
        h.Client.Lottery.SelectTheme(LotteryTheme.MechaLock);
        h.Client.Lottery.Start([new("机械键盘", 1, grade)], "停", 42);
        h.Client.Lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        Assert.True((await browser.EvaluateAsync("!!document.querySelector('.mechaClamp')")).GetBoolean(), "The result panel needs the TARGET LOCKED corner frame.");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync("document.body.style.background = '#f0eae4'; document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); }); true");
        foreach (var milliseconds in new[] { 336, 1200, 2140, 2450, 2520, 3000, 4550, 5000 })
        {
            await browser.EvaluateAsync($"document.getAnimations().forEach(a => a.currentTime = {milliseconds}); true");
            var effects = await browser.EvaluateAsync("""
                (() => {
                  const visible = selector => { const e = document.querySelector(selector), s = getComputedStyle(e); return s.display !== 'none' && +s.opacity > .1; };
                  const panel = document.getElementById('result').getBoundingClientRect(), beam = document.querySelector('.mechaBeam').getBoundingClientRect();
                  const hud = document.querySelector('.mechaHud').getBoundingClientRect();
                  return {radar:visible('.mechaRadar'), clamp:visible('.mechaClamp'), lines:visible('.mechaLines'), pulse:visible('.mechaPulse'),
                    beam:visible('.mechaBeam') && beam.top >= panel.top && beam.bottom <= panel.bottom,
                    centred:Math.abs(hud.left + hud.width/2 - innerWidth/2) < 4};
                })()
                """);
            Assert.True(effects.GetProperty("centred").GetBoolean());
            Assert.Equal(milliseconds < 1960, effects.GetProperty("radar").GetBoolean());
            Assert.Equal(milliseconds >= 2140, effects.GetProperty("clamp").GetBoolean());
            if (milliseconds <= 2140 || milliseconds >= 3000)
                Assert.Equal(grade != Grade.Third && milliseconds == 2140, effects.GetProperty("lines").GetBoolean());
            if (milliseconds == 1200) Assert.Equal(grade != Grade.Third, effects.GetProperty("pulse").GetBoolean());
            Assert.Equal(milliseconds is 2450 or 2520 or 4550, effects.GetProperty("beam").GetBoolean());
            await AssertLayoutAsync(browser);
            await browser.ScreenshotAsync($"{width}-{grade}-enhanced-{milliseconds}");
        }
        Assert.Empty(browser.Errors);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    [InlineData(800, 600)]
    public Task Capsule_gacha_renders_both_modes_all_grades_and_the_offline_example_through_real_SSE(int width, int height) =>
        RenderBothModesAsync(width, height, LotteryTheme.CapsuleGacha);

    [Theory]
    [InlineData(1920, 1080, "柚子不酸")]
    [InlineData(1920, 1080, "观众的超长中文昵称星河入梦与漫天星光")]
    [InlineData(1080, 1920, "柚子不酸")]
    [InlineData(1080, 1920, "观众的超长中文昵称星河入梦与漫天星光")]
    public async Task Capsule_gacha_banner_stays_inside_the_unmasked_area(int width, int height, string nickname)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, width, height);
        h.Client.Lottery.SelectTheme(LotteryTheme.CapsuleGacha);
        h.Client.Lottery.Start([new("机械键盘", 1, Grade.Grand)], "停", 42);
        await h.PushAndCollectAsync(watch, Message(42, nickname, "停"));
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync("document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); }); true");
        foreach (var milliseconds in new[] { 16, 50, 100, 154, 252, 1000, 1500, 1771, 1900, 2000 })
        {
            await browser.EvaluateAsync($"document.getAnimations().forEach(a => a.currentTime = {milliseconds}); true");
            var bounds = await browser.EvaluateAsync("""
                (() => { const r = document.getElementById('banner').getBoundingClientRect();
                  return {left:r.left, top:r.top, right:r.right, bottom:r.bottom}; })()
                """);
            Assert.True(bounds.GetProperty("top").GetDouble() >= 56 && bounds.GetProperty("left").GetDouble() >= 56 &&
                bounds.GetProperty("right").GetDouble() <= width - 56 && bounds.GetProperty("bottom").GetDouble() <= height - 56,
                $"{width} {nickname} {milliseconds}: banner enters the edge fade: {bounds}");
        }
        Assert.Empty(browser.Errors);
    }

    [Theory]
    [InlineData(1920, 1080, Grade.Grand)]
    [InlineData(1920, 1080, Grade.Second)]
    [InlineData(1920, 1080, Grade.Third)]
    [InlineData(1080, 1920, Grade.Grand)]
    [InlineData(1080, 1920, Grade.Second)]
    [InlineData(1080, 1920, Grade.Third)]
    public async Task Capsule_gacha_opens_with_beams_rings_and_a_sticker_then_holds_a_rotating_sun_and_stars(int width, int height, Grade grade)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, width, height);
        h.Client.Lottery.SelectTheme(LotteryTheme.CapsuleGacha);
        h.Client.Lottery.Start([new("机械键盘", 1, grade)], "停", 42);
        h.Client.Lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync("document.body.style.background = '#f0eae4'; document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); }); true");
        string? rotation = null;
        foreach (var milliseconds in new[] { 1500, 2050, 2250, 2800, 3400, 5000, 6500 })
        {
            await browser.EvaluateAsync($"document.getAnimations().forEach(a => a.currentTime = {milliseconds}); true");
            var effects = await browser.EvaluateAsync("""
                (() => {
                  const visible = selector => { const e = document.querySelector(selector); if (!e) return false;
                    const s = getComputedStyle(e); return s.display !== 'none' && +s.opacity > .1; };
                  return {beams:visible('.gachaBeams'), ring:visible('.gachaRing'), echo:visible('.gachaRing.echo'),
                    burst:visible('.gachaPopSticker'), sun:visible('.gachaSun'), stars:visible('.gachaSparkles'),
                    rotation:document.querySelector('.gachaSun') && getComputedStyle(document.querySelector('.gachaSun'), '::before').transform};
                })()
                """);
            Assert.Equal(milliseconds is 2050 or 2250, effects.GetProperty("beams").GetBoolean());
            Assert.Equal(milliseconds is 2050 or 2250, effects.GetProperty("ring").GetBoolean());
            Assert.Equal(grade != Grade.Third && milliseconds == 2250, effects.GetProperty("echo").GetBoolean());
            Assert.Equal(milliseconds is 2050 or 2250 or 2800, effects.GetProperty("burst").GetBoolean());
            Assert.Equal(milliseconds >= 2050, effects.GetProperty("sun").GetBoolean());
            Assert.Equal(milliseconds >= 2800, effects.GetProperty("stars").GetBoolean());
            if (milliseconds >= 3400)
            {
                var current = effects.GetProperty("rotation").GetString();
                Assert.NotEqual(rotation, current);
                rotation = current;
            }
            await AssertLayoutAsync(browser);
            await browser.ScreenshotAsync($"{width}-{grade}-enhanced-{milliseconds}");
        }
        Assert.Empty(browser.Errors);
    }

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
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, width, height);
        var portrait = width < height;
        Assert.Equal(portrait ? "portrait" : "landscape", (await browser.EvaluateAsync("document.getElementById('scene').dataset.orientation")).GetString());
        var lottery = h.Client.Lottery;
        var mecha = theme == LotteryTheme.MechaLock;
        var gacha = theme == LotteryTheme.CapsuleGacha;
        Assert.True((await browser.EvaluateAsync("document.getElementById('scene').hidden")).GetBoolean());
        lottery.New(LotteryMode.Entry);
        LotteryAward[] awards = [new("一等奖", "机械键盘", 1, Grade.Grand), new("二等奖", "周边抱枕", 1, Grade.Second), new("三等奖", "签名照", 1, Grade.Third)];
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
    [InlineData(LotteryTheme.StarSummon, 1920, 1080)]
    [InlineData(LotteryTheme.StarSummon, 1080, 1920)]
    [InlineData(LotteryTheme.MechaLock, 1920, 1080)]
    [InlineData(LotteryTheme.MechaLock, 1080, 1920)]
    [InlineData(LotteryTheme.CapsuleGacha, 1920, 1080)]
    [InlineData(LotteryTheme.CapsuleGacha, 1080, 1920)]
    public async Task Draw_effects_leave_forty_pixels_of_background_at_every_edge(LotteryTheme theme, int width, int height)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, width, height);
        h.Client.Lottery.SelectTheme(theme);
        await browser.EvaluateAsync("document.body.style.background = 'rgb(17, 231, 103)'; true");
        foreach (var grade in new[] { Grade.Grand, Grade.Second, Grade.Third })
        {
            h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
            h.Client.Lottery.Start([new("机械键盘", 1, grade)], "停", 42);
            await browser.WaitAsync("document.getElementById('scene').dataset.view === 'spinning'");
            h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
            h.Client.Lottery.Stop();
            await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
            foreach (var milliseconds in new[] { 350, 1400, 1850, 2000, 2300, 2800, 4000, 6000 })
            {
                await browser.WaitAsync($"document.getElementById('banner').getAnimations()[0].currentTime >= {milliseconds}");
                var png = await browser.CommandAsync("Page.captureScreenshot", new { format = "png" });
                var pixels = await browser.EvaluateAsync("""
                    (async () => {
                      const image = new Image(); image.src = 'data:image/png;base64,' +
                    """ + JsonSerializer.Serialize(png.GetProperty("data").GetString()) + """
                      ; await image.decode();
                      const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
                      const ctx = canvas.getContext('2d'); ctx.drawImage(image,0,0);
                      const {data} = ctx.getImageData(0,0,canvas.width,canvas.height);
                      let changed = 0, first;
                      for (let y = 0; y < canvas.height; y++) for (let x = 0; x < canvas.width; x++) {
                        if (x >= 40 && y >= 40 && x < canvas.width-40 && y < canvas.height-40) continue;
                        const i = (y*canvas.width+x)*4;
                        if (data[i] !== 17 || data[i+1] !== 231 || data[i+2] !== 103) { changed++; first ||= {x,y}; }
                      }
                      return {changed,first};
                    })()
                    """);
                await browser.ScreenshotAsync($"{width}-{grade}-edges-{milliseconds}");
                Assert.True(pixels.GetProperty("changed").GetInt32() == 0, $"{theme} {width} {grade} {milliseconds}: {pixels}");
            }
            await browser.WaitAsync("document.getElementById('scene').hidden");
            h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        }
        Assert.Empty(browser.Errors);
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, Grade.Grand)]
    [InlineData(LotteryTheme.StarSummon, Grade.Second)]
    [InlineData(LotteryTheme.StarSummon, Grade.Third)]
    [InlineData(LotteryTheme.MechaLock, Grade.Grand)]
    [InlineData(LotteryTheme.CapsuleGacha, Grade.Grand)]
    [InlineData(LotteryTheme.CapsuleGacha, Grade.Second)]
    [InlineData(LotteryTheme.CapsuleGacha, Grade.Third)]
    public async Task Portrait_transition_frames_keep_text_inside_the_frame(LotteryTheme theme, Grade grade)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, 1080, 1920);
        h.Client.Lottery.SelectTheme(theme);
        h.Client.Lottery.Start([new("机械键盘", 1, grade)], "停", 42);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'spinning'");
        await browser.ScreenshotAsync("portrait-scanning");
        await h.PushAndCollectAsync(watch, Message(42, "观众的超长中文昵称星河入梦与漫天星光", "停"));
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await browser.EvaluateAsync("document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); }); true");
        foreach (var milliseconds in new[] { 16, 33, 50, 100, 154, 252, 300, 1400, 1700, 1960, 2080, 2093, 2205, 2240, 2450, 2800, 4000, 6800 })
        {
            await browser.EvaluateAsync($"document.getAnimations().forEach(a => a.currentTime = {milliseconds}); true");
            await AssertLayoutAsync(browser);
        }
        Assert.Empty(browser.Errors);
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, 1)]
    [InlineData(LotteryTheme.StarSummon, 5)]
    [InlineData(LotteryTheme.MechaLock, 1)]
    [InlineData(LotteryTheme.MechaLock, 5)]
    [InlineData(LotteryTheme.CapsuleGacha, 1)]
    [InlineData(LotteryTheme.CapsuleGacha, 5)]
    public async Task Portrait_centres_awards_long_names_and_paged_results_after_resizing(LotteryTheme theme, int awards)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, 1080, 1920);
        var lottery = h.Client.Lottery;
        lottery.SelectTheme(theme);
        lottery.New(LotteryMode.Entry);
        const string keyword = "欢迎参加星辰召唤特别纪念限定抽奖活动";
        lottery.StartEntry(Enumerable.Range(1, awards).Select(i => new LotteryAward($"第{i}个特别纪念奖项", "星辰召唤限量纪念版机械键盘", 10, Grade.Grand)).ToArray(), keyword);
        var messages = Enumerable.Range(1, 7).Select(i => Message(i, $"观众{i}的超长中文昵称星河入梦", keyword)).ToArray();
        await h.PushAndCollectAsync(watch, messages);
        await browser.WaitAsync("document.getElementById('entryCount').textContent === '7'");
        await browser.WaitAsync("document.getElementById('entryCount').getAnimations().every(a => a.playState === 'finished')");
        var keywordSize = await browser.EvaluateAsync("(() => { const k = document.getElementById('keyword'); return {width:k.clientWidth,scrollWidth:k.scrollWidth,height:k.clientHeight,scrollHeight:k.scrollHeight,font:getComputedStyle(k).fontSize}; })()");
        Assert.True(keywordSize.GetProperty("scrollWidth").GetInt32() <= keywordSize.GetProperty("width").GetInt32() + 1
            && keywordSize.GetProperty("scrollHeight").GetInt32() <= keywordSize.GetProperty("height").GetInt32() + 1, keywordSize.GetRawText());
        await AssertLayoutAsync(browser);
        await browser.ScreenshotAsync($"1080-entry-{awards}-awards");
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
        await browser.WaitAsync("Array.from(document.querySelectorAll('.winnerCard')).every(c => c.getAnimations().every(a => a.playState === 'finished'))");
        await AssertLayoutAsync(browser);
        Assert.Empty(browser.Errors);
    }

    [Fact]
    public async Task Capsule_gacha_repages_grouped_long_names_when_rotating_a_closed_lottery()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        h.Time.Advance(DateTimeOffset.UtcNow - h.Time.GetUtcNow());
        await using var watch = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, 1920, 1080);
        var lottery = h.Client.Lottery;
        lottery.SelectTheme(LotteryTheme.CapsuleGacha);
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry(Enumerable.Range(1, 3).Select(i => new LotteryAward($"第{i}个特别纪念奖项", "限量纪念版机械键盘", 3, Grade.Grand)).ToArray(), "抽奖");
        await h.PushAndCollectAsync(watch, Enumerable.Range(1, 7).Select(i => Message(i, $"观众{i}的超长中文昵称星河入梦")).ToArray());
        lottery.CloseEntries();
        for (var i = 0; i < 6; i++) { lottery.DrawEntry(i / 2); h.Time.Advance(LotteryController.DrawDuration); }
        await browser.WaitAsync("document.querySelectorAll('.winnerName').length === 6");
        foreach (var (width, height) in new[] { (1080, 1920), (1920, 1080), (1080, 1920) })
        {
            await browser.CommandAsync("Emulation.setDeviceMetricsOverride", new { width, height, deviceScaleFactor = 1, mobile = false });
            await browser.WaitAsync($"document.querySelectorAll('.winnerName').length === {(width < height ? 5 : 6)}");
            await browser.WaitAsync("Array.from(document.querySelectorAll('.winnerCard')).every(c => c.getAnimations().every(a => a.playState === 'finished'))");
            await AssertLayoutAsync(browser);
        }
        await browser.ScreenshotAsync("1080-grouped-winners-rotated");
        lottery.EndEntry();
        await browser.WaitAsync("document.querySelectorAll('.winnerName').length === 6");
        await browser.WaitAsync("Array.from(document.querySelectorAll('.winnerCard')).every(c => c.getAnimations().every(a => a.playState === 'finished'))");
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
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.LotteryAddress, width, height);
        var lottery = h.Client.Lottery;
        const string name = "观众的超长中文昵称星河入梦与漫天星光", prize = "星辰召唤特别纪念限量联名豪华版无线机械键盘和配套主题礼盒";
        lottery.SelectTheme(LotteryTheme.CapsuleGacha);
        if (entry)
        {
            lottery.New(LotteryMode.Entry);
            lottery.StartEntry([new("一等奖", prize, 1, Grade.Grand)], "抽奖");
        }
        else lottery.Start([new(prize, 1, Grade.Grand)], "停", 42);
        await h.PushAndCollectAsync(watch, Message(42, name, entry ? "抽奖" : "停"));
        if (entry) { lottery.CloseEntries(); lottery.DrawEntry(0); }
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing' && document.fonts.status === 'loaded'");
        await browser.EvaluateAsync("document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); }); true");
        foreach (var milliseconds in new[] { 154, 252, 3000 })
        {
            await browser.EvaluateAsync($"document.getAnimations().forEach(a => a.currentTime = {milliseconds}); true");
            await AssertLayoutAsync(browser);
        }
        await browser.ScreenshotAsync($"{width}-long-{(entry ? "entry" : "pool")}-draw");
        var clipped = await browser.EvaluateAsync("['prize','viewer'].map(id => document.getElementById(id)).filter(e => e.scrollHeight > e.clientHeight + 1 || e.scrollWidth > e.clientWidth + 1).map(e => ({id:e.id,text:e.textContent,height:e.clientHeight,scrollHeight:e.scrollHeight,font:getComputedStyle(e).fontSize}))");
        Assert.True(clipped.GetArrayLength() == 0, clipped.GetRawText());
        Assert.True((await browser.EvaluateAsync($"['prize','viewer'].every(id => parseFloat(getComputedStyle(document.getElementById(id)).fontSize) >= {(width < height ? 40 : 56)})")).GetBoolean());
        await AssertLayoutAsync(browser);
        Assert.Empty(browser.Errors);
    }

    private static async Task AssertDrawAsync(ChromeBrowser browser, Grade grade, string mode, LotteryTheme theme)
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
            Assert.Contains(grade == Grade.Grand ? "S 级" : grade == Grade.Second ? "A 级" : "B 级",
                (await browser.EvaluateAsync("document.getElementById('rank').textContent")).GetString());
            Assert.Equal(grade != Grade.Third, (await browser.EvaluateAsync("getComputedStyle(document.querySelector('.hazard')).display !== 'none'")).GetBoolean());
        }
        else if (gacha)
        {
            await browser.WaitAsync("document.getElementById('result').getAnimations()[0].effect.getComputedTiming().progress >= .4");
            Assert.Equal(grade == Grade.Grand ? "大奖!!" : grade == Grade.Second ? "POP!" : "pop",
                (await browser.EvaluateAsync("document.getElementById('shout').textContent")).GetString());
            Assert.Equal(grade == Grade.Grand, (await browser.EvaluateAsync("getComputedStyle(document.querySelector('.gachaRainbow')).display !== 'none'")).GetBoolean());
        }
        else await browser.WaitAsync("getComputedStyle(document.querySelector('.flip')).transform === 'matrix(1, 0, 0, 1, 0, 0)'");
        if (!mecha && !gacha)
            Assert.NotEqual("rgba(0, 0, 0, 0)", (await browser.EvaluateAsync("getComputedStyle(document.getElementById('shout')).backgroundColor")).GetString());
        var revealedAt = DateTimeOffset.UtcNow;
        Assert.Equal(gacha ? width == 1080 ? "100px" : "120px" : mecha ? width == 1080 ? "116px" : "150px" : width == 1080 ? "96px" : "112px", (await browser.EvaluateAsync("getComputedStyle(document.getElementById('prize')).fontSize")).GetString());
        if (width == 1080 && grade == Grade.Grand)
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
        if (!mecha && !gacha && grade == Grade.Grand)
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

    private static async Task AssertLayoutAsync(ChromeBrowser browser)
    {
        // Measure rendered text in design coordinates; the board's secondary labels stay at 28–38px.
        var violations = await browser.EvaluateAsync("""
            (() => {
              const scene = document.getElementById('scene'), rect = scene.getBoundingClientRect();
              const portrait = scene.dataset.orientation === 'portrait';
              const scale = rect.width / (portrait ? 1080 : 1920);
              const bounds = portrait ? [0,0,1080,1920] : [0,0,1920,1080];
              const labels = '.label,#registrationTitle span+span,.countLabel,.awardName,.capacity,.candidateLabel,.candidateName,.winnerAward,.winnerPrize,#machineLabel span,#warning,.pilotCode,.pilotStatus,.mechaClamp span';
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
        var centre = await browser.EvaluateAsync("""
            (() => {
              const scene = document.getElementById('scene'), r = scene.getBoundingClientRect();
              if (scene.dataset.orientation !== 'portrait') return [];
              const scale = r.width / 1080;
              const summon = document.getElementById('summon');
              if (!summon.hidden) {
                // The stage's original design anchor maps to the source centre, including Gacha's asymmetric composition.
                const b = summon.getBoundingClientRect(), x = (b.left-r.left)/scale + 459, y = (b.top-r.top)/scale + 760;
                return Math.abs(x-540) > 4 || Math.abs(y-960) > 4 ? [{id:'summon',x,y}] : [];
              }
              if (scene.dataset.view === 'winners' && scene.dataset.theme === 'StarSummon') {
                const cards = Array.from(scene.querySelectorAll('.winnerCard'), e => e.getBoundingClientRect());
                if (cards.length) {
                  const x = ((Math.min(...cards.map(b => b.left)) + Math.max(...cards.map(b => b.right)))/2-r.left)/scale;
                  if (Math.abs(x-540) > 4) return [{id:'winnerCards',x}];
                }
              }
              return ['registration','winners'].map(id => document.getElementById(id)).filter(e => !e.hidden).flatMap(e => {
                const b = e.getBoundingClientRect(), x = (b.left + b.width/2 - r.left)/scale, y = (b.top + b.height/2 - r.top)/scale;
                return Math.abs(x - 540) > 4 || Math.abs(y - 960) > 4 ? [{id:e.id,x,y}] : [];
              });
            })()
            """);
        Assert.True(centre.GetArrayLength() == 0, centre.GetRawText());
    }

    private static string Message(long uid, string name, string keyword = "抽奖") => JsonSerializer.Serialize(new
    { cmd = "DANMU_MSG", info = new object[] { Array.Empty<object>(), keyword, new object[] { uid, name } } });
}
