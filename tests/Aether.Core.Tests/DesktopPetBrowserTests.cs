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
