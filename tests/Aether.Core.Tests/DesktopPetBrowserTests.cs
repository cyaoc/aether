using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>The 桌宠页 in a real browser: what happens in the room and the saved settings reach it as SSE events, and its engine
/// plays them. The page's clock is an animation, so these tests stop it and move it by hand.</summary>
public sealed class DesktopPetBrowserTests
{
    private const string Clock = "document.getAnimations()[0]";
    private const string TwoFrames = "new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => done(true))))";
    // The box of what the page draws, as [left, top, right, bottom], or null when it is transparent.
    private const string OpaqueBox = """
        (() => {
          const canvas = document.querySelector('canvas'), { width, height } = canvas, data = canvas.getContext('2d').getImageData(0, 0, width, height).data;
          let box = null;
          for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) if (data[(y * width + x) * 4 + 3] > 128)
            box = box ? [Math.min(box[0], x), Math.min(box[1], y), Math.max(box[2], x), Math.max(box[3], y)] : [x, y, x, y];
          return box;
        })()
        """;
    private static readonly string[] DailySheets = ["pose-3-daily-1", "pose-9-ski", "pose-10-gym", "pose-11-hike", "pose-12-cycle"];

    [Fact]
    public async Task The_page_is_transparent_while_the_desktop_pet_is_off_and_scales_with_the_browser_source_at_three_to_four()
    {
        await using var page = await StartAsync(on: false);
        await page.OpenAsync(480, 640);
        Assert.Equal(JsonValueKind.Null, (await page.Browser.EvaluateAsync(OpaqueBox)).ValueKind);

        await page.ApplyAsync(("desktop_pet.enabled", "true"));
        var box = await BoxAsync();
        // Twice as big, and as big again in a wider source: there she stands in its middle.
        foreach (var (width, height, scale, left) in new[] { (960, 1280, 2.0, 0.0), (800, 640, 1.0, 160.0) })
        {
            await page.Browser.CommandAsync("Emulation.setDeviceMetricsOverride", new { width, height, deviceScaleFactor = 1, mobile = false });
            await page.Browser.EvaluateAsync(TwoFrames);
            Assert.All((await BoxAsync()).Select((edge, i) => (edge, expected: box[i] * scale + (i % 2 == 0 ? left : 0))),
                pair => Assert.InRange(pair.edge, pair.expected - 3, pair.expected + 3));
        }
        Assert.Empty(page.Browser.Errors);

        async Task<double[]> BoxAsync() => [.. (await page.Browser.EvaluateAsync(OpaqueBox)).EnumerateArray().Select(edge => edge.GetDouble())];
    }

    [Fact]
    public async Task Called_the_捣蛋鬼_sets_off_on_the_page_which_reports_it_by_the_id_its_events_were_asked_for_with()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        await page.H.Client.WebService.SummonAsync();

        await foreach (var summon in page.H.Client.WebService.GetSummonUpdatesAsync(timeout.Token))
            if (summon.Visit == PranksterVisit.OnStage) break;
        Assert.Equal("peek", (await page.Browser.EvaluateAsync("window.pet.gm.visit?.phase")).GetString());
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_summon_the_page_calls_off_as_the_desktop_pet_is_turned_off_is_reported_in_the_run_it_was_called_in()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        var service = page.H.Client.WebService;
        // the 试演 she plays keeps the 捣蛋鬼 called waiting; her reports are held back, as ones slow to reach Core
        await service.TestShowAsync("captain", null, "星河");
        await page.Browser.EvaluateAsync("""
            window.held = [];
            const send = window.fetch.bind(window);
            window.fetch = (url, o) => String(url).startsWith('/desktop-pet/report') ? (window.held.push(String(url)), Promise.resolve()) : send(url, o);
            """);
        await service.SummonAsync();
        await Eventually.TrueAsync(async () => (await page.Browser.EvaluateAsync("window.pet.gm.due")).ValueKind == JsonValueKind.String);

