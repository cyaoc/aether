using System.Text.Json;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
    [Theory]
    [InlineData(1920, 1080, Grade.Grand, "S 级", 0)]
    [InlineData(1920, 1080, Grade.Second, "A 级", 1)]
    [InlineData(1920, 1080, Grade.Third, "B 级", 2)]
    [InlineData(1080, 1920, Grade.Grand, "S 级", 0)]
    [InlineData(1080, 1920, Grade.Second, "A 级", 1)]
    [InlineData(1080, 1920, Grade.Third, "B 级", 2)]
    public async Task Mecha_result_renders_the_matching_emblem(int width, int height, Grade grade, string label, int cell)
    {
        await using var page = await OpenPageAsync(width, height);
        var (h, browser) = (page.H, page.Browser);
        h.Client.Lottery.SelectTheme(LotteryTheme.MechaLock);
        h.Client.Lottery.Start([new("机械键盘", 1, grade)], "停", 42);
        h.Client.Lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync("document.body.style.background = '#f0eae4'; document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); a.currentTime = 4000; }); true");
        var badge = await browser.EvaluateAsync("""
            (() => {
              const image = document.querySelector('#rank [role=img]');
              const rect = image.getBoundingClientRect();
              return {label:image.getAttribute('aria-label'), x:rect.x, y:rect.y, width:rect.width, height:rect.height};
            })()
            """);
        Assert.Equal(label, badge.GetProperty("label").GetString());
        Assert.Equal(width < height ? 88 : 104, badge.GetProperty("width").GetDouble(), 1);
        Assert.Equal(width < height ? 88 : 104, badge.GetProperty("height").GetDouble(), 1);
        var screenshot = await browser.CommandAsync("Page.captureScreenshot", new { format = "png" });
        await browser.ScreenshotAsync($"{width}-MechaLock-{grade}-textures");
        // The correct cell must match better than either other rank; CSS and canvas use different downsampling filters.
        var difference = await browser.EvaluateAsync("""
            (async () => {
              const rect =
            """ + badge.GetRawText() + "; const png = " + JsonSerializer.Serialize(screenshot.GetProperty("data").GetString()) + ";" + """
              const shot = new Image(); shot.src = 'data:image/png;base64,' + png; await shot.decode();
              const atlas = new Image(); atlas.src = '/lottery/textures/mecha/emblems.webp'; await atlas.decode();
              const canvas = document.createElement('canvas'); canvas.width = shot.width; canvas.height = shot.height;
              const ctx = canvas.getContext('2d'); ctx.drawImage(shot,0,0);
              const actual = ctx.getImageData(0,0,canvas.width,canvas.height).data;
              const comparisons = [];
              for (let cell=0;cell<3;cell++) {
                ctx.clearRect(0,0,canvas.width,canvas.height); ctx.imageSmoothingQuality = 'high';
                ctx.drawImage(atlas,cell * atlas.width / 3,0,atlas.width / 3,atlas.height,rect.x,rect.y,rect.width,rect.height);
                const expected = ctx.getImageData(0,0,canvas.width,canvas.height).data;
                let error = 0, count = 0;
                for (let i=0;i<expected.length;i+=4) if (expected[i+3] > 250) {
                  error += Math.abs(actual[i]-expected[i]) + Math.abs(actual[i+1]-expected[i+1]) + Math.abs(actual[i+2]-expected[i+2]);
                  count++;
                }
                comparisons.push({error:error / (count * 3),count});
              }
              return comparisons;
            })()
            """);
        Assert.True(difference[cell].GetProperty("count").GetInt32() > 300, "Expected enough opaque emblem pixels to compare.");
        var expectedError = difference[cell].GetProperty("error").GetDouble();
        Assert.All(Enumerable.Range(0, 3).Where(other => other != cell), other =>
            Assert.True(expectedError + 5 < difference[other].GetProperty("error").GetDouble(), $"The rendered {label} emblem does not clearly match its atlas cell: {difference}."));
        Assert.Empty(browser.Errors);
    }
}
