using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
    [Theory]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 6, 0)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 6, 300)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 6, 600)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 6, 900)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 6, 1200)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 6, 1500)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 6, 1800)]
    [InlineData(LotteryTheme.StarSummon, "NH1-HideSummon", 6, 3600)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 0, 0)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 0, 300)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 0, 600)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 0, 900)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 0, 1200)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 0, 1500)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 0, 1800)]
    [InlineData(LotteryTheme.MechaLock, "NH2-HideMecha", 0, 3600)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 8, 0)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 8, 300)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 8, 600)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 8, 900)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 8, 1200)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 8, 1500)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 8, 1800)]
    [InlineData(LotteryTheme.CapsuleGacha, "NH3-HideGacha", 8, 3600)]
    // ghostLimit: the original hidden row keeps its top and bottom border height (Star Summon 2×3px, Capsule Gacha 2×4px).
    public async Task Pinned_reference_pool_hide_matches_the_original_collapse_and_fill(LotteryTheme theme, string board, int ghostLimit, int elapsed)
    {
        await using var page = await OpenPageAsync(1920, 1080);
        await using var reference = await OpenPinnedReferenceAsync(page, board, 1920, 1080);
        page.H.Client.Lottery.SelectTheme(theme);
        await SeedPinnedPoolAsync(page, await ReferenceRowsAsync(reference));
        page.H.Client.Lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.querySelectorAll('#pinnedRows>.pinnedRow').length === 5");
        var pool = Assert.IsType<PoolLottery>(page.H.Client.Lottery.State.Lottery);
        page.H.Client.Lottery.SetDrawHiddenFromStream(pool.Draws[^3].Id, true);
        await page.Browser.WaitAsync("document.querySelector('.pinnedRow.hiding')");
        await MatchReferenceCountAsync(page.Browser, reference);
        var failures = new List<string>();
        if (elapsed > 1320)
        {
            await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1321 });
            await page.Browser.WaitAsync("!document.querySelector('.hiding') && document.querySelector('.filling')");
        }
        await SetReferenceHideTimeAsync(page.Browser, reference, elapsed);
        await CompareReferenceFrameAsync(page.Browser, reference, board, elapsed, failures, hiding: true, ghostLimit: ghostLimit);
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
        const string board = "NH4-HideEntrySummon";
        await using var page = await OpenPageAsync(1920, 1080);
        await using var reference = await OpenPinnedReferenceAsync(page, board, 1920, 1080);
        var lottery = page.H.Client.Lottery;
        await SeedPinnedEntryAsync(page, ["柚子不酸", "一只小鹿", "晚风", "阿狸", "Momo", "小鱼干"]);
        lottery.SetEntryHidden(true); lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.querySelectorAll('#pinnedRows .pinnedName').length === 6");
        // Draws are random. Only demo names change; original groups, chips and layout remain intact.
        var names = await page.Browser.EvaluateAsync("Array.from(document.querySelectorAll('#pinnedRows .pinnedName'),e=>e.textContent)");
        await reference.EvaluateAsync("document.querySelectorAll('.rb-name').forEach((e,i)=>e.textContent=" + names.GetRawText() + "[i]); true");
        var entry = Assert.IsType<EntryLottery>(lottery.State.Lottery);
        lottery.SetDrawHiddenFromStream(entry.Draws[2].Id, true);
        await page.Browser.WaitAsync("document.querySelector('.pinnedName.hiding')");
        await MatchReferenceCountAsync(page.Browser, reference);
        var failures = new List<string>();
        if (elapsed > 1320)
        {
            await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance", budget = 1321 });
            await page.Browser.WaitAsync("document.querySelectorAll('#pinnedRows .pinnedName').length === 5");
        }
        await SetReferenceHideTimeAsync(page.Browser, reference, elapsed);
        // Before 600ms the original demo is still interpolating the collapsing chip's padding and margin.
        await CompareReferenceFrameAsync(page.Browser, reference, board, elapsed, failures, hiding: true, chipPreRoll: elapsed < 600);
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
        await using var page = await OpenPageAsync(1920, 1080);
        await using var reference = await OpenPinnedReferenceAsync(page, board, 1920, 1080);
        page.H.Client.Lottery.SelectTheme(theme);
        await SeedPinnedPoolAsync(page, await ReferenceRowsAsync(reference));
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
        await CompareReferenceFrameAsync(page.Browser, reference, board, pageNumber == 1 ? 3000 : 11000, failures, includePages: true, exactPixels: true);
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, "RB1-PinBoard", 1920, 1080, 1, false)]
    [InlineData(LotteryTheme.MechaLock, "RB2-PinMecha", 1920, 1080, 1, false)]
    [InlineData(LotteryTheme.CapsuleGacha, "RB3-PinGacha", 1920, 1080, 1, false)]
    [InlineData(LotteryTheme.StarSummon, "NP1-PinPortraitSummon", 1080, 1920, 1, false)]
    [InlineData(LotteryTheme.MechaLock, "NP2-PinPortraitMecha", 1080, 1920, 1, false)]
    [InlineData(LotteryTheme.CapsuleGacha, "NP3-PinPortraitGacha", 1080, 1920, 1, false)]
    [InlineData(LotteryTheme.StarSummon, "NS1-ScaleLandscape50", 1920, 1080, .5, true)]
    [InlineData(LotteryTheme.MechaLock, "NS2-ScaleLandscapeMecha50", 1920, 1080, .5, true)]
    [InlineData(LotteryTheme.CapsuleGacha, "NS3-ScaleLandscapeGacha50", 1920, 1080, .5, true)]
    [InlineData(LotteryTheme.StarSummon, "NS4-ScalePortrait55", 1080, 1920, .55, true)]
    // exactPixels: the scaled sources (NS) also match pixel for pixel; the unscaled RB/NP boards are compared by layout.
    public async Task Pinned_reference_static_sources_match_the_original_wrapper(LotteryTheme theme, string board, int width, int height, double scale, bool exactPixels)
    {
        await using var page = await OpenPageAsync((int)(width * scale), (int)(height * scale));
        await using var reference = await OpenPinnedReferenceAsync(page, board, width, height);
        page.H.Client.Lottery.SelectTheme(theme);
        await SeedPinnedPoolAsync(page, await ReferenceRowsAsync(reference));
        page.H.Client.Lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        await MatchReferenceCountAsync(page.Browser, reference);
        await FreezePinnedAsync(page.Browser, 3000);
        await FreezePinnedAsync(reference, 3000);
        var failures = new List<string>();
        await CompareReferenceFrameAsync(page.Browser, reference, board, 3000, failures, includePages: true, exactPixels: exactPixels);
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, "NA1-ArriveSummonBig", 960)]
    [InlineData(LotteryTheme.StarSummon, "NA1-ArriveSummonBig", 1920)]
    [InlineData(LotteryTheme.StarSummon, "NA1-ArriveSummonBig", 5400)]
    [InlineData(LotteryTheme.StarSummon, "NB1-ArriveSummonSecond", 960)]
    [InlineData(LotteryTheme.StarSummon, "NB1-ArriveSummonSecond", 1920)]
    [InlineData(LotteryTheme.StarSummon, "NB1-ArriveSummonSecond", 5400)]
    [InlineData(LotteryTheme.StarSummon, "NC1-ArriveSummonThird", 960)]
    [InlineData(LotteryTheme.StarSummon, "NC1-ArriveSummonThird", 1920)]
    [InlineData(LotteryTheme.StarSummon, "NC1-ArriveSummonThird", 5400)]
    [InlineData(LotteryTheme.MechaLock, "NA2-ArriveMechaBig", 960)]
    [InlineData(LotteryTheme.MechaLock, "NA2-ArriveMechaBig", 1920)]
    [InlineData(LotteryTheme.MechaLock, "NA2-ArriveMechaBig", 5400)]
    [InlineData(LotteryTheme.MechaLock, "NB2-ArriveMechaSecond", 960)]
    [InlineData(LotteryTheme.MechaLock, "NB2-ArriveMechaSecond", 1920)]
    [InlineData(LotteryTheme.MechaLock, "NB2-ArriveMechaSecond", 5400)]
    [InlineData(LotteryTheme.MechaLock, "NC2-ArriveMechaThird", 960)]
    [InlineData(LotteryTheme.MechaLock, "NC2-ArriveMechaThird", 1920)]
    [InlineData(LotteryTheme.MechaLock, "NC2-ArriveMechaThird", 5400)]
    [InlineData(LotteryTheme.CapsuleGacha, "NA3-ArriveGachaBig", 960)]
    [InlineData(LotteryTheme.CapsuleGacha, "NA3-ArriveGachaBig", 1920)]
    [InlineData(LotteryTheme.CapsuleGacha, "NA3-ArriveGachaBig", 5400)]
    [InlineData(LotteryTheme.CapsuleGacha, "NB3-ArriveGachaSecond", 960)]
    [InlineData(LotteryTheme.CapsuleGacha, "NB3-ArriveGachaSecond", 1920)]
    [InlineData(LotteryTheme.CapsuleGacha, "NB3-ArriveGachaSecond", 5400)]
    [InlineData(LotteryTheme.CapsuleGacha, "NC3-ArriveGachaThird", 960)]
    [InlineData(LotteryTheme.CapsuleGacha, "NC3-ArriveGachaThird", 1920)]
    [InlineData(LotteryTheme.CapsuleGacha, "NC3-ArriveGachaThird", 5400)]
    public async Task Pinned_reference_arrivals_match_the_original_entrance(LotteryTheme theme, string board, int milliseconds)
    {
        await using var page = await OpenPageAsync(1920, 1080);
        await using var reference = await OpenPinnedReferenceAsync(page, board, 1920, 1080);
        page.H.Client.Lottery.SelectTheme(theme);
        var rows = await ReferenceRowsAsync(reference);
        await SeedPinnedPoolAsync(page, rows[..^1]);
        page.H.Client.Lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        await ArrivePinnedAsync(page, rows[^1]);
        await MatchReferenceCountAsync(page.Browser, reference);
        await FreezePinnedAsync(page.Browser, milliseconds);
        await FreezePinnedAsync(reference, milliseconds);
        var failures = new List<string>();
        await CompareReferenceFrameAsync(page.Browser, reference, board, milliseconds, failures);
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Theory]
    [InlineData(LotteryTheme.StarSummon, "NG4-PageEntrySummon", 1920, 1080)]
    [InlineData(LotteryTheme.StarSummon, "NG4-PageEntrySummon", 1080, 1920)]
    [InlineData(LotteryTheme.MechaLock, "ND2-PinEntryMecha", 1920, 1080)]
    [InlineData(LotteryTheme.MechaLock, "ND2-PinEntryMecha", 1080, 1920)]
    [InlineData(LotteryTheme.CapsuleGacha, "ND3-PinEntryGacha", 1920, 1080)]
    [InlineData(LotteryTheme.CapsuleGacha, "ND3-PinEntryGacha", 1080, 1920)]
    public async Task Pinned_reference_entry_groups_match_the_original_first_page(LotteryTheme theme, string board, int width, int height)
    {
        await using var page = await OpenPageAsync(width, height);
        await using var reference = await OpenPinnedReferenceAsync(page, board, width, height);
        var lottery = page.H.Client.Lottery;
        lottery.SelectTheme(theme);
        await SeedPinnedEntryAsync(page, PinnedEntryNames(theme));
        lottery.SetEntryHidden(true); lottery.SetPinnedResults(true);
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.getElementById('scene').dataset.view === 'pinned'");
        await MatchReferenceCountAsync(page.Browser, reference);
        // The real draw picks random viewers: the original's first page takes the same names. The landscape-only boards lay out a portrait source too.
        var names = await page.Browser.EvaluateAsync("Array.from(document.querySelectorAll('.pinnedName'),e=>e.textContent)");
        await reference.EvaluateAsync("""
            Array.from(document.querySelectorAll('.rb-page')).slice(1).forEach(e=>e.remove());
            document.querySelectorAll('.rb-name').forEach((e,i)=>e.textContent=
            """ + names.GetRawText() + """
            [i]);
            const originalBoard=document.querySelector('.rb'); originalBoard.classList.toggle('o-port',innerWidth<innerHeight); originalBoard.classList.toggle('o-land',innerWidth>=innerHeight);
            for (let e = referenceSource; e !== document.body; e = e.parentElement) { e.style.width = innerWidth + 'px'; e.style.height = innerHeight + 'px'; }
            true
            """);
        await FreezePinnedAsync(page.Browser, 3000);
        await FreezePinnedAsync(reference, 3000);
        var failures = new List<string>();
        await CompareReferenceFrameAsync(page.Browser, reference, board, 3000, failures);
        Assert.Empty(page.Browser.Errors);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    private static async Task<ChromeBrowser> OpenPinnedReferenceAsync(PageSession page, string board, int width, int height, [CallerFilePath] string source = "")
    {
        var kind = board[..2];
        var props = new
        {
            theme = board.Contains("Mecha", StringComparison.Ordinal) ? "机甲锁定" : board.Contains("Gacha", StringComparison.Ordinal) ? "萌系扭蛋" : "星辰召唤",
            orient = width < height ? "竖屏" : "横屏",
            mode = board.Contains("Entry", StringComparison.Ordinal) ? "报名" : "奖池",
            stage = kind == "NH" ? "藏掉一条" : kind == "NG" ? "翻页" : kind is "NA" or "NB" or "NC" ? "新结果进来" : "常挂",
            tier = kind == "NB" ? "二奖" : kind == "NC" ? "三奖" : "大奖",
            scale = kind != "NS" ? "100%" : width < height ? "55%" : "50%", fx = "贴图", room = "透明",
        };
        var reference = await OpenDesignReferenceAsync(page, "RB1-PinBoard.dc.html", props, width, height, source);
        try
        {
            await reference.WaitAsync("!!document.querySelector('.rb') && document.fonts.status === 'loaded'");
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

    private static async Task<ChromeBrowser> OpenDesignReferenceAsync(PageSession page, string board, object props, int width, int height, [CallerFilePath] string source = "")
    {
        var port = WelcomeAnimationTests.FreePort();
        var script = Path.GetFullPath("../../docs/design/lottery-textures/tools/server.mjs", Path.GetDirectoryName(source)!);
        page.ReferenceServer = Process.Start(new ProcessStartInfo("node")
        {
            ArgumentList = { script, port.ToString() }, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        var root = await page.ReferenceServer.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.StartsWith($"http://localhost:{port}/", root);
        var reference = new ChromeBrowser();
        try
        {
            var url = $"http://localhost:{port}/render.html?board={board}&props=" + Uri.EscapeDataString(JsonSerializer.Serialize(props));
            await reference.OpenAsync(Path.Combine(page.H.DataDirectory, "reference-" + Guid.NewGuid()), url, width, height);
            return reference;
        }
        catch { await reference.DisposeAsync(); throw; }
    }

    // The original's demo rows, oldest first as Core draws them.
    private static async Task<(Grade Grade, string Prize, string? Who)[]> ReferenceRowsAsync(ChromeBrowser reference)
    {
        var rows = await reference.EvaluateAsync("Array.from(document.querySelectorAll('.rb-row'), e=>({grade:['Grand','Second','Third'].find(g=>e.classList.contains('g-'+g)),prize:e.querySelector('.rb-prize').textContent,who:e.querySelector('.rb-who').textContent}))");
        return rows.EnumerateArray().Reverse().Select(row =>
        {
            var who = row.GetProperty("who").GetString()!;
            return (Enum.Parse<Grade>(row.GetProperty("grade").GetString()!), row.GetProperty("prize").GetString()!, who == "主播点停" ? null : who);
        }).ToArray();
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
        await browser.EvaluateAsync($"document.getAnimations().forEach(a=>{{if(a.animationName === 'pinnedHide' || a.animationName === 'pinnedHideName') a.currentTime={elapsed}; if(a.animationName === 'pinnedFill' || a.animationName === 'pinnedEnter') a.currentTime={Math.Max(0, elapsed - 1320)};}}); true");
    }

    // The original boards keep their rb-* names. Each production class names the same element; the last is the page stack.
    private static readonly string[] ReferenceParts = [".rb-head", ".rb-title", ".rb-count", ".rb-row", ".rb-badge", ".rb-prize", ".rb-who", ".rb-name", ".rb-pages"];
    private static readonly string[] PinnedParts = [".pinnedHead", ".pinnedHeadTitle", ".pinnedHeadCount", ".pinnedRow", ".pinnedBadge", ".pinnedPrize", ".pinnedWho", ".pinnedName", ".pinnedPageStack"];

    // hiding: an NH collapse frame (milliseconds since the hide). ghostLimit and chipPreRoll accept the original demo's known differences.
    private static async Task CompareReferenceFrameAsync(ChromeBrowser browser, ChromeBrowser reference, string board, int milliseconds, List<string> failures,
        bool includePages = false, bool exactPixels = false, bool hiding = false, int ghostLimit = 0, bool chipPreRoll = false)
    {
        var source = await reference.EvaluateAsync("(() => { const b=referenceSource.getBoundingClientRect(); return {x:b.x,y:b.y,width:b.width,height:b.height}; })()");
        var clip = new { x = source.GetProperty("x").GetDouble(), y = source.GetProperty("y").GetDouble(), width = source.GetProperty("width").GetDouble(), height = source.GetProperty("height").GetDouble(), scale = 1 };
        // Only the original marks a removed row with is-gone and a removed chip with gone.
        string Measure(string[] parts, double x, double y) => """
            (() => Array.from(document.querySelectorAll(
            """ + JsonSerializer.Serialize(string.Join(",", includePages ? parts : parts[..^1])) + """
            )).filter(e => {
              for(let p=e;p;p=p.parentElement) { const s=getComputedStyle(p); if(+s.opacity<.01 || s.visibility==='hidden' || s.display==='none') return false; }
              return true;
            }).map(e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e),gone=document.querySelector('.rb-row.is-gone'); return {prototypeChip:e.matches('.rb-name.gone'),afterGone:!!gone&&!!(gone.compareDocumentPosition(e)&Node.DOCUMENT_POSITION_FOLLOWING),text:e.matches(
            """ + JsonSerializer.Serialize(string.Join(",", parts[0], parts[3], parts[4], parts[8])) + ")?'':e.textContent,x:r.x-" + JsonSerializer.Serialize(x) + ",y:r.y-" + JsonSerializer.Serialize(y) + ",w:r.width,h:r.height,font:s.fontSize};}))()";
        var expected = await reference.EvaluateAsync(Measure(ReferenceParts, clip.x, clip.y));
        var actual = await browser.EvaluateAsync(Measure(PinnedParts, 0, 0));
        string Motion(string selectors) => """
            Array.from(document.querySelectorAll(
            """ + JsonSerializer.Serialize(selectors) + """
            ),e=>{
              const s=getComputedStyle(e),r=e.getBoundingClientRect();
              return {text:e.textContent,classes:e.className,x:r.x,y:r.y,w:r.width,h:r.height,opacity:s.opacity,height:s.height,maxWidth:s.maxWidth,
                padding:s.padding,paddingX:parseFloat(s.paddingLeft)+parseFloat(s.paddingRight),border:s.borderWidth,margin:s.margin,marginLeft:parseFloat(s.marginLeft),overflow:s.overflow,transform:s.transform,matrix:Array.from(new DOMMatrix(s.transform).toFloat64Array()),delay:s.getPropertyValue('--d'),
                animations:e.getAnimations({subtree:true}).map(a=>({name:a.animationName,time:a.currentTime,pending:a.pending,state:a.playState,duration:a.effect.getTiming().duration,delay:a.effect.getTiming().delay,opacity:getComputedStyle(a.effect.target).opacity,transform:getComputedStyle(a.effect.target).transform}))};
            })
            """;
        var expectedMotion = await reference.EvaluateAsync(Motion(".rb-row.is-gone,.rb-name.gone,.rb-row.is-hiding,.rb-name.is-hiding,.rb-row.is-fill,.rb-row.amb"));
        var actualMotion = await browser.EvaluateAsync(Motion(".pinnedRow.hiding,.pinnedName.hiding,.pinnedRow.filling,.pinnedRow.ambient"));
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
            Array.from(document.querySelectorAll('.pinnedRow.ambient'),e=>({
              key:e.querySelector('.pinnedPrize').textContent+'|'+(e.querySelector('.pinnedWho')?.textContent||''),
              delay:parseFloat(getComputedStyle(e).getPropertyValue('--d')),
              arrival:!!e.closest('.arriving')||e.classList.contains('fresh'),
              animations:e.getAnimations({subtree:true}).map(a=>a.animationName)
            }))
            """);
        var phaseExpected = expected;
        if (hiding && milliseconds > 1320)
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
                """ + Measure(ReferenceParts, clip.x, clip.y) + "; changed.forEach(([a,time])=>a.currentTime=time); return bounds;})()");
        }
        var unexplained = differences.Except(geometry.Select(item => item.Detail)).ToList();
        var explained = new List<string>();
        var sourceGone = expectedMotion.EnumerateArray().FirstOrDefault(row => row.GetProperty("classes").GetString()!.Contains("gone", StringComparison.Ordinal));
        var actualGone = actualMotion.EnumerateArray().FirstOrDefault(row => row.GetProperty("classes").GetString()!.Contains("hiding", StringComparison.Ordinal));
        var ghost = sourceGone.ValueKind == JsonValueKind.Undefined ? 0 : sourceGone.GetProperty("h").GetDouble() - (actualGone.ValueKind == JsonValueKind.Undefined ? 0 : actualGone.GetProperty("h").GetDouble());
        foreach (var (index, dimension, detail) in geometry)
        {
            var target = phaseExpected[index].GetProperty(dimension).GetDouble();
            var residualBorder = dimension == "y" && expected[index].GetProperty("afterGone").GetBoolean() && ghost > 0 && ghost <= ghostLimit;
            if (residualBorder) target -= ghost;
            if (Math.Abs(target - actual[index].GetProperty(dimension).GetDouble()) <= 1)
                explained.Add(detail + (residualBorder ? " (original hidden row retains its border height)" : " (ambient delay follows reordered page)"));
            else if (chipPreRoll && expected[index].GetProperty("prototypeChip").GetBoolean() && dimension is "x" or "w")
            {
                var padding = actualGone.GetProperty("paddingX").GetDouble() - sourceGone.GetProperty("paddingX").GetDouble();
                var margin = actualGone.GetProperty("marginLeft").GetDouble() - sourceGone.GetProperty("marginLeft").GetDouble();
                var scale = actualGone.GetProperty("matrix")[0].GetDouble();
                // Width scales with padding; a centered transform shifts the left edge by half the unscaled remainder.
                var adjustment = dimension == "w" ? padding * scale : margin + padding * (1 - scale) / 2;
                if (padding is >= 0 and <= 32 && margin is >= 0 and <= 10 && scale is >= .6 and <= 1 &&
                    Math.Abs(target + adjustment - actual[index].GetProperty(dimension).GetDouble()) <= 1)
                    explained.Add(detail + " (original chip padding/margin interpolate during the demo pre-roll)");
                else unexplained.Add(detail);
            }
            else unexplained.Add(detail);
        }
        if (prefix != null)
        {
            await File.WriteAllBytesAsync(prefix + "-expected.png", Convert.FromBase64String(images[0]!), TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(prefix + "-actual.png", Convert.FromBase64String(images[1]!), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(prefix + ".json", JsonSerializer.Serialize(new { board, milliseconds, clip, expected, actual, expectedMotion, actualMotion, ambient, phaseExpected, pixels, differences, explained, unexplained }), TestContext.Current.CancellationToken);
        }
        if (hiding && milliseconds <= 1200)
        {
            var sourceTarget = expectedMotion.EnumerateArray().Single(row => row.GetProperty("classes").GetString()!.Contains("gone", StringComparison.Ordinal));
            var actualTarget = actualMotion.EnumerateArray().Single(row => row.GetProperty("classes").GetString()!.Contains("hiding", StringComparison.Ordinal));
            foreach (var property in new[] { "opacity", "maxWidth" })
            {
                var a = sourceTarget.GetProperty(property).GetString()!; var b = actualTarget.GetProperty(property).GetString()!;
                if (a == "none") continue;
                Assert.Equal(double.Parse(a.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture), double.Parse(b.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture), 2);
            }
            for (var index = 0; index < 16; index++) Assert.Equal(sourceTarget.GetProperty("matrix")[index].GetDouble(), actualTarget.GetProperty("matrix")[index].GetDouble(), 2);
        }
        if (exactPixels)
        {
            // Empty accessible emblems and the board's transparent text rasterize animated bitmaps differently when scaled.
            if (board.Contains("Mecha", StringComparison.Ordinal))
                Assert.All(pixels.GetProperty("paintedMean").EnumerateArray(), channel => Assert.InRange(channel.GetDouble(), 0, .75));
            else Assert.All(pixels.GetProperty("max").EnumerateArray(), channel => Assert.InRange(channel.GetInt32(), 0, 4));
        }
        foreach (var row in actualMotion.EnumerateArray().Where(row => row.GetProperty("classes").GetString()!.Contains("filling", StringComparison.Ordinal)))
            Assert.Equal("hidden", row.GetProperty("overflow").GetString());
        failures.AddRange(unexplained.Select(difference => $"{board}@{milliseconds}ms: {difference}"));
    }
}
