using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed partial class LotteryBrowserTests
{
    [Theory]
    [InlineData(1920, 1080, Grade.Grand, 600)]
    [InlineData(1920, 1080, Grade.Second, 420)]
    [InlineData(1920, 1080, Grade.Third, 0)]
    [InlineData(1080, 1920, Grade.Grand, 480)]
    [InlineData(1080, 1920, Grade.Second, 360)]
    [InlineData(1080, 1920, Grade.Third, 0)]
    public async Task Summon_comet_keeps_its_head_and_shadow_on_a_light_room(int width, int height, Grade grade, int cometWidth)
    {
        await using var page = await OpenPageAsync(width, height);
        var browser = page.Browser;
        page.H.Client.Lottery.SelectTheme(LotteryTheme.StarSummon);
        page.H.Client.Lottery.Start([new("星辰键盘", 1, grade)], "停", 42);
        page.H.Client.Lottery.Stop();
        await browser.WaitAsync("document.getElementById('scene').dataset.view === 'drawing'");
        await browser.CommandAsync("Emulation.setVirtualTimePolicy", new { policy = "pause" });
        await browser.EvaluateAsync("document.body.style.background='#e9e5de'; document.getAnimations().forEach(a => { a.pause(); a.effect.updateTiming({delay:0}); a.currentTime=1850; }); true");
        if (cometWidth == 0)
            Assert.Equal("none", (await browser.EvaluateAsync("getComputedStyle(document.querySelector('.meteor')).display")).GetString());
        else
        {
            var geometry = await browser.EvaluateAsync("(() => { const el=document.querySelector('.meteor'), face=getComputedStyle(el,'::after'), shadow=getComputedStyle(el,'::before'); return {width:parseFloat(face.width),height:parseFloat(face.height),origin:face.transformOrigin,mask:face.webkitMaskImage,shadow:shadow.backgroundColor}; })()");
            Assert.Equal(cometWidth, geometry.GetProperty("width").GetDouble());
            Assert.Equal(cometWidth / 3d, geometry.GetProperty("height").GetDouble());
            Assert.Equal($"{cometWidth / 10}px {cometWidth / 6}px", geometry.GetProperty("origin").GetString());
            Assert.Contains("summon/comet.webp", geometry.GetProperty("mask").GetString());
            Assert.Equal("rgba(28, 14, 70, 0.42)", geometry.GetProperty("shadow").GetString());
        }
        await browser.ScreenshotAsync($"{width}-{grade}-comet-light");
        Assert.Empty(browser.Errors);
    }
}
