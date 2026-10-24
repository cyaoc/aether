using System.Text.Json;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
    [Theory]
    [InlineData(LotteryTheme.CapsuleGacha, 1920, 1080, 1000, 600)]
    [InlineData(LotteryTheme.CapsuleGacha, 1080, 1920, 780, 920)]
    [InlineData(LotteryTheme.StarSummon, 1920, 1080, 944, 504)]
    [InlineData(LotteryTheme.StarSummon, 1080, 1920, 524, 944)]
    public async Task Opening_core_hides_the_stream_pattern_at_two_hundred_milliseconds(LotteryTheme theme, int width, int height, int x, int y)
    {
        await using var page = await OpenPageAsync(width, height);
        var browser = page.Browser;
        page.H.Client.Lottery.SelectTheme(theme);
        page.H.Client.Lottery.Start([new("机械键盘", 1, Grade.Grand)], "停", 42);
        page.H.Client.Lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync("document.querySelector('.stage').style.visibility='hidden'; document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); a.currentTime=200; }); true");
        var frames = new List<string>();
        foreach (var colors in new[] { "#fff 0% 25%, #000 0% 50%", "#000 0% 25%, #fff 0% 50%" })
        {
            await browser.EvaluateAsync($"document.body.style.background = 'repeating-conic-gradient({colors}) 0 0 / 16px 16px'; true");
            var png = await browser.CommandAsync("Page.captureScreenshot", new { format = "png" });
            frames.Add(png.GetProperty("data").GetString()!);
        }
        // These patches are inside the core but outside the machine and text, so opaque props cannot hide a fading core.
        var contrast = await browser.EvaluateAsync("""
            (async () => {
              const samples = [];
              for (const png of
            """ + JsonSerializer.Serialize(frames) + """
              ) {
                const image = new Image(); image.src='data:image/png;base64,'+png; await image.decode();
                const canvas=document.createElement('canvas'); canvas.width=image.width; canvas.height=image.height;
                const ctx=canvas.getContext('2d'); ctx.drawImage(image,0,0);
                samples.push(ctx.getImageData(
            """ + $"{x},{y},32,32" + """
                ).data);
              }
              let difference=0;
              for(let i=0;i<samples[0].length;i++) if(i%4!==3) difference+=Math.abs(samples[0][i]-samples[1][i]);
              return difference/(32*32*3);
            })()
            """);
        Assert.InRange(contrast.GetDouble(), 0, 35);
        await browser.ScreenshotAsync($"{width}-opaque-core-200");
        Assert.Empty(browser.Errors);
    }
}
