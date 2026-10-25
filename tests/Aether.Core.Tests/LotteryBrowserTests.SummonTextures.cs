using System.Text.Json;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
    [Theory]
    [InlineData(1920, 1080, Grade.Grand, "ssr", .3, .2, .62)]
    [InlineData(1920, 1080, Grade.Second, "sr", .25, .15, .6)]
    [InlineData(1920, 1080, Grade.Third, "r", .2, .1, .55)]
    [InlineData(1080, 1920, Grade.Grand, "ssr", .3, .2, .62)]
    [InlineData(1080, 1920, Grade.Second, "sr", .25, .15, .6)]
    [InlineData(1080, 1920, Grade.Third, "r", .2, .1, .55)]
    public async Task Summon_reveal_paints_the_grade_illustration_beneath_readable_text(
        int width, int height, Grade grade, string face, double top, double middle, double bottom)
    {
        await using var page = await OpenPageAsync(width, height);
        var (h, browser) = (page.H, page.Browser);
        h.Client.Lottery.Start([new("星辰召唤特别纪念限量联名豪华版无线机械键盘和配套主题礼盒", 1, grade)], "停", 42);
        h.Client.Lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await browser.EvaluateAsync("textures.ready");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync("document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); a.currentTime=4000; }); true");
        var screenshot = await browser.CommandAsync("Page.captureScreenshot", new { format = "png" });
        // Stay clear of the frame's inner glow when comparing the illustration and dark gradient.
        var difference = await browser.EvaluateAsync("""
            (async () => {
              const [png, face, stops] =
            """ + JsonSerializer.Serialize(new object[] { screenshot.GetProperty("data").GetString()!, face, new[] { top, middle, bottom } }) + """
              ;
              const actual = new Image(), art = new Image();
              actual.src='data:image/png;base64,'+png; art.src='/lottery/textures/summon/face-'+face+'.webp';
              await Promise.all([actual.decode(),art.decode()]);
              const canvas=document.createElement('canvas'); canvas.width=actual.width; canvas.height=actual.height;
              const ctx=canvas.getContext('2d'); ctx.drawImage(actual,0,0);
              const card=document.querySelector('.cardFace').getBoundingClientRect(), w=card.width-12, h=card.height-12;
              const expected=document.createElement('canvas'); expected.width=w; expected.height=h;
              const ref=expected.getContext('2d'), scale=Math.max(w/art.width,h/art.height);
              ref.drawImage(art,(w-art.width*scale)/2,(h-art.height*scale)/2,art.width*scale,art.height*scale);
              const shade=ref.createLinearGradient(0,0,0,h);
              [0,.4,1].forEach((stop,i)=>shade.addColorStop(stop,`rgba(10,8,30,${stops[i]})`));
              ref.fillStyle=shade; ref.fillRect(0,0,w,h);
              let error=0, samples=0;
              for (const x of [56,64,72]) for (const y of [160,168,176]) {
                const a=ctx.getImageData(Math.round(card.left+6+x),Math.round(card.top+6+y),1,1).data;
                const b=ref.getImageData(x,y,1,1).data;
                for(let i=0;i<3;i++){error+=Math.abs(a[i]-b[i]);samples++;}
              }
              return error/samples;
            })()
            """);
        Assert.True(difference.GetDouble() < 4, $"{width} {grade}: illustration pixel error {difference}.");
        await AssertLayoutAsync(browser);
        await browser.ScreenshotAsync($"{width}-{grade}-textured-card");
        Assert.Empty(browser.Errors);
    }
}
