using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
    [Theory]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon")]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha")]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha")]
    public async Task Pinned_reference_pool_hide_matches_the_original_collapse_and_fill(LotteryTheme theme, string board)
    {
        var root = PinnedReferenceRoot();
        await using var page = await OpenPageAsync(1920, 1080);
        await using var reference = await OpenPinnedReferenceAsync(page, root, board, 1920, 1080);
        page.H.Client.Lottery.SelectTheme(theme);
        await SeedReferencePoolAsync(page, reference);
        page.H.Client.Lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.querySelectorAll('#pinnedRows>.rb-row').length === 5");
        var pool = Assert.IsType<PoolLottery>(page.H.Client.Lottery.State.Lottery);
        page.H.Client.Lottery.SetDrawHiddenFromStream(pool.Draws[^3].Id, true);
        await page.Browser.WaitAsync("document.querySelector('.rb-row.is-hiding')");
        await MatchReferenceCountAsync(page.Browser, reference);
        var failures = new List<string>();
        foreach (var elapsed in new[] { 0, 300, 600, 900, 1200 })
        {
            await SetReferenceHideTimeAsync(page.Browser, reference, elapsed);
            await CompareReferenceFrameAsync(page.Browser, reference, board, elapsed, failures);
        }
        await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1321 });
        await page.Browser.WaitAsync("!document.querySelector('.is-hiding') && document.querySelector('.is-fill')");
        foreach (var elapsed in new[] { 1500, 1800, 3600 })
        {
            await SetReferenceHideTimeAsync(page.Browser, reference, elapsed);
            await CompareReferenceFrameAsync(page.Browser, reference, board, elapsed, failures);
        }
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public async Task Pinned_reference_entry_hide_matches_the_original_name_collapse()
    {
        var root = PinnedReferenceRoot(); const string board = "NH4-HideEntrySummon";
        await using var page = await OpenPageAsync(1920, 1080);
        await using var reference = await OpenPinnedReferenceAsync(page, root, board, 1920, 1080);
        var lottery = page.H.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry([new("一等奖", "机械键盘", 1, Grade.Grand), new("二等奖", "周边抱枕", 2, Grade.Second), new("三等奖", "签名照", 3, Grade.Third)], "抽奖");
        await page.H.PushAndCollectAsync(page.Watch, new[] { "柚子不酸", "一只小鹿", "晚风", "阿狸", "Momo", "小鱼干" }.Select((name, index) => Message(index + 1, name)).ToArray());
        lottery.CloseEntries();
        for (var index = 0; index < 6; index++)
        {
            lottery.DrawEntry(index == 0 ? 0 : index < 3 ? 1 : 2);
            page.H.Time.Advance(LotteryController.DrawDuration);
        }
        lottery.EndEntry(); lottery.SetEntryHidden(true); lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.querySelectorAll('#pinnedRows .rb-name').length === 6");
        // Draws are random. Only demo names change; original groups, chips and layout remain intact.
        var names = await page.Browser.EvaluateAsync("Array.from(document.querySelectorAll('#pinnedRows .rb-name'),e=>e.textContent)");
        await reference.EvaluateAsync("document.querySelectorAll('.rb-name').forEach((e,i)=>e.textContent=" + names.GetRawText() + "[i]); true");
        var entry = Assert.IsType<EntryLottery>(lottery.State.Lottery);
        lottery.SetDrawHiddenFromStream(entry.Draws[2].Id, true);
        await page.Browser.WaitAsync("document.querySelector('.rb-name.is-hiding')");
        await MatchReferenceCountAsync(page.Browser, reference);
        var failures = new List<string>();
        foreach (var elapsed in new[] { 0, 300, 600, 900, 1200 })
        {
            await SetReferenceHideTimeAsync(page.Browser, reference, elapsed);
            await CompareReferenceFrameAsync(page.Browser, reference, board, elapsed, failures);
        }
        await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1321 });
        await page.Browser.WaitAsync("document.querySelectorAll('#pinnedRows .rb-name').length === 5");
        await SetReferenceHideTimeAsync(page.Browser, reference, 3600);
        await CompareReferenceFrameAsync(page.Browser, reference, board, 3600, failures);
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, "NG1-PageSummon")]
    [InlineData(LotteryTheme.MechaLock, "NG2-PageMecha")]
    [InlineData(LotteryTheme.CapsuleGacha, "NG3-PageGacha")]
    public async Task Pinned_reference_pages_match_both_original_pages_and_keep_full_height(LotteryTheme theme, string board)
    {
        var root = PinnedReferenceRoot();
        await using var page = await OpenPageAsync(1920, 1080);
        await using var reference = await OpenPinnedReferenceAsync(page, root, board, 1920, 1080);
        page.H.Client.Lottery.SelectTheme(theme);
        await SeedReferencePoolAsync(page, reference);
        page.H.Client.Lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.getElementById('pinnedPageNumber').textContent === '1 / 2'");
        await MatchReferenceCountAsync(page.Browser, reference);
        var failures = new List<string>();
        await FreezePinnedAsync(page.Browser, 3000);
        await FreezePinnedAsync(reference, 3000);
        await CompareReferenceFrameAsync(page.Browser, reference, board, 3000, failures, includePages: true);
        await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 8000 });
        await page.Browser.WaitAsync("document.getElementById('pinnedPageNumber').textContent === '2 / 2'");
        await FreezePinnedAsync(page.Browser, 3000);
        await FreezePinnedAsync(reference, 11000);
        // Both original pages stay in the CSS grid: the hidden first page still reserves full height.
        await CompareReferenceFrameAsync(page.Browser, reference, board, 11000, failures, includePages: true);
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, "NS1-ScaleLandscape50", 1920, 1080, .5)]
    [InlineData(LotteryTheme.MechaLock, "NS2-ScaleLandscapeMecha50", 1920, 1080, .5)]
    [InlineData(LotteryTheme.CapsuleGacha, "NS3-ScaleLandscapeGacha50", 1920, 1080, .5)]
    [InlineData(LotteryTheme.StarSummon, "NS4-ScalePortrait55", 1080, 1920, .55)]
    public async Task Pinned_reference_scaled_sources_match_the_original_transformed_wrapper(LotteryTheme theme, string board, int width, int height, double scale)
    {
        var root = PinnedReferenceRoot();
        await using var page = await OpenPageAsync((int)(width * scale), (int)(height * scale));
        await using var reference = await OpenPinnedReferenceAsync(page, root, board, width, height);
        page.H.Client.Lottery.SelectTheme(theme);
        await SeedReferencePoolAsync(page, reference);
        page.H.Client.Lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        await MatchReferenceCountAsync(page.Browser, reference);
        await FreezePinnedAsync(page.Browser, 3000);
        await FreezePinnedAsync(reference, 3000);
        var failures = new List<string>();
        await CompareReferenceFrameAsync(page.Browser, reference, board, 3000, failures, includePages: true);
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    private static string PinnedReferenceRoot()
    {
        var root = Environment.GetEnvironmentVariable("AETHER_TEST_DESIGN_REFERENCE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(root), "Set AETHER_TEST_DESIGN_REFERENCE to the locally rendered original boards.");
        return root!;
    }

    private static async Task<ChromeBrowser> OpenPinnedReferenceAsync(PageSession page, string root, string board, int width, int height)
    {
        var reference = new ChromeBrowser();
        try
        {
            await reference.OpenAsync(Path.Combine(page.H.DataDirectory, "reference-" + Guid.NewGuid()), root.TrimEnd('/') + "/" + board + ".static.html", width, height);
            // Retain all layout ancestors and the original scaled source wrapper. Hide only the room and source-frame decorations.
            await reference.EvaluateAsync("""
                window.referenceSource = document.querySelector('.rb').parentElement;
                for (let e = referenceSource; e.parentElement; e = e.parentElement) {
                  for (const sibling of e.parentElement.children) if (sibling !== e) sibling.style.visibility = 'hidden';
                  e.parentElement.style.background = 'transparent';
                  if (e.parentElement === document.body) break;
                }
                document.body.style.background = referenceSource.style.background = '#f0eae4'; true
                """);
            return reference;
        }
        catch { await reference.DisposeAsync(); throw; }
    }

    private static async Task SeedReferencePoolAsync(PageSession page, ChromeBrowser reference)
    {
        var rows = await reference.EvaluateAsync("Array.from(document.querySelectorAll('.rb-row'), e=>({grade:['Grand','Second','Third'].find(g=>e.classList.contains('g-'+g)),prize:e.querySelector('.rb-prize').textContent,who:e.querySelector('.rb-who').textContent}))");
        foreach (var row in rows.EnumerateArray().Reverse())
        {
            var who = row.GetProperty("who").GetString()!;
            page.H.Client.Lottery.Start([new(row.GetProperty("prize").GetString()!, 1, Enum.Parse<Grade>(row.GetProperty("grade").GetString()!))], "停", who == "主播点停" ? null : 42);
            if (who != "主播点停") await page.H.PushAndCollectAsync(page.Watch, Message(42, who));
            page.H.Client.Lottery.Stop(); page.H.Time.Advance(LotteryController.DrawDuration);
        }
    }

    private static async Task MatchReferenceCountAsync(ChromeBrowser browser, ChromeBrowser reference)
    {
        var count = await browser.EvaluateAsync("document.getElementById('pinnedCount').textContent");
        await reference.EvaluateAsync("document.querySelector('.rb-count').textContent=" + count.GetRawText() + "; true");
    }

    private static async Task SetReferenceHideTimeAsync(ChromeBrowser browser, ChromeBrowser reference, int elapsed)
    {
        await FreezePinnedAsync(reference, elapsed + 1440);
        await FreezePinnedAsync(browser, elapsed + 1440);
        // Ambient effects use the same source clock; operation animations start at the actual hide click.
        await browser.EvaluateAsync($"document.getAnimations().forEach(a=>{{if(a.animationName === 'rbHide' || a.animationName === 'rbHideName') a.currentTime={elapsed}; if(a.animationName === 'rbFill' || a.animationName === 'rbEnter') a.currentTime={Math.Max(0, elapsed - 1320)};}}); true");
    }

    private static async Task CompareReferenceFrameAsync(ChromeBrowser browser, ChromeBrowser reference, string board, int milliseconds, List<string> failures, bool includePages = false)
    {
        var source = await reference.EvaluateAsync("(() => { const b=referenceSource.getBoundingClientRect(); return {x:b.x,y:b.y,width:b.width,height:b.height}; })()");
        var clip = new { x = source.GetProperty("x").GetDouble(), y = source.GetProperty("y").GetDouble(), width = source.GetProperty("width").GetDouble(), height = source.GetProperty("height").GetDouble(), scale = 1 };
        var selectors = ".rb-head,.rb-title,.rb-count,.rb-row,.rb-badge,.rb-prize,.rb-who,.rb-name" + (includePages ? ",.rb-pages" : "");
        string Measure(double x, double y) => """
            (() => Array.from(document.querySelectorAll(
            """ + JsonSerializer.Serialize(selectors) + """
            )).filter(e => {
              for(let p=e;p;p=p.parentElement) { const s=getComputedStyle(p); if(+s.opacity<.01 || s.visibility==='hidden' || s.display==='none') return false; }
              return true;
            }).map(e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e); return {text:e.matches('.rb-head,.rb-row,.rb-pages')?'':e.textContent,x:r.x-
            """ + JsonSerializer.Serialize(x) + ",y:r.y-" + JsonSerializer.Serialize(y) + ",w:r.width,h:r.height,font:s.fontSize};}))()";
        var expected = await reference.EvaluateAsync(Measure(clip.x, clip.y));
        var actual = await browser.EvaluateAsync(Measure(0, 0));
        var differences = new List<string>();
        if (expected.GetArrayLength() != actual.GetArrayLength()) differences.Add($"element count {expected.GetArrayLength()} != {actual.GetArrayLength()}");
        else for (var index = 0; index < expected.GetArrayLength(); index++)
        {
            var a = expected[index]; var b = actual[index];
            foreach (var property in new[] { "text", "font" })
                if (a.GetProperty(property).GetString() != b.GetProperty(property).GetString()) differences.Add($"#{index} {property}: {a.GetProperty(property)} != {b.GetProperty(property)}");
            foreach (var dimension in new[] { "x", "y", "w", "h" })
                if (Math.Abs(a.GetProperty(dimension).GetDouble() - b.GetProperty(dimension).GetDouble()) > 1)
                    differences.Add($"#{index} {a.GetProperty("text")} {dimension}: {a.GetProperty(dimension)} != {b.GetProperty(dimension)}");
        }
        var referencePng = await reference.CommandAsync("Page.captureScreenshot", new { format = "png", clip });
        var productionPng = await browser.CommandAsync("Page.captureScreenshot", new { format = "png" });
        var images = new[] { referencePng.GetProperty("data").GetString(), productionPng.GetProperty("data").GetString() };
        var pixels = await browser.EvaluateAsync("""
            (async () => {
              const frames=[];
              for (const png of
            """ + JsonSerializer.Serialize(images) + """
              ) {
                const image=new Image(); image.src='data:image/png;base64,'+png; await image.decode();
                const canvas=document.createElement('canvas'); canvas.width=image.width; canvas.height=image.height;
                const ctx=canvas.getContext('2d'); ctx.drawImage(image,0,0); frames.push({width:image.width,height:image.height,data:ctx.getImageData(0,0,image.width,image.height).data});
              }
              if(frames[0].width!==frames[1].width || frames[0].height!==frames[1].height) return {sizeMismatch:true};
              const [a,b]=frames.map(f=>f.data), sum=[0,0,0],max=[0,0,0],paintedSum=[0,0,0],background=[240,234,228]; let changed=0,painted=0;
              for(let i=0;i<a.length;i+=4) {
                const ink=background.some((v,c)=>Math.abs(a[i+c]-v)>3 || Math.abs(b[i+c]-v)>3); if(ink) painted++;
                let differs=false; for(let c=0;c<3;c++){const d=Math.abs(a[i+c]-b[i+c]);sum[c]+=d;max[c]=Math.max(max[c],d);if(ink)paintedSum[c]+=d;if(d)differs=true;} if(differs)changed++;
              }
              return {width:frames[0].width,height:frames[0].height,changed,painted,mean:sum.map(v=>v/(a.length/4)),paintedMean:paintedSum.map(v=>v/Math.max(1,painted)),max};
            })()
            """);
        if (pixels.TryGetProperty("sizeMismatch", out _)) differences.Add("screenshot sizes differ");
        if (Environment.GetEnvironmentVariable("AETHER_TEST_SCREENSHOTS") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            var prefix = Path.Combine(output, $"reference-validation-{board}-{milliseconds}");
            await File.WriteAllBytesAsync(prefix + "-expected.png", Convert.FromBase64String(images[0]!), TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(prefix + "-actual.png", Convert.FromBase64String(images[1]!), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(prefix + ".json", JsonSerializer.Serialize(new { board, milliseconds, clip, expected, actual, pixels, differences }), TestContext.Current.CancellationToken);
        }
        failures.AddRange(differences.Select(difference => $"{board}@{milliseconds}ms: {difference}"));
    }
}
