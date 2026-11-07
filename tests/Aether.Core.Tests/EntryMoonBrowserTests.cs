using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

// One at a time: sound cues drop when software rendering falls behind real time.
[Collection("Animation in Chrome")]
public sealed class EntryMoonBrowserTests
{
    [Fact]
    public async Task Moon_finishes_after_the_world_shatters_and_the_last_bars_leave()
    {
        await using var h = new WatchHarness();
        await using var browser = await ChromeBrowser.OpenVideoPageAsync(h);
        await browser.WatchFramesAsync("general-60", "window.tail={t,hero:scene.S.hero.alpha,day:scene.S.dayLayer.alpha,bar:scene.S.bars[0].height,night:scene.S.base.visible,whole:scene.S.redWhole.visible};");
        await browser.CommandAsync("Runtime.evaluate", new {expression="(async()=>{const {ctx,cues}=await animationModule.preload();window.soundCues=cues['general-60'];window.soundStarts=[];const create=ctx.createBufferSource.bind(ctx);ctx.createBufferSource=()=>{const source=create(),start=source.start.bind(source);source.start=(...args)=>{soundStarts.push(source);start(...args);};return source;};await ctx.resume();return true;})()",awaitPromise=true,userGesture=true});
        await h.WaitForLogAsync("视频页 1 个推流软件");
        await using var updates=h.Client.WebService.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        h.Client.EntryAnimation.TestPlay(EntryTier.Parse(60)!.Value,"柚子不酸");
        do { Assert.True(await updates.MoveNextAsync()); }
        while(updates.Current is not SourcePlayback {State:PlaybackState.Completed or PlaybackState.Failed});
        Assert.Equal(PlaybackState.Completed,((SourcePlayback)updates.Current).State);
        Assert.True((await browser.EvaluateAsync("tail.t===9064 && tail.hero===0 && tail.day===0 && tail.bar===0 && !tail.night && !tail.whole")).GetBoolean(),(await browser.EvaluateAsync("JSON.stringify(tail)")).GetString());
        Assert.Equal("hidden",(await browser.EvaluateAsync("document.querySelector('canvas').style.visibility")).GetString());
        Assert.True((await browser.EvaluateAsync("soundStarts.length===soundCues.length && soundCues.every(c=>soundStarts.some(s=>s.buffer===c.buffer))")).GetBoolean(),(await browser.EvaluateAsync("JSON.stringify({started:soundStarts.length,expected:soundCues.length})")).GetString());
        Assert.Empty(browser.Errors);
    }
}