        await page.ApplyAsync(("desktop_pet.enabled", "false"));

        var held = (await page.Browser.EvaluateAsync("JSON.stringify(window.held)")).GetString()!;
        Assert.Matches(@"^\[""/desktop-pet/report\?visit=end&page=\w+&run=0""\]$", held);
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task The_page_reports_one_after_another_so_Core_never_takes_a_来访s_start_after_its_end()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        // its first report is slow to reach Core
        await page.Browser.EvaluateAsync("""
            window.sent = [];
            const send = window.fetch.bind(window);
            window.fetch = (url, o) => {
              if (!String(url).startsWith('/desktop-pet/report')) return send(url, o);
              window.sent.push(String(url).split('?')[1].split('&')[0]);
              return window.sent.length > 1 ? send(url, o) : new Promise(done => { window.release = () => done(send(url, o)); });
            };
            window.pet.visited('start'); window.pet.visited('end'); true;
            """);
        await page.Browser.EvaluateAsync(TwoFrames);
        Assert.Equal("""["visit=start"]""", (await page.Browser.EvaluateAsync("JSON.stringify(window.sent)")).GetString());

        await page.Browser.EvaluateAsync("window.release(), true");
        await Eventually.TrueAsync(async () => (await page.Browser.EvaluateAsync("window.sent.length")).GetInt32() == 2);
        Assert.Equal("""["visit=start","visit=end"]""", (await page.Browser.EvaluateAsync("JSON.stringify(window.sent)")).GetString());
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_report_that_does_not_get_to_Core_is_sent_again_before_the_next()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        // its first try at the first report fails, as with Core restarting; what Core answers is kept
        await page.Browser.EvaluateAsync("""
            window.tries = [];
            window.answers = [];
            const send = window.fetch.bind(window);
            window.fetch = (url, o) => {
              if (!String(url).startsWith('/desktop-pet/report')) return send(url, o);
              window.tries.push(String(url).split('?')[1].split('&')[0]);
              if (window.tries.length === 1) return Promise.reject(new TypeError('Failed to fetch'));
              return send(url, o).then(reply => (window.answers.push(reply.status), reply));
            };
            window.pet.visited('start'); window.pet.visited('end'); true;
            """);

        await Eventually.TrueAsync(async () => (await page.Browser.EvaluateAsync("window.answers.length")).GetInt32() == 2);
        Assert.Equal("""[["visit=start","visit=start","visit=end"],[204,204]]""",
            (await page.Browser.EvaluateAsync("JSON.stringify([window.tries, window.answers])")).GetString());
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task The_捣蛋鬼_on_the_page_is_told_to_Core_again_once_the_pages_events_reconnect()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        var service = page.H.Client.WebService;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var summons = service.GetSummonUpdatesAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        await service.SummonAsync();
        await UntilAsync(PranksterVisit.OnStage);
        // (its pictures loaded first: one the restart cut off would not load)
        await page.Browser.EvaluateAsync("Promise.all(Object.values(window.pet.loads)).then(() => true)");

        // the web service restarts on its port: Core finds the page gone, and the page's events reconnect
        var port = new Uri(service.Status.Origin).Port;
        await service.StopAsync();
        await UntilAsync(PranksterVisit.Away);
        await service.StartAsync(port);

        await UntilAsync(PranksterVisit.OnStage);
        Assert.Equal("peek", (await page.Browser.EvaluateAsync("window.pet.gm.visit?.phase")).GetString());
        Assert.Empty(page.Browser.Errors);

