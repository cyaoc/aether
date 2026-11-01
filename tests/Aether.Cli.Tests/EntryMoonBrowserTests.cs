using Aether.Core;
using Aether.Core.Tests.Support;

namespace Aether.Cli.Tests;

public sealed class EntryMoonBrowserTests
{
    [Fact]
    public async Task Moon_finishes_after_the_world_shatters_and_the_last_bars_leave()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real animation browser acceptance.");
        await using var h = new WatchHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string,string> {
            ["web_service.port"] = Aether.Core.Tests.WelcomeAnimationTests.FreePort().ToString()
        });
        await h.Client.ApplyWebSettingsAsync(Settings.Load(h.DataDirectory));
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory,h.Client.WebService.Status.VideoAddress);
        await browser.EvaluateAsync("(async()=>{window.animationModule=await import('/video/animations/runtime.mjs');await animationModule.preload();const {registry}=await import('/video/animations/registry.mjs');const create=registry['general-60'].create;registry['general-60'].create=context=>{const scene=create(context),frame=scene.frame.bind(scene);scene.frame=t=>{frame(t);window.tail={t,hero:scene.S.hero.alpha,day:scene.S.dayLayer.alpha,bar:scene.S.bars[0].height,night:scene.S.base.visible,whole:scene.S.redWhole.visible};};return scene;};return true;})()");
        await browser.CommandAsync("Runtime.evaluate", new {expression="(async()=>{const {ctx,cues}=await animationModule.preload();window.soundCues=cues['general-60'];window.soundStarts=[];const create=ctx.createBufferSource.bind(ctx);ctx.createBufferSource=()=>{const source=create(),start=source.start.bind(source);source.start=(...args)=>{soundStarts.push(source);start(...args);};return source;};await ctx.resume();return true;})()",awaitPromise=true,userGesture=true});
        await h.WaitForLogAsync("视频页 1 个推流软件");
        var source=new PlaybackSource(h.Client.WebService,h.Time,h.Logger,"进场动画",true);
        await using var updates=h.Client.WebService.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        source.TestPlay(new PlaybackContent.Animation("general-60","柚子不酸"));
        do { Assert.True(await updates.MoveNextAsync()); }
        while(updates.Current is not SourcePlayback {State:PlaybackState.Completed or PlaybackState.Failed});
        Assert.Equal(PlaybackState.Completed,((SourcePlayback)updates.Current).State);
        Assert.True((await browser.EvaluateAsync("tail.t===9064 && tail.hero===0 && tail.day===0 && tail.bar===0 && !tail.night && !tail.whole")).GetBoolean(),(await browser.EvaluateAsync("JSON.stringify(tail)")).GetString());
        Assert.Equal("hidden",(await browser.EvaluateAsync("document.querySelector('canvas').style.visibility")).GetString());
        Assert.True((await browser.EvaluateAsync("soundStarts.length===soundCues.length && soundCues.every(c=>soundStarts.some(s=>s.buffer===c.buffer))")).GetBoolean(),(await browser.EvaluateAsync("JSON.stringify({started:soundStarts.length,expected:soundCues.length})")).GetString());
        Assert.Empty(browser.Errors);
    }
}
