using System.Diagnostics;
using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed partial class GiftBoardBrowserTests
{
    // Where the board's cards and pills are, in design pixels (design package README, 画面).
    private const double Mascot = 220, NoMascot = 8, CardsY = 92, RowsY = 416, Pitch = 60;
    private const string Canvas = "document.querySelector('canvas')";
    private const string Playing = Canvas + ".dataset.event";
    private const string Clock = "document.getAnimations()[0]";
    private const string TwoFrames = "new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => done(true))))";

    /// <summary>Records every sound the page starts, with when its loudest moment is heard, counted from the start of the event.</summary>
    private const string RecordSounds = $$"""
        window.sounds = [];
        const start = AudioBufferSourceNode.prototype.start;
        AudioBufferSourceNode.prototype.start = function (when, offset) {
          const ctx = this.context, buffer = this.buffer;
          let top = 0, at = 0;
          for (let channel = 0; channel < buffer.numberOfChannels; channel++) {
            const samples = buffer.getChannelData(channel);
            for (let i = 0; i < samples.length; i++) if (Math.abs(samples[i]) > top) { top = Math.abs(samples[i]); at = i / buffer.sampleRate; }
          }
          const sinceEvent = ({{Clock}}.currentTime - window.eventStart) / 1000, latency = ctx.baseLatency + (ctx.outputLatency || 0);
          sounds.push({ length: buffer.duration, peakAfterEventStart: sinceEvent + (when - ctx.currentTime) + (at - offset) + latency });
          return start.apply(this, arguments);
        };
        true
        """;

    /// <summary>The gift board on, a room connected, and a Chrome that is not open yet.</summary>
    private static async Task<PageSession> StartAsync(params (string Name, string Value)[] settings)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real gift board browser acceptance.");
        var h = new WatchHarness();
        var page = new PageSession(h, await h.WatchWithGiftBoardAsync());
        try
        {
            if (settings.Length > 0) await h.ApplySettingsAsync(settings);
            return page;
        }
        catch
        {
            await page.DisposeAsync();
            throw;
        }
    }

    /// <summary>A connected room, and the page open in Chrome once <see cref="OpenAsync"/> has run; the browser closes first.</summary>
    private sealed class PageSession(WatchHarness h, IAsyncEnumerator<WatchUpdate> watch) : IAsyncDisposable
    {
        public WatchHarness H { get; } = h;
        public IAsyncEnumerator<WatchUpdate> Watch { get; } = watch;
        public ChromeBrowser Browser { get; } = new();

        public async Task OpenAsync(int width, int height)
        {
            await Browser.OpenAsync(H.DataDirectory, H.Client.WebService.Status.GiftBoardAddress, width, height);
            await Browser.WaitAsync($"{Playing} === ''");
            await WaitForStatesAsync(1);
        }

        /// <summary>The viewer sends <paramref name="num"/> more of the same gift at <paramref name="price"/> 金瓜子 each.</summary>
        public Task GiveAsync(long uid, long price, long num = 1) =>
            PushAsync(BlindGiftFixture.Ordinary(uid: uid, nickname: "观众" + uid, price: price, num: num, tid: Guid.NewGuid().ToString()));

        /// <summary>Pushes a gift through the room. Once the page is open this returns when the board's new state has reached it
        /// and the page has had frames to start whatever it calls for.</summary>
        public async Task PushAsync(string gift)
        {
            var received = Browser.EventSourceMessages.Count;
            await H.PushAndSyncAsync(Watch, gift);
            if (received == 0) return;
            await WaitForStatesAsync(received + 1);
            await Browser.EvaluateAsync(TwoFrames);
        }

        /// <summary>Waits until the page has received that many states, the one it gets on connecting included.</summary>
        public async Task WaitForStatesAsync(int count)
        {
            var wait = Stopwatch.StartNew();
            while (Browser.EventSourceMessages.Count < count && wait.Elapsed < TimeSpan.FromSeconds(10)) await Browser.EvaluateAsync("true");
            Assert.True(Browser.EventSourceMessages.Count >= count, $"The page received {Browser.EventSourceMessages.Count} states, not {count}.");
        }

        /// <summary>That many viewers, the first of them sending the most.</summary>
        public async Task GiveToAsync(int viewers)
        {
            for (var viewer = 1; viewer <= viewers; viewer++) await GiveAsync(10000 + viewer, 5000 - viewer * 100);
        }

        public async Task<string> PlayingAsync() => (await Browser.EvaluateAsync(Playing)).GetString()!;

        /// <summary>Stops the page's clock; from here the page only moves when a test moves it.</summary>
        public async Task PauseClockAsync() => await Browser.EvaluateAsync($"{Clock}.pause(); {TwoFrames}");

        public async Task StepClockAsync(int milliseconds) =>
            await Browser.EvaluateAsync($"{Clock}.currentTime += {milliseconds}; {TwoFrames}");

        /// <summary>Waits for the page to start the event, and notes the time it started at.</summary>
        public async Task WaitForEventAsync(string kind)
        {
            try { await Browser.WaitAsync($"{Playing} === '{kind}'"); }
            catch (Xunit.Sdk.XunitException) { Assert.Fail($"The page never played '{kind}'; it is playing '{await PlayingAsync()}'. States: {string.Join("\n", Browser.EventSourceMessages.TakeLast(3))}"); }
            await Browser.EvaluateAsync($"window.eventStart = {Clock}.currentTime; true");
        }

        public async Task SetEventTimeAsync(int milliseconds) =>
            await Browser.EvaluateAsync($"{Clock}.currentTime = window.eventStart + {milliseconds}; {TwoFrames}");

        /// <summary>Lets every waiting event play out.</summary>
        public async Task SettleAsync()
        {
            for (var step = 0; step < 8 && await PlayingAsync() != ""; step++) await StepClockAsync(5000);
            Assert.Equal("", await PlayingAsync());
        }

        /// <summary>A mouse click: the browser lets a page play sound only after the viewer has touched it.</summary>
        public async Task ClickAsync()
        {
            await Browser.CommandAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x = 10, y = 10, button = "left", clickCount = 1 });
            await Browser.CommandAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x = 10, y = 10, button = "left", clickCount = 1 });
        }

        /// <summary>How opaque the page is, 0 to 255, at each point, from a screenshot.</summary>
        public async Task<int[]> AlphaAsync(IEnumerable<(int X, int Y)> points)
        {
            var shot = await Browser.CommandAsync("Page.captureScreenshot", new { format = "png" });
            var sampled = await Browser.EvaluateAsync($$"""
                (async () => {
                  const image = new Image();
                  image.src = 'data:image/png;base64,{{shot.GetProperty("data").GetString()}}';
                  await image.decode();
                  const canvas = document.createElement('canvas');
                  canvas.width = image.width; canvas.height = image.height;
                  const g = canvas.getContext('2d');
                  g.drawImage(image, 0, 0);
                  return {{JsonSerializer.Serialize(points.Select(point => new[] { point.X, point.Y }))}}.map(([x, y]) => g.getImageData(x, y, 1, 1).data[3]);
                })()
                """);
            return [.. sampled.EnumerateArray().Select(alpha => alpha.GetInt32())];
        }

        /// <summary>Waits until the page is solid at the point, as a pill's body is.</summary>
        public async Task WaitForSolidAsync(int x, int y)
        {
            for (var attempt = 0; attempt < 100; attempt++)
                if ((await AlphaAsync([(x, y)]))[0] > 100) return;
            Assert.Fail($"The page never became solid at ({x}, {y}).");
        }

        /// <summary>The first and the last column along row <paramref name="y"/> that are mostly opaque.</summary>
        public async Task<(int Left, int Right)> OpaqueEdgesAsync(int y, int width)
        {
            var alphas = await AlphaAsync(Enumerable.Range(0, width).Select(x => (x, y)));
            return (Array.FindIndex(alphas, alpha => alpha >= 120), Array.FindLastIndex(alphas, alpha => alpha >= 120));
        }

        public async ValueTask DisposeAsync()
        {
            await using (H)
            await using (Watch)
                await Browser.DisposeAsync();
        }
    }
}