        async Task UntilAsync(PranksterVisit visit)
        {
            do Assert.True(await summons.MoveNextAsync());
            while (summons.Current.Visit != visit);
        }
    }

    [Fact]
    public async Task The_one_called_still_on_its_way_is_told_to_Core_again_once_the_pages_events_reconnect()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        var service = page.H.Client.WebService;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var summons = service.GetSummonUpdatesAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        // the 试演 she plays keeps the 捣蛋鬼 called waiting
        await service.TestShowAsync("captain", null, "星河");
        await service.SummonAsync();
        await UntilAsync(PranksterVisit.OnTheWay);
        await Eventually.TrueAsync(async () => (await page.Browser.EvaluateAsync("String(window.pet.gm.due)")).GetString() == "called");
        // (its pictures loaded first, the game's too, which it loads once one is due: one the restart cut off would not load)
        await page.Browser.EvaluateAsync("Promise.all([window.pet.loadGame(), ...Object.values(window.pet.loads)]).then(() => true)");

        var port = new Uri(service.Status.Origin).Port;
        await service.StopAsync();
        await UntilAsync(PranksterVisit.Away);
        await service.StartAsync(port);

        await UntilAsync(PranksterVisit.OnTheWay);
        Assert.Equal(JsonValueKind.Null, (await page.Browser.EvaluateAsync("window.pet.gm.visit")).ValueKind);
        Assert.Empty(page.Browser.Errors);

        async Task UntilAsync(PranksterVisit visit)
        {
            do Assert.True(await summons.MoveNextAsync());
            while (summons.Current.Visit != visit);
        }
    }

    [Fact]
    public async Task The_捣蛋鬼_whose_pictures_failed_to_load_comes_once_they_load_again()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        var service = page.H.Client.WebService;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        // its pictures cannot be had, as while the web service restarts
        await page.Browser.CommandAsync("Network.setBlockedURLs", new { urls = new[] { "*prankster*" } });
        await service.SummonAsync();
        Assert.Equal("failed", (await page.Browser.EvaluateAsync("""
            new Promise(done => { const wait = () => window.pet.loads.game ? window.pet.loads.game.then(() => done('loaded'), () => done('failed')) : requestAnimationFrame(wait); wait(); })
            """)).GetString());

        await page.Browser.CommandAsync("Network.setBlockedURLs", new { urls = Array.Empty<string>() });

        await foreach (var summon in service.GetSummonUpdatesAsync(timeout.Token))
            if (summon.Visit == PranksterVisit.OnStage) break;
        Assert.Equal("peek", (await page.Browser.EvaluateAsync("window.pet.gm.visit?.phase")).GetString());
    }

    [Fact]
    public async Task The_desktop_pet_turned_off_and_on_while_the_pages_events_were_down_leaves_nothing_of_the_run_before()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        var service = page.H.Client.WebService;
        // the 试演 she plays keeps the 捣蛋鬼 called waiting; her reports are kept as she sends them
        await service.TestShowAsync("captain", null, "星河");
        await service.SummonAsync();
        await Eventually.TrueAsync(async () => (await page.Browser.EvaluateAsync("String(window.pet.gm.due)")).GetString() == "called");
        await page.Browser.EvaluateAsync("Promise.all([window.pet.loadGame(), ...Object.values(window.pet.loads)]).then(() => true)");
        // and the room has been quiet for 45 minutes
        await page.Browser.EvaluateAsync("""
            window.pet.lastGift = window.pet.lastAny = window.pet.now() - 45 * 60000;
            window.sent = [];
            const send = window.fetch.bind(window);
            window.fetch = (url, o) => (String(url).startsWith('/desktop-pet/report') && window.sent.push(String(url)), send(url, o));
            true;
            """);

        var port = new Uri(service.Status.Origin).Port;
        await service.StopAsync();
        await page.H.ApplySettingsAsync(("desktop_pet.enabled", "false"));
        await page.H.ApplySettingsAsync(("desktop_pet.enabled", "true"));
        var received = page.Browser.EventSourceMessages.Count;
        await service.StartAsync(port);
        await Eventually.TrueAsync(async () =>
        {
            await page.Browser.EvaluateAsync("true");
            return page.Browser.EventSourceMessages.Skip(received).Any(data => data.Contains("\"run\":1", StringComparison.Ordinal));
        });
        await page.Browser.EvaluateAsync(TwoFrames);

        Assert.Equal("""[false,null,null,[],"calm"]""",
            (await page.Browser.EvaluateAsync("JSON.stringify([window.pet.gm.due, window.pet.gm.visit, window.pet.cur, window.sent.filter(url => !url.includes('run=0')), window.pet.mood(window.pet.now(), false)])")).GetString());
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_daily_actions_pictures_load_when_it_first_plays_not_with_the_page()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        Assert.Contains(page.Browser.Requests, url => url.EndsWith("/characters/current/desktop-pet/pose-1-common.webp", StringComparison.Ordinal));
        Assert.DoesNotContain(page.Browser.Requests, url => DailySheets.Any(sheet => url.EndsWith(sheet + ".webp", StringComparison.Ordinal)));

        await page.H.Client.WebService.TestShowAsync("gym", null, "星河");

        await page.Browser.WaitAsync("window.pet.cur?.name === 'gym'");
        await Eventually.TrueAsync(async () =>
        {
            await page.Browser.EvaluateAsync("true");
            return page.Browser.Requests.Any(url => url.EndsWith("/pose-10-gym.webp", StringComparison.Ordinal));
        });
        Assert.DoesNotContain(page.Browser.Requests, url => url.EndsWith("/pose-9-ski.webp", StringComparison.Ordinal));
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_first_frame_in_a_pose_whose_pictures_still_load_draws_her_idle()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);

        // As the page opened while a lottery waits for its draw: she hopes, and the lottery's sheet has not loaded yet.
        var pose = await page.Browser.EvaluateAsync("""
            (() => {
              const sheet = pet.art.poses.hope[0], loaded = pet.cells[sheet], st = pet.stateAt(pet.now());
              delete pet.cells[sheet]; delete pet.hv; st.pose = 'hope';
              try { pet.draw(document.querySelector('canvas').getContext('2d'), st, pet.now()); return pet.hv.pose; }
              finally { if (loaded) pet.cells[sheet] = loaded; }
            })()
            """);

        Assert.Equal("idle", pose.GetString());
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_first_PK_sign_show_while_her_signs_pictures_still_load_has_her_stand_as_she_was()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);

        // (her sign's sheet loads as the show starts, not with the page)
        var drawn = await page.Browser.EvaluateAsync("""
            (() => {
              const loaded = !!pet.cells[pet.art.poses.signUp[0]];
              pet.start('pkLastSeconds', {}); pet.frame();
              return JSON.stringify([loaded, pet.hv.pose, pet.signShown]);
            })()
            """);

        Assert.Equal("""[false,"idle",false]""", drawn.GetString());
        Assert.Empty(page.Browser.Errors);
    }

    [Theory]
    [InlineData("pkOpponent", "intelHeld", "intelBack")]
    [InlineData("pkLastSeconds", "signHeld", "signBack")]
    public async Task The_desktop_pet_turned_off_while_she_holds_a_card_or_her_sign_does_not_bring_it_back_turned_on(string show, string held, string back)
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        await page.H.Client.WebService.TestShowAsync(show, null, "星河");
        await page.Browser.WaitAsync($"window.pet.cur?.name === '{show}'");
        Assert.True((await page.Browser.EvaluateAsync($$"""
            (async () => {
              for (let i = 0; i < 40 && !pet.{{held}}; i++) { await Promise.all(Object.values(pet.loads)); {{Clock}}.currentTime += 250; await {{TwoFrames}}; }
              return !!pet.{{held}};
            })()
            """)).GetBoolean());

        await page.ApplyAsync(("desktop_pet.enabled", "false"));
        await page.ApplyAsync(("desktop_pet.enabled", "true"));
        await page.Browser.EvaluateAsync(TwoFrames);

        Assert.Equal("[null,null]", (await page.Browser.EvaluateAsync($"JSON.stringify([pet.{held} ?? null, pet.{back} ?? null])")).GetString());
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task Her_PK_sign_fits_the_widest_double_bonus_conditions_at_full_size()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);

        // each condition's first row in her sign's fonts, and the width inside its border (the board checks it as it loads)
        var measured = await page.Browser.EvaluateAsync("""
            (async () => {
              const { SIGN } = await import('/desktop-pet/sign.mjs'), { SANS } = await import('/gift-board/paint.mjs'), g = document.createElement('canvas').getContext('2d');
              const wide = row => row.reduce((width, [text, weight, px]) => (g.font = `${weight} ${px}px ${SANS}`, width + g.measureText(text).width), 0);
              return [SIGN.W - 2 * SIGN.PAD, ...['差9999票翻倍', '差1.2万票翻倍', '差1人翻倍', '差99999票翻倍', '差12.3万票翻倍'].map(text => wide(SIGN.words('pkCondition', { text })[0]))];
            })()
            """);

        var (inside, widths) = (measured[0].GetDouble(), measured.EnumerateArray().Skip(1).Select(width => width.GetDouble()).ToList());
        Assert.Equal(5, widths.Count);
        Assert.All(widths, width => Assert.InRange(width, 1, inside));
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_frame_that_throws_loses_only_itself()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);

        var frames = await page.Browser.EvaluateAsync($$"""
            (async () => {
              const frame = pet.frame;
              let frames = 0;
              pet.frame = function () { if (++frames === 1) throw new Error('one frame failed'); return frame.call(this); };
              await {{TwoFrames}}; await {{TwoFrames}};
              pet.frame = frame;
              return frames;
            })()
            """);

        Assert.True(frames.GetInt32() > 1);
        Assert.Contains(page.Browser.Errors, error => error.Contains("one frame failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Saved_thresholds_lines_mood_minutes_and_daily_actions_play_without_reloading_the_page()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        await page.ApplyAsync(("desktop_pet.mid_gift_spend", "10"));
        await page.H.PushAndSyncAsync(page.Updates, BlindGiftFixture.Ordinary(price: 2000, tid: "t1"));
        await page.Browser.WaitAsync("window.pet.cur?.name === 'midGift'");

        Settings.SaveDesktopPet(page.H.DataDirectory, new Dictionary<string, string> { ["desktop_pet.bored_minutes"] = "1" },
            new Dictionary<string, IReadOnlyList<string>> { ["entry"] = ["{昵称}，谢谢你～"] }, Settings.Load(page.H.DataDirectory));
        await page.ApplyAsync();
        await page.Browser.EvaluateAsync($"{Clock}.currentTime += 4000; {TwoFrames}");
        await page.H.PushAndSyncAsync(page.Updates, EntryV2Fixture.Message(uid: 20002, nickname: "进场观众"));
        await page.Browser.WaitAsync("window.pet.cur?.name === 'entry'");
        Assert.Equal("{昵称}，谢谢你～", (await page.Browser.EvaluateAsync("window.pet.cur.line")).GetString());
        await page.Browser.EvaluateAsync($"{Clock}.currentTime += 61000; {TwoFrames}");
        Assert.Equal("bored", (await page.Browser.EvaluateAsync("window.pet.mood(window.pet.now())")).GetString());

        await page.ApplyAsync(("desktop_pet.bored_minutes", "0"), ("desktop_pet.shows_off", "[hum, gym, hike, bike]"), ("desktop_pet.mishap_percent", "0"));
        Assert.Equal("""["ski",false]""", (await page.Browser.EvaluateAsync("JSON.stringify([window.pet.cur?.name, window.pet.cur?.opts.mishap])")).GetString());
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_test_show_played_out_is_reported_for_the_GUI_to_offer_another()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 640);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var updates = page.H.Client.WebService.GetUpdatesAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await updates.MoveNextAsync());

        await page.H.Client.WebService.TestShowAsync("entry", null, "星河");
        await page.Browser.WaitAsync("window.pet.cur?.name === 'entry'");
        await page.Browser.EvaluateAsync($"{Clock}.currentTime += 1600; {TwoFrames}");

        while (await updates.MoveNextAsync() && updates.Current is not DesktopPetTestEnded) { }
        Assert.IsType<DesktopPetTestEnded>(updates.Current);
        Assert.Empty(page.Browser.Errors);
    }

    /// <summary>The design package's board and the page, side by side through scripts/desktop-pet/compare.mjs (about a minute):
    /// every show, 心情 and round and the 宝箱小游戏's routes from one seed, frame by frame on one clock, the same pixels.</summary>
    [Fact]
    public async Task The_page_draws_every_show_pixel_for_pixel_as_the_design_board_does()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real 桌宠页 browser acceptance.");
        var port = WelcomeAnimationTests.FreePort().ToString(CultureInfo.InvariantCulture);
        var differences = Path.Combine(Path.GetTempPath(), "desktop-pet-compare-" + Guid.NewGuid());
        using var server = Node(RepositoryFile("docs/design/desktop-pet/tools/server.mjs"), port);
        try
        {
            Assert.StartsWith($"http://localhost:{port}/", await server.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            using var compare = Node(RepositoryFile("scripts/desktop-pet/compare.mjs"), "--port", port, "--out", differences);
            var output = await compare.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await compare.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(compare.ExitCode == 0, $"{output}pictures of the moments that differ: {differences}");
        }
        finally { server.Kill(); }
    }

    private static Process Node(params string[] arguments)
    {
        var start = new ProcessStartInfo("node") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    private static string RepositoryFile(string path, [CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine("../..", path), Path.GetDirectoryName(source)!);

    /// <summary>A connected room with the 桌宠 <paramref name="on"/>, and a Chrome that is not open yet.</summary>
    private static async Task<PageSession> StartAsync(bool on = true)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real 桌宠页 browser acceptance.");
        var h = new WatchHarness();
        try
        {
            await h.LoginAsync();
            await h.ApplySettingsAsync(("desktop_pet.enabled", on ? "true" : "false"));
            h.EnableStreamOverlay = true;
            return new PageSession(h, await h.WatchConnectedAsync(includeGiftRounds: true));
        }
        catch
        {
            await h.DisposeAsync();
            throw;
        }
    }

    /// <summary>A connected room and the 桌宠页 open in Chrome once <see cref="OpenAsync"/> has run, its engine as window.pet and
    /// its clock stopped.</summary>
    private sealed class PageSession(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates) : IAsyncDisposable
    {
        public WatchHarness H { get; } = h;
        public IAsyncEnumerator<WatchUpdate> Updates { get; } = updates;
        public ChromeBrowser Browser { get; } = new();

        public async Task OpenAsync(int width, int height)
        {
            await Browser.OpenAsync(H.DataDirectory, H.Client.WebService.Status.DesktopPetAddress, width, height);
            await Browser.EvaluateAsync($"(async () => {{ window.pet = (await import('/desktop-pet/page.mjs')).pet; {Clock}.pause(); return {TwoFrames}; }})()");
            await WaitForStateAsync(0);
        }

        /// <summary>Saves <paramref name="changes"/> and waits for the state the page is told of them, and for it to draw.</summary>
        public async Task ApplyAsync(params (string Name, string Value)[] changes)
        {
            var received = Browser.EventSourceMessages.Count;
            await H.ApplySettingsAsync(changes);
            await WaitForStateAsync(received);
            await Browser.EvaluateAsync(TwoFrames);
        }

        /// <summary>Waits until the page has received a state after its first <paramref name="received"/> events; the danmaku
        /// the harness syncs with are told too.</summary>
        private Task WaitForStateAsync(int received) => Eventually.TrueAsync(async () =>
        {
            await Browser.EvaluateAsync("true");
            return Browser.EventSourceMessages.Skip(received).Any(data => data.Contains("\"enabled\"", StringComparison.Ordinal));
        });

        public async ValueTask DisposeAsync()
        {
            await using (H)
            await using (Updates)
                await Browser.DisposeAsync();
        }
    }
}
