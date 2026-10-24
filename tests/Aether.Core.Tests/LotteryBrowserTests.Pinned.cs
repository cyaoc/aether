using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
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
        await SeedPinnedPoolAsync(page);
        lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        await FreezePinnedAsync(browser, 3000);
        Assert.Equal(theme == LotteryTheme.MechaLock ? "5 次" : theme == LotteryTheme.CapsuleGacha ? "共 5 颗" : "共 5 次", (await browser.EvaluateAsync("document.getElementById('pinnedCount').textContent")).GetString());
        Assert.Equal("柚子不酸", (await browser.EvaluateAsync("document.querySelector('.rb-who').textContent")).GetString());
        Assert.False((await browser.EvaluateAsync("document.getElementById('pinned').classList.contains('st-arrive')")).GetBoolean());
        await AssertPinnedLayoutAsync(browser, width, height);
        await browser.ScreenshotAsync($"{width}-pinned-static");
        await ComparePinnedReferenceAsync(page, PinnedReference(theme, width == 1920 ? "RB" : "NP"), width, height, 3000);
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
        lottery.Start([new("第六件", 1, Grade.Third)], "停"); lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        h.Time.Advance(LotteryController.DrawDuration);
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 7000 });
        await browser.WaitAsync("document.getElementById('pinnedPageNumber').textContent === '1 / 2'");
        var fullHeight = (await browser.EvaluateAsync("document.getElementById('pinned').offsetHeight")).GetDouble();
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 8000 });
        await browser.WaitAsync("document.getElementById('pinnedPageNumber').textContent === '2 / 2'", 12);
        await FreezePinnedAsync(browser, 2000);
        Assert.Equal(fullHeight, (await browser.EvaluateAsync("document.getElementById('pinned').offsetHeight")).GetDouble());
        await browser.ScreenshotAsync($"{width}-pinned-page-2");
        var pool = Assert.IsType<PoolLottery>(lottery.State.Lottery);
        lottery.SetDrawHiddenFromStream(pool.Draws[0].Id, true);
        await browser.WaitAsync("document.querySelector('.rb-row.is-hiding')");
        await FreezePinnedAsync(browser, 600);
        await browser.ScreenshotAsync($"{width}-pinned-hide");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1400 });
        await browser.WaitAsync("document.getElementById('pinnedPageNumber').textContent === '' && !document.querySelector('.is-hiding')");
        Assert.Equal(theme == LotteryTheme.MechaLock ? "5 次" : theme == LotteryTheme.CapsuleGacha ? "共 5 颗" : "共 5 次", (await browser.EvaluateAsync("document.getElementById('pinnedCount').textContent")).GetString());
        await FreezePinnedAsync(browser, 2000);
        await browser.ScreenshotAsync($"{width}-pinned-filled");
        lottery.HideAllDrawsFromStream();
        await browser.WaitAsync("document.querySelector('.is-hiding')");
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
        await SeedPinnedPoolAsync(page, includeNewest: false);
        lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        lottery.Start([new(grade == Grade.Grand ? "机械键盘" : grade == Grade.Second ? "周边抱枕" : "签名照", 1, grade)], "停", 42);
        await h.PushAndCollectAsync(page.Watch, Message(42, "柚子不酸"));
        lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 7000 });
        await browser.WaitAsync("document.getElementById('pinned').classList.contains('st-arrive')", 12);
        h.Time.Advance(LotteryController.DrawDuration);
        await FreezePinnedAsync(browser, 960);
        Assert.Single((await browser.EvaluateAsync("Array.from(document.querySelectorAll('.is-new'), e => e.className)")).EnumerateArray());
        Assert.False((await browser.EvaluateAsync("document.querySelector('.is-new').classList.contains('amb')")).GetBoolean());
        foreach (var milliseconds in new[] { 960, 1920, 5400 })
        {
            await FreezePinnedAsync(browser, milliseconds);
            await browser.ScreenshotAsync($"{width}-pinned-arrive-{grade}-{milliseconds}");
            if (width == 1920)
                await ComparePinnedReferenceAsync(page, PinnedReference(theme, grade == Grade.Grand ? "NA" : grade == Grade.Second ? "NB" : "NC"), width, height, milliseconds);
        }
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 6100 });
        await browser.WaitAsync("!document.getElementById('pinned').classList.contains('st-arrive')");
        Assert.Equal(grade == Grade.Grand, (await browser.EvaluateAsync("document.querySelector('.rb-row').classList.contains('amb')")).GetBoolean());
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
        lottery.SetPinnedResults(true); lottery.New(LotteryMode.Entry);
        var total = theme == LotteryTheme.StarSummon ? 7 : 6;
        lottery.StartEntry([new("一等奖", "机械键盘", 1, Grade.Grand), new("二等奖", "周边抱枕", 2, Grade.Second), new("三等奖", "签名照", total - 3, Grade.Third)], "抽奖");
        await h.PushAndCollectAsync(page.Watch, Enumerable.Range(1, total).Select(i => Message(i, $"观众{i}星河入梦")).ToArray());
        lottery.CloseEntries();
        for (var i = 0; i < total; i++) { lottery.DrawEntry(i == 0 ? 0 : i < 3 ? 1 : 2); h.Time.Advance(LotteryController.DrawDuration); }
        lottery.EndEntry();
        await ReloadAtCoreTimeAsync(page);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'winners'");
        lottery.SetEntryHidden(true);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        await FreezePinnedAsync(browser, 3000);
        Assert.Equal(3, (await browser.EvaluateAsync("document.querySelectorAll('.rb-group').length")).GetInt32());
        Assert.Equal(6, (await browser.EvaluateAsync("document.querySelectorAll('.rb-name').length")).GetInt32());
        Assert.False((await browser.EvaluateAsync("document.getElementById('pinned').classList.contains('st-arrive')")).GetBoolean());
        await AssertPinnedLayoutAsync(browser, width, height);
        await browser.ScreenshotAsync($"{width}-pinned-entry");
        await ComparePinnedReferenceAsync(page, theme == LotteryTheme.StarSummon ? "NG4-PageEntrySummon" : PinnedReference(theme, "ND"), width, height, 3000, matchEntryNames: true);
        var draw = Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws[0];
        lottery.SetDrawHiddenFromStream(draw.Id, true);
        await browser.WaitAsync("document.querySelector('.rb-group.is-hiding')");
        await FreezePinnedAsync(browser, 600);
        await browser.ScreenshotAsync($"{width}-pinned-entry-hide");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1400 });
        await browser.WaitAsync("document.querySelectorAll('.rb-group').length === 2");
        await AssertPinnedLayoutAsync(browser, width, height);
        lottery.SetEntryHidden(false);
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'winners'");
        Assert.Empty(browser.Errors);
    }

    private static string PinnedReference(LotteryTheme theme, string scene)
    {
        var (number, name) = theme switch
        {
            LotteryTheme.MechaLock => (2, "Mecha"),
            LotteryTheme.CapsuleGacha => (3, "Gacha"),
            _ => (1, "Summon"),
        };
        return scene switch
        {
            "RB" => number == 1 ? "RB1-PinBoard" : $"RB{number}-Pin{name}",
            "NP" => $"NP{number}-PinPortrait{name}",
            "ND" => $"ND{number}-PinEntry{name}",
            "NA" => $"NA{number}-Arrive{name}Big",
            "NB" => $"NB{number}-Arrive{name}Second",
            _ => $"NC{number}-Arrive{name}Third",
        };
    }

    private static async Task SeedPinnedPoolAsync(PageSession page, bool includeNewest = true)
    {
        (Grade Grade, string Prize, string? Who)[] results = [(Grade.Third,"签名照","阿狸"), (Grade.Grand,"机械键盘","晚风"), (Grade.Second,"周边抱枕",null), (Grade.Third,"贴纸套装","一只小鹿"), (Grade.Grand,"机械键盘","柚子不酸")];
        foreach (var (grade, prize, who) in results.Take(includeNewest ? 5 : 4))
        {
            page.H.Client.Lottery.Start([new(prize, 1, grade)], "停", who is null ? null : 42);
            if (who is not null) await page.H.PushAndCollectAsync(page.Watch, Message(42, who));
            page.H.Client.Lottery.Stop(); page.H.Time.Advance(LotteryController.DrawDuration);
        }
    }

    private static async Task ReloadAtCoreTimeAsync(PageSession page, ChromeBrowser? browser = null, bool pause = true)
    {
        browser ??= page.Browser;
        await browser.CommandAsync("Page.addScriptToEvaluateOnNewDocument", new { source = $"{{ const start = performance.now(); Date.now = () => {page.H.Time.GetUtcNow().ToUnixTimeMilliseconds()} + performance.now() - start; }}" });
        await browser.CommandAsync("Page.reload");
        // The initial empty page reports fonts loaded before SSE creates text and requests its font.
        await browser.WaitAsync("document.readyState === 'complete' && document.getElementById('scene').dataset.view");
        await browser.EvaluateAsync("(async()=>{await document.fonts.ready; await new Promise(resolve=>requestAnimationFrame(resolve)); return true;})()");
        if (pause) await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
    }

    private static async Task FreezePinnedAsync(ChromeBrowser browser, int milliseconds)
    {
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync($"document.body.style.background = '#f0eae4'; document.getAnimations().forEach(a => {{ a.pause(); a.currentTime = {milliseconds}; }}); true");
    }

    private static async Task AssertPinnedLayoutAsync(ChromeBrowser browser, int width, int height)
    {
        var portrait = width < height; var scale = Math.Min(width / (portrait ? 1080d : 1920), height / (portrait ? 1920d : 1080));
        var bounds = await browser.EvaluateAsync("(() => {const b=document.getElementById('pinned').getBoundingClientRect(); return {x:b.x,y:b.y,w:b.width,h:b.height, overflow:Array.from(document.querySelectorAll('.rb-row,.rb-name'),e=>e.getBoundingClientRect()).some(r=>r.left<b.left-1||r.right>b.right+1)};})()");
        Assert.False(bounds.GetProperty("overflow").GetBoolean(), bounds.GetRawText());
        Assert.Equal((portrait ? 800 : 620) * scale, bounds.GetProperty("w").GetDouble(), 1);
        if (portrait) Assert.Equal(height / 2d, bounds.GetProperty("y").GetDouble() + bounds.GetProperty("h").GetDouble() / 2, 1);
        else { Assert.Equal(1240 * scale, bounds.GetProperty("x").GetDouble(), 1); Assert.Equal(60 * scale, bounds.GetProperty("y").GetDouble(), 1); }
    }

    // Optional design evidence: uses the exported original board, local fonts and the same clock/viewport.
    private static async Task ComparePinnedReferenceAsync(PageSession page, string board, int width, int height, int milliseconds, bool matchEntryNames = false)
    {
        if (Environment.GetEnvironmentVariable("AETHER_TEST_DESIGN_REFERENCE") is not { Length: > 0 } root ||
            Environment.GetEnvironmentVariable("AETHER_TEST_SCREENSHOTS") is not { Length: > 0 } output) return;
        await using var reference = new ChromeBrowser();
        await reference.OpenAsync(Path.Combine(page.H.DataDirectory, "reference-" + Guid.NewGuid()), root.TrimEnd('/') + "/" + board + ".static.html", width, height);
        var count = await page.Browser.EvaluateAsync("document.getElementById('pinnedCount').textContent");
        await reference.EvaluateAsync("const originalBoard=document.querySelector('.rb'); document.body.replaceChildren(originalBoard); document.body.style.margin='0'; document.body.style.width=innerWidth+'px'; document.body.style.height=innerHeight+'px'; document.querySelector('.rb-count').textContent=" + count.GetRawText() + "; true");
        if (matchEntryNames)
        {
            // The real draw picks random viewers; compare the source's layout with those same six names.
            var names = await page.Browser.EvaluateAsync("Array.from(document.querySelectorAll('.rb-name'),e=>e.textContent)");
            await reference.EvaluateAsync("Array.from(document.querySelectorAll('.rb-page')).slice(1).forEach(e=>e.remove()); const names=" + names.GetRawText() + "; document.querySelectorAll('.rb-name').forEach((e,i)=>e.textContent=names[i]); originalBoard.classList.toggle('o-port',innerWidth<innerHeight); originalBoard.classList.toggle('o-land',innerWidth>=innerHeight); true");
        }
        await FreezePinnedAsync(reference, milliseconds);
        const string measure = "Array.from(document.querySelectorAll('.rb-head,.rb-row,.rb-title,.rb-count,.rb-badge,.rb-prize,.rb-who,.rb-name'),e=>{const b=e.getBoundingClientRect();return {text:e.textContent,x:b.x,y:b.y,w:b.width,h:b.height,font:getComputedStyle(e).fontSize}})";
        var expected = await reference.EvaluateAsync(measure); var actual = await page.Browser.EvaluateAsync(measure);
        var png = await reference.CommandAsync("Page.captureScreenshot", new { format = "png" });
        Directory.CreateDirectory(output);
        var prefix = Path.Combine(output, $"reference-{board}-{width}-{milliseconds}");
        await File.WriteAllBytesAsync(prefix + ".png", Convert.FromBase64String(png.GetProperty("data").GetString()!), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(prefix + ".json", JsonSerializer.Serialize(new { expected, actual }), TestContext.Current.CancellationToken);
        Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
        for (var i = 0; i < expected.GetArrayLength(); i++)
        {
            Assert.Equal(expected[i].GetProperty("font").GetString(), actual[i].GetProperty("font").GetString());
            foreach (var dimension in new[] { "x", "y", "w", "h" })
                Assert.InRange(Math.Abs(expected[i].GetProperty(dimension).GetDouble() - actual[i].GetProperty(dimension).GetDouble()), 0, 1);
        }
    }
}
