using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
    // Oldest first: the RB/NP boards' rows, newest at the top.
    private static readonly (Grade Grade, string Prize, string? Who)[] PinnedPool =
        [(Grade.Third,"签名照","阿狸"), (Grade.Grand,"机械键盘","晚风"), (Grade.Second,"周边抱枕",null), (Grade.Third,"贴纸套装","一只小鹿"), (Grade.Grand,"机械键盘","柚子不酸")];

    [Theory]
    [InlineData(LotteryTheme.StarSummon, 1920, 1080)]
    [InlineData(LotteryTheme.StarSummon, 1080, 1920)]
    [InlineData(LotteryTheme.MechaLock, 1920, 1080)]
    [InlineData(LotteryTheme.MechaLock, 1080, 1920)]
    [InlineData(LotteryTheme.CapsuleGacha, 1920, 1080)]
    [InlineData(LotteryTheme.CapsuleGacha, 1080, 1920)]
    public async Task Pinned_pool_uses_real_results_pages_hides_and_scales_without_replaying(LotteryTheme theme, int width, int height)
    {
        await using var page = await OpenPageAsync(width, height);
        var (h, browser) = (page.H, page.Browser);
        var lottery = h.Client.Lottery;
        lottery.SelectTheme(theme);
        await SeedPinnedPoolAsync(page, PinnedPool);
        lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        await FreezePinnedAsync(browser, 3000);
        Assert.Equal(theme == LotteryTheme.MechaLock ? "5 次" : theme == LotteryTheme.CapsuleGacha ? "共 5 颗" : "共 5 次", (await browser.EvaluateAsync("document.getElementById('pinnedCount').textContent")).GetString());
        Assert.Equal("柚子不酸", (await browser.EvaluateAsync("document.querySelector('.pinnedWho').textContent")).GetString());
        Assert.False((await browser.EvaluateAsync("document.getElementById('pinned').classList.contains('arriving')")).GetBoolean());
        await AssertPinnedLayoutAsync(browser, width, height);
        await browser.ScreenshotAsync($"{width}-pinned-static");
        foreach (var scale in new[] { .5, .55 })
        {
            var w = (int)(width * scale); var ht = (int)(height * scale);
            await using var scaled = new ChromeBrowser();
            await scaled.OpenAsync(Path.Combine(h.DataDirectory, "scaled-" + scale), h.Client.WebService.Status.LotteryAddress, w, ht);
            await ReloadAtCoreTimeAsync(page, scaled);
            await scaled.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
            await FreezePinnedAsync(scaled, 3000);
            await AssertPinnedLayoutAsync(scaled, w, ht);
            await scaled.ScreenshotAsync($"{width}-pinned-scale-{scale}");
        }
        await ArrivePinnedAsync(page, (Grade.Third, "第六件", null));
        await browser.WaitAsync("document.getElementById('pinnedPageNumber').textContent === '1 / 2'");
        var fullHeight = (await browser.EvaluateAsync("document.getElementById('pinned').offsetHeight")).GetDouble();
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 8000 });
        await browser.WaitAsync("document.getElementById('pinnedPageNumber').textContent === '2 / 2'", 12);
        await FreezePinnedAsync(browser, 2000);
        Assert.Equal(fullHeight, (await browser.EvaluateAsync("document.getElementById('pinned').offsetHeight")).GetDouble());
        await browser.ScreenshotAsync($"{width}-pinned-page-2");
        var pool = Assert.IsType<PoolLottery>(lottery.State.Lottery);
        lottery.SetDrawHiddenFromStream(pool.Draws[0].Id, true);
        await browser.WaitAsync("document.querySelector('.pinnedRow.hiding')");
        await FreezePinnedAsync(browser, 600);
        await browser.ScreenshotAsync($"{width}-pinned-hide");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1400 });
        await browser.WaitAsync("document.getElementById('pinnedPageNumber').textContent === '' && !document.querySelector('.hiding')");
        Assert.Equal(theme == LotteryTheme.MechaLock ? "5 次" : theme == LotteryTheme.CapsuleGacha ? "共 5 颗" : "共 5 次", (await browser.EvaluateAsync("document.getElementById('pinnedCount').textContent")).GetString());
        await FreezePinnedAsync(browser, 2000);
        await browser.ScreenshotAsync($"{width}-pinned-filled");
        lottery.HideAllDrawsFromStream();
        await browser.WaitAsync("document.querySelector('.hiding')");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1400 });
        await browser.WaitAsync("document.getElementById('scene').hidden");
        Assert.Empty(browser.Errors);
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, 1920, 1080, Grade.Grand)]
    [InlineData(LotteryTheme.MechaLock, 1920, 1080, Grade.Grand)]
    [InlineData(LotteryTheme.CapsuleGacha, 1920, 1080, Grade.Grand)]
    [InlineData(LotteryTheme.StarSummon, 1920, 1080, Grade.Second)]
    [InlineData(LotteryTheme.MechaLock, 1920, 1080, Grade.Second)]
    [InlineData(LotteryTheme.CapsuleGacha, 1920, 1080, Grade.Second)]
    [InlineData(LotteryTheme.StarSummon, 1920, 1080, Grade.Third)]
    [InlineData(LotteryTheme.MechaLock, 1920, 1080, Grade.Third)]
    [InlineData(LotteryTheme.CapsuleGacha, 1920, 1080, Grade.Third)]
    [InlineData(LotteryTheme.StarSummon, 1080, 1920, Grade.Grand)]
    [InlineData(LotteryTheme.MechaLock, 1080, 1920, Grade.Grand)]
    [InlineData(LotteryTheme.CapsuleGacha, 1080, 1920, Grade.Grand)]
    [InlineData(LotteryTheme.StarSummon, 1080, 1920, Grade.Second)]
    [InlineData(LotteryTheme.MechaLock, 1080, 1920, Grade.Second)]
    [InlineData(LotteryTheme.CapsuleGacha, 1080, 1920, Grade.Second)]
    [InlineData(LotteryTheme.StarSummon, 1080, 1920, Grade.Third)]
    [InlineData(LotteryTheme.MechaLock, 1080, 1920, Grade.Third)]
    [InlineData(LotteryTheme.CapsuleGacha, 1080, 1920, Grade.Third)]
    public async Task Pinned_pool_entrance_only_follows_this_pages_completed_draw(LotteryTheme theme, int width, int height, Grade grade)
    {
        await using var page = await OpenPageAsync(width, height);
        var (h, browser) = (page.H, page.Browser);
        var lottery = h.Client.Lottery;
        lottery.SelectTheme(theme);
        await SeedPinnedPoolAsync(page, PinnedPool[..^1]);
        lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        await ArrivePinnedAsync(page, (grade, grade == Grade.Grand ? "机械键盘" : grade == Grade.Second ? "周边抱枕" : "签名照", "柚子不酸"));
        await FreezePinnedAsync(browser, 960);
        Assert.Single((await browser.EvaluateAsync("Array.from(document.querySelectorAll('.fresh'), e => e.className)")).EnumerateArray());
        Assert.False((await browser.EvaluateAsync("document.querySelector('.fresh').classList.contains('ambient')")).GetBoolean());
        foreach (var milliseconds in new[] { 960, 1920, 5400 })
        {
            await FreezePinnedAsync(browser, milliseconds);
            await browser.ScreenshotAsync($"{width}-pinned-arrive-{grade}-{milliseconds}");
        }
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 6100 });
        await browser.WaitAsync("!document.getElementById('pinned').classList.contains('arriving')");
        Assert.Equal(grade == Grade.Grand, (await browser.EvaluateAsync("document.querySelector('.pinnedRow').classList.contains('ambient')")).GetBoolean());
        Assert.Empty(browser.Errors);
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, 1920, 1080)]
    [InlineData(LotteryTheme.StarSummon, 1080, 1920)]
    [InlineData(LotteryTheme.MechaLock, 1920, 1080)]
    [InlineData(LotteryTheme.MechaLock, 1080, 1920)]
    [InlineData(LotteryTheme.CapsuleGacha, 1920, 1080)]
    [InlineData(LotteryTheme.CapsuleGacha, 1080, 1920)]
    public async Task Pinned_entry_groups_results_only_after_closing_the_full_overlay(LotteryTheme theme, int width, int height)
    {
        await using var page = await OpenPageAsync(width, height);
        var (h, browser) = (page.H, page.Browser);
        var lottery = h.Client.Lottery;
        lottery.SelectTheme(theme);
        lottery.SetPinnedResults(true);
        await SeedPinnedEntryAsync(page, PinnedEntryNames(theme));
        await ReloadAtCoreTimeAsync(page);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'winners'");
        lottery.SetEntryHidden(true);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        await FreezePinnedAsync(browser, 3000);
        Assert.Equal(3, (await browser.EvaluateAsync("document.querySelectorAll('.pinnedGroup').length")).GetInt32());
        Assert.Equal(6, (await browser.EvaluateAsync("document.querySelectorAll('.pinnedName').length")).GetInt32());
        Assert.False((await browser.EvaluateAsync("document.getElementById('pinned').classList.contains('arriving')")).GetBoolean());
        await AssertPinnedLayoutAsync(browser, width, height);
        await browser.ScreenshotAsync($"{width}-pinned-entry");
        var draw = Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws[0];
        lottery.SetDrawHiddenFromStream(draw.Id, true);
        await browser.WaitAsync("document.querySelector('.pinnedGroup.hiding')");
        await FreezePinnedAsync(browser, 600);
        await browser.ScreenshotAsync($"{width}-pinned-entry-hide");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1400 });
        await browser.WaitAsync("document.querySelectorAll('.pinnedGroup').length === 2");
        await AssertPinnedLayoutAsync(browser, width, height);
        lottery.SetEntryHidden(false);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'winners'");
        Assert.Empty(browser.Errors);
    }

    // Seven names page Star Summon's grouped board as NG4 does; the other themes' ND boards fit six.
    private static string[] PinnedEntryNames(LotteryTheme theme) =>
        Enumerable.Range(1, theme == LotteryTheme.StarSummon ? 7 : 6).Select(i => $"观众{i}星河入梦").ToArray();

    private static async Task SeedPinnedPoolAsync(PageSession page, IEnumerable<(Grade Grade, string Prize, string? Who)> rows)
    {
        foreach (var row in rows)
        {
            await DrawPinnedAsync(page, row);
            page.H.Time.Advance(LotteryController.DrawDuration);
        }
    }

    // Plays one real reveal in the page and lets Core finish it after the page's own deadline.
    private static async Task ArrivePinnedAsync(PageSession page, (Grade Grade, string Prize, string? Who) row)
    {
        await DrawPinnedAsync(page, row);
        await page.Browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 7000 });
        await page.Browser.WaitAsync("document.getElementById('pinned').classList.contains('arriving')", 12);
        page.H.Time.Advance(LotteryController.DrawDuration);
    }

    private static async Task DrawPinnedAsync(PageSession page, (Grade Grade, string Prize, string? Who) row)
    {
        page.H.Client.Lottery.Start([new(row.Prize, 1, row.Grade)], "停", row.Who is null ? null : 42);
        if (row.Who is not null) await page.H.PushAndCollectAsync(page.Watch, Message(42, row.Who));
        page.H.Client.Lottery.Stop();
    }

    private static async Task SeedPinnedEntryAsync(PageSession page, string[] names)
    {
        var lottery = page.H.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry([new("一等奖", "机械键盘", 1, Grade.Grand), new("二等奖", "周边抱枕", 2, Grade.Second), new("三等奖", "签名照", names.Length - 3, Grade.Third)], "抽奖");
        await page.H.PushAndCollectAsync(page.Watch, names.Select((name, index) => Message(index + 1, name)).ToArray());
        lottery.CloseEntries();
        for (var index = 0; index < names.Length; index++)
        {
            lottery.DrawEntry(index == 0 ? 0 : index < 3 ? 1 : 2);
            page.H.Time.Advance(LotteryController.DrawDuration);
        }
        lottery.EndEntry();
    }

    private static async Task ReloadAtCoreTimeAsync(PageSession page, ChromeBrowser? browser = null)
    {
        browser ??= page.Browser;
        await browser.CommandAsync("Page.addScriptToEvaluateOnNewDocument", new { source = $"{{ const start = performance.now(); Date.now = () => {page.H.Time.GetUtcNow().ToUnixTimeMilliseconds()} + performance.now() - start; }}" });
        await browser.CommandAsync("Page.reload");
        // The initial empty page reports fonts loaded before SSE creates text and requests its font.
        await browser.WaitAsync("document.readyState === 'complete' && document.getElementById('scene').dataset.view");
        await browser.EvaluateAsync("(async()=>{await document.fonts.ready; await new Promise(resolve=>requestAnimationFrame(resolve)); return true;})()");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
    }

    private static async Task FreezePinnedAsync(ChromeBrowser browser, int milliseconds)
    {
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync($"document.body.style.background = '#f0eae4'; document.getAnimations().forEach(a => {{ a.pause(); a.currentTime = {milliseconds}; }}); true");
    }

    private static async Task AssertPinnedLayoutAsync(ChromeBrowser browser, int width, int height)
    {
        var portrait = width < height; var scale = Math.Min(width / (portrait ? 1080d : 1920), height / (portrait ? 1920d : 1080));
        var bounds = await browser.EvaluateAsync("(() => {const b=document.getElementById('pinned').getBoundingClientRect(); return {x:b.x,y:b.y,w:b.width,h:b.height, overflow:Array.from(document.querySelectorAll('.pinnedRow,.pinnedName'),e=>e.getBoundingClientRect()).some(r=>r.left<b.left-1||r.right>b.right+1)};})()");
        Assert.False(bounds.GetProperty("overflow").GetBoolean(), bounds.GetRawText());
        Assert.Equal((portrait ? 800 : 620) * scale, bounds.GetProperty("w").GetDouble(), 1);
        if (portrait) Assert.Equal(height / 2d, bounds.GetProperty("y").GetDouble() + bounds.GetProperty("h").GetDouble() / 2, 1);
        else { Assert.Equal(1240 * scale, bounds.GetProperty("x").GetDouble(), 1); Assert.Equal(60 * scale, bounds.GetProperty("y").GetDouble(), 1); }
    }
}
