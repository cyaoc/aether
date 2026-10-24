using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
    [Theory]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 0)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 300)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 600)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 900)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 1200)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 1500)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 1800)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 3600)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 0)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 300)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 600)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 900)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 1200)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 1500)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 1800)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 3600)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 0)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 300)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 600)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 900)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 1200)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 1500)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 1800)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 3600)]
    public async Task Pinned_reference_pool_hide_matches_the_original_collapse_and_fill(LotteryTheme theme, string board, int elapsed)
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
        if (elapsed > 1320)
        {
            await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1321 });
            await page.Browser.WaitAsync("!document.querySelector('.is-hiding') && document.querySelector('.is-fill')");
        }
        await SetReferenceHideTimeAsync(page.Browser, reference, elapsed);
        await CompareReferenceFrameAsync(page.Browser, reference, board, elapsed, failures);
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(600)]
    [InlineData(900)]
    [InlineData(1200)]
    [InlineData(3600)]
    public async Task Pinned_reference_entry_hide_matches_the_original_name_collapse(int elapsed)
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
        if (elapsed > 1320)
        {
            await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1321 });
            await page.Browser.WaitAsync("document.querySelectorAll('#pinnedRows .rb-name').length === 5");
        }
        await SetReferenceHideTimeAsync(page.Browser, reference, elapsed);
        await CompareReferenceFrameAsync(page.Browser, reference, board, elapsed, failures);
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, "NG1-PageSummon", 1)]
    [InlineData(LotteryTheme.StarSummon, "NG1-PageSummon", 2)]
    [InlineData(LotteryTheme.MechaLock, "NG2-PageMecha", 1)]
    [InlineData(LotteryTheme.MechaLock, "NG2-PageMecha", 2)]
    [InlineData(LotteryTheme.CapsuleGacha, "NG3-PageGacha", 1)]
    [InlineData(LotteryTheme.CapsuleGacha, "NG3-PageGacha", 2)]
    public async Task Pinned_reference_pages_match_both_original_pages_and_keep_full_height(LotteryTheme theme, string board, int pageNumber)
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
        if (pageNumber == 2)
        {
            await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 8000 });
            await page.Browser.WaitAsync("document.getElementById('pinnedPageNumber').textContent === '2 / 2'");
        }
        await FreezePinnedAsync(page.Browser, 3000);
        await FreezePinnedAsync(reference, pageNumber == 1 ? 3000 : 11000);
        // Both original pages stay in the CSS grid: the hidden first page still reserves full height.
        await CompareReferenceFrameAsync(page.Browser, reference, board, pageNumber == 1 ? 3000 : 11000, failures, includePages: true);
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
            }).map(e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e),gone=document.querySelector('.rb-row.is-gone'); return {prototypeChip:e.matches('.rb-name.gone'),afterGone:!!gone&&!!(gone.compareDocumentPosition(e)&Node.DOCUMENT_POSITION_FOLLOWING),text:e.matches('.rb-head,.rb-row,.rb-pages')?'':e.textContent,x:r.x-
            """ + JsonSerializer.Serialize(x) + ",y:r.y-" + JsonSerializer.Serialize(y) + ",w:r.width,h:r.height,font:s.fontSize};}))()";
        var expected = await reference.EvaluateAsync(Measure(clip.x, clip.y));
        var actual = await browser.EvaluateAsync(Measure(0, 0));
        const string motion = """
            Array.from(document.querySelectorAll('.rb-row.is-gone,.rb-name.gone,.rb-row.is-hiding,.rb-name.is-hiding,.rb-row.is-fill,.rb-row.amb'),e=>{
              const s=getComputedStyle(e),r=e.getBoundingClientRect();
              return {text:e.textContent,classes:e.className,x:r.x,y:r.y,w:r.width,h:r.height,opacity:s.opacity,height:s.height,maxWidth:s.maxWidth,
                padding:s.padding,border:s.borderWidth,margin:s.margin,overflow:s.overflow,transform:s.transform,matrix:Array.from(new DOMMatrix(s.transform).toFloat64Array()),delay:s.getPropertyValue('--d'),
                animations:e.getAnimations({subtree:true}).map(a=>({name:a.animationName,time:a.currentTime,pending:a.pending,state:a.playState,duration:a.effect.getTiming().duration,delay:a.effect.getTiming().delay,opacity:getComputedStyle(a.effect.target).opacity,transform:getComputedStyle(a.effect.target).transform}))};
            })
            """;
        var expectedMotion = await reference.EvaluateAsync(motion);
        var actualMotion = await browser.EvaluateAsync(motion);
        var differences = new List<string>();
        var geometry = new List<(int Index, string Dimension, string Detail)>();
        if (expected.GetArrayLength() != actual.GetArrayLength()) differences.Add($"element count {expected.GetArrayLength()} != {actual.GetArrayLength()}");
        else for (var index = 0; index < expected.GetArrayLength(); index++)
        {
            var a = expected[index]; var b = actual[index];
            foreach (var property in new[] { "text", "font" })
                if (a.GetProperty(property).GetString() != b.GetProperty(property).GetString()) differences.Add($"#{index} {property}: {a.GetProperty(property)} != {b.GetProperty(property)}");
            foreach (var dimension in new[] { "x", "y", "w", "h" })
                if (Math.Abs(a.GetProperty(dimension).GetDouble() - b.GetProperty(dimension).GetDouble()) > 1)
                {
                    var detail = $"#{index} {a.GetProperty("text")} {dimension}: {a.GetProperty(dimension)} != {b.GetProperty(dimension)}";
                    differences.Add(detail); geometry.Add((index, dimension, detail));
                }
        }
        var output = Environment.GetEnvironmentVariable("AETHER_TEST_SCREENSHOTS");
        var prefix = string.IsNullOrWhiteSpace(output) ? null : Path.Combine(output, $"reference-validation-{board}-{milliseconds}");
        if (prefix != null)
        {
            Directory.CreateDirectory(output!);
            await File.WriteAllTextAsync(prefix + ".json", JsonSerializer.Serialize(new { board, milliseconds, clip, expected, actual, expectedMotion, actualMotion, differences }), TestContext.Current.CancellationToken);
        }
        // Each test owns a fresh page and captures it once. Only the scaled source needs a crop.
        var productionPng = await browser.CommandAsync("Page.captureScreenshot", new { format = "png" });
        var referencePng = clip.x == 0 && clip.y == 0
            ? await reference.CommandAsync("Page.captureScreenshot", new { format = "png" })
            : await reference.CommandAsync("Page.captureScreenshot", new { format = "png", clip });
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
        // Reordering keeps Grand effects staggered, but assigns their delay by the current page order.
        // Preserve raw screenshots; compare geometry once more at the equivalent ambient phase.
        var ambient = await browser.EvaluateAsync("""
            Array.from(document.querySelectorAll('.rb-row.amb'),e=>({
              key:e.querySelector('.rb-prize').textContent+'|'+(e.querySelector('.rb-who')?.textContent||''),
              delay:parseFloat(getComputedStyle(e).getPropertyValue('--d')),
              arrival:!!e.closest('.st-arrive')||e.classList.contains('is-new'),
              animations:e.getAnimations({subtree:true}).map(a=>a.animationName)
            }))
            """);
        var phaseExpected = expected;
        if (board.StartsWith("NH", StringComparison.Ordinal) && milliseconds > 1320)
        {
            Assert.All(ambient.EnumerateArray(), row => Assert.False(row.GetProperty("arrival").GetBoolean()));
            phaseExpected = await reference.EvaluateAsync("""
                (()=>{const changed=[]; for(const row of document.querySelectorAll('.rb-row.amb')) {
                  const key=row.querySelector('.rb-prize').textContent+'|'+(row.querySelector('.rb-who')?.textContent||'');
                  const actual=
                """ + ambient.GetRawText() + """
                    .find(r=>r.key===key); if(!actual) continue;
                  const shift=(parseFloat(getComputedStyle(row).getPropertyValue('--d'))-actual.delay)*1000;
                  for(const a of row.getAnimations({subtree:true})) { changed.push([a,a.currentTime]); a.currentTime+=shift; }
                }
                const bounds=
                """ + Measure(clip.x, clip.y) + "; changed.forEach(([a,time])=>a.currentTime=time); return bounds;})()");
        }
        var unexplained = differences.Except(geometry.Select(item => item.Detail)).ToList();
        var explained = new List<string>();
        var sourceGone = expectedMotion.EnumerateArray().FirstOrDefault(row => row.GetProperty("classes").GetString()!.Contains("is-gone", StringComparison.Ordinal));
        var actualGone = actualMotion.EnumerateArray().FirstOrDefault(row => row.GetProperty("classes").GetString()!.Contains("is-hiding", StringComparison.Ordinal));
        var ghost = sourceGone.ValueKind == JsonValueKind.Undefined ? 0 : sourceGone.GetProperty("h").GetDouble() - (actualGone.ValueKind == JsonValueKind.Undefined ? 0 : actualGone.GetProperty("h").GetDouble());
        var ghostLimit = board == "NH1-HideSummon" ? 6 : board == "NH3-HideGacha" ? 8 : 0;
        foreach (var (index, dimension, detail) in geometry)
        {
            var target = phaseExpected[index].GetProperty(dimension).GetDouble();
            var residualBorder = dimension == "y" && expected[index].GetProperty("afterGone").GetBoolean() && ghost > 0 && ghost <= ghostLimit;
            if (residualBorder) target -= ghost;
            if (Math.Abs(target - actual[index].GetProperty(dimension).GetDouble()) <= 1)
                explained.Add(detail + (residualBorder ? " (original hidden row retains its border height)" : " (ambient delay follows reordered page)"));
            else if (board == "NH4-HideEntrySummon" && milliseconds < 600 && expected[index].GetProperty("prototypeChip").GetBoolean() && dimension is "x" or "w")
                explained.Add(detail + " (original chip padding/margin interpolate during the demo pre-roll)");
            else unexplained.Add(detail);
        }
        if (prefix != null)
        {
            await File.WriteAllBytesAsync(prefix + "-expected.png", Convert.FromBase64String(images[0]!), TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(prefix + "-actual.png", Convert.FromBase64String(images[1]!), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(prefix + ".json", JsonSerializer.Serialize(new { board, milliseconds, clip, expected, actual, expectedMotion, actualMotion, ambient, phaseExpected, pixels, differences, explained, unexplained }), TestContext.Current.CancellationToken);
        }
        if (board.StartsWith("NH", StringComparison.Ordinal) && milliseconds <= 1200)
        {
            var sourceTarget = expectedMotion.EnumerateArray().Single(row => row.GetProperty("classes").GetString()!.Contains("gone", StringComparison.Ordinal));
            var actualTarget = actualMotion.EnumerateArray().Single(row => row.GetProperty("classes").GetString()!.Contains("is-hiding", StringComparison.Ordinal));
            foreach (var property in new[] { "opacity", "maxWidth" })
            {
                var a = sourceTarget.GetProperty(property).GetString()!; var b = actualTarget.GetProperty(property).GetString()!;
                if (a == "none") continue;
                Assert.Equal(double.Parse(a.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture), double.Parse(b.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture), 2);
            }
            for (var index = 0; index < 16; index++) Assert.Equal(sourceTarget.GetProperty("matrix")[index].GetDouble(), actualTarget.GetProperty("matrix")[index].GetDouble(), 2);
        }
        if (board.StartsWith("NG", StringComparison.Ordinal) || board.StartsWith("NS", StringComparison.Ordinal))
            Assert.All(pixels.GetProperty("max").EnumerateArray(), channel => Assert.InRange(channel.GetInt32(), 0, 4));
        foreach (var row in actualMotion.EnumerateArray().Where(row => row.GetProperty("classes").GetString()!.Contains("is-fill", StringComparison.Ordinal)))
            Assert.Equal("hidden", row.GetProperty("overflow").GetString());
        failures.AddRange(unexplained.Select(difference => $"{board}@{milliseconds}ms: {difference}"));
    }
}
