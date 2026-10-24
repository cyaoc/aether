using System.Text.Json;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
    [Theory]
    [InlineData(LotteryTheme.StarSummon, 1920, 1080, "W1-PoolSummon.dc.html", "w1")]
    [InlineData(LotteryTheme.StarSummon, 1080, 1920, "PW1-PoolSummonPortrait.dc.html", "pw1")]
    [InlineData(LotteryTheme.MechaLock, 1920, 1080, "W2-PoolMecha.dc.html", "w2")]
    [InlineData(LotteryTheme.MechaLock, 1080, 1920, "PW2-PoolMechaPortrait.dc.html", "pw2")]
    [InlineData(LotteryTheme.CapsuleGacha, 1920, 1080, "W3-PoolGacha.dc.html", "w3")]
    [InlineData(LotteryTheme.CapsuleGacha, 1080, 1920, "PW3-PoolGachaPortrait.dc.html", "pw3")]
    public async Task Winner_markers_match_the_frozen_design_boards(LotteryTheme theme, int width, int height, string board, string prefix)
    {
        await using var page = await OpenPageAsync(width, height);
        var lottery = page.H.Client.Lottery;
        lottery.SelectTheme(theme);
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry([new("一等奖", "机械键盘", 2, Grade.Grand), new("二等奖", "周边抱枕", 2, Grade.Second), new("三等奖", "签名照", 3, Grade.Third)], "抽奖");
        await page.H.PushAndCollectAsync(page.Watch, Enumerable.Range(1, 6).Select(i => Message(i, $"观众{i}")).ToArray());
        lottery.CloseEntries();
        for (var i = 0; i < 3; i++) { lottery.DrawEntry(i); page.H.Time.Advance(LotteryController.DrawDuration); }
        await ReloadAtCoreTimeAsync(page);
        await page.Browser.WaitAsync("document.querySelectorAll('.winnerName').length === 3");
        await using var reference = await OpenDesignReferenceAsync(page, board, new { mode = "报名", stage = "中奖名单", fx = "贴图", room = "透明" }, width, height);
        var origin = (await reference.EvaluateAsync("location.origin")).GetString();
        foreach (var ended in new[] { false, true })
        {
            if (ended)
            {
                lottery.EndEntry();
                await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance" });
                await page.Browser.WaitAsync("document.getElementById('remaining').hidden");
            }
            var props = Uri.EscapeDataString(JsonSerializer.Serialize(new { mode = "报名", stage = ended ? "中奖名单" : "已截止", fx = "贴图", room = "透明" }));
            await reference.CommandAsync("Page.navigate", new { url = $"{origin}/render.html?board={board}&props={props}" });
            var referenceSelector = theme == LotteryTheme.StarSummon ? $".{prefix}-deal [class*='t-frame']" : theme == LotteryTheme.MechaLock ? $".{prefix}-row .{prefix}t-emblem" : $".{prefix}t-cap";
            await reference.WaitAsync($"document.querySelectorAll({JsonSerializer.Serialize(referenceSelector)}).length > 0 && document.fonts.status === 'loaded'");
            foreach (var browser in new[] { page.Browser, reference })
                await browser.EvaluateAsync("document.getAnimations().forEach(a=>{a.pause();a.currentTime=3000}); true");
            var actualSelector = theme == LotteryTheme.StarSummon ? ".winnerCard>.summonFrame" : theme == LotteryTheme.MechaLock ? ".winnerCard>.mechaEmblem" : ".winnerGroup>.gachaCapsule";
            // Compare local CSS dimensions, independent of the design preview's source scaling.
            const string measure = "e=>{const s=getComputedStyle(e);return {width:e.offsetWidth,height:e.offsetHeight,slice:s.borderImageSlice,border:s.borderImageWidth,top:s.top,left:s.left,image:s.backgroundImage,clip:s.backgroundClip,position:parseFloat(s.backgroundPositionX)}}";
            var actual = await page.Browser.EvaluateAsync($"Array.from(document.querySelectorAll('{actualSelector}'),{measure})");
            var expected = await reference.EvaluateAsync($"Array.from(document.querySelectorAll({JsonSerializer.Serialize(referenceSelector)}),{measure})");
            Assert.Equal(3, actual.GetArrayLength());
            var expectedMarker = expected[0];
            // Closed boards contain only Third results; their frame sizes still match the same card geometry.
            foreach (var marker in actual.EnumerateArray())
            {
                Assert.Equal(expectedMarker.GetProperty("width").GetInt32(), marker.GetProperty("width").GetInt32());
                Assert.Equal(expectedMarker.GetProperty("height").GetInt32(), marker.GetProperty("height").GetInt32());
                if (theme == LotteryTheme.StarSummon)
                {
                    Assert.Equal(expectedMarker.GetProperty("top").GetString(), marker.GetProperty("top").GetString());
                    Assert.Equal(expectedMarker.GetProperty("left").GetString(), marker.GetProperty("left").GetString());
                }
            }
            if (theme != LotteryTheme.StarSummon)
            {
                Assert.All(actual.EnumerateArray(), marker =>
                {
                    Assert.NotEqual("none", marker.GetProperty("image").GetString());
                    Assert.Equal("border-box", marker.GetProperty("clip").GetString());
                });
                Assert.Equal(new[] { 0, 50, 100 }, actual.EnumerateArray().Select(marker => marker.GetProperty("position").GetInt32()));
                Assert.Equal(expected[0].GetProperty("position").GetInt32(), actual[ended ? 0 : 2].GetProperty("position").GetInt32());
            }
            else
            {
                var matching = actual[ended ? 0 : 2];
                Assert.Equal(expected[0].GetProperty("slice").GetString(), matching.GetProperty("slice").GetString());
                Assert.Equal(expected[0].GetProperty("border").GetString(), matching.GetProperty("border").GetString());
            }
            await page.Browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "advance" });
            await page.Browser.ScreenshotAsync($"{width}-{theme}-{ended}-winners-actual");
            if (Environment.GetEnvironmentVariable("AETHER_TEST_SCREENSHOTS") is { Length: > 0 } directory)
            {
                var shot = await reference.CommandAsync("Page.captureScreenshot", new { format = "png" });
                await File.WriteAllBytesAsync(Path.Combine(directory, $"{width}-{theme}-{ended}-winners-board.png"), Convert.FromBase64String(shot.GetProperty("data").GetString()!), TestContext.Current.CancellationToken);
            }
        }
        Assert.Empty(page.Browser.Errors);
    }
}
