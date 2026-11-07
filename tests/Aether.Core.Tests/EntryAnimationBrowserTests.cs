using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

// One at a time: sound cues drop when software rendering falls behind real time.
[Collection("Animation in Chrome")]
public sealed class EntryAnimationBrowserTests
{
    [Fact]
    public async Task Animation_finishes_and_an_unknown_animation_does_not_break_the_next_play()
    {
        await using var h = new WatchHarness();
        await using var browser = await ChromeBrowser.OpenVideoPageAsync(h);
        await browser.WatchFramesAsync("general-30", "window.tail={t,hero:scene.S.hero.alpha,dim:scene.S.dim.alpha,bar:scene.S.bars[0].height};");
        await browser.EvaluateAsync("(async()=>{window.completed=[]; window.abort=new AbortController(); await animationModule.play({animation:'general-30',nickname:'さくら月影',volume:0,abort},document.querySelector('canvas'),(success,reason)=>completed.push({success,reason}));return true;})()");
        await browser.WaitAsync("completed.length===1", 15);
        Assert.True((await browser.EvaluateAsync("completed[0].success")).GetBoolean(), (await browser.EvaluateAsync("JSON.stringify(completed)")).GetString());
        Assert.True((await browser.EvaluateAsync("tail.t===6800 && tail.hero===0 && tail.dim===0 && tail.bar===0")).GetBoolean());
        Assert.Equal("hidden", (await browser.EvaluateAsync("document.querySelector('canvas').style.visibility")).GetString());
        await browser.EvaluateAsync("(async()=>{try { await animationModule.play({animation:'unknown',abort},document.querySelector('canvas'),()=>{}); } catch(e) { window.unknown=e.message; };return true;})()");
        Assert.Contains("未注册", (await browser.EvaluateAsync("unknown")).GetString());
        await browser.CommandAsync("Emulation.setDeviceMetricsOverride", new {width=1080,height=1920,deviceScaleFactor=1,mobile=false});
        await browser.WatchFramesAsync("general-30", "", "window.scene=scene;");
        await browser.EvaluateAsync("(async()=>{window.abort=new AbortController(); await animationModule.play({animation:'general-30',nickname:'长昵称中文Englishかな𰻞繁體測試',volume:0,abort},document.querySelector('canvas'),()=>{}); return true;})()");
        Assert.True((await browser.EvaluateAsync("scene.S.nameT.text==='长昵称中文Englishかな𰻞繁體測試' && scene.S.nameT.width<=1101 && scene.S.capName.width<=821")).GetBoolean());
        Assert.True((await browser.EvaluateAsync("(()=>{const r=document.querySelector('canvas').getBoundingClientRect();return Math.abs(r.width/r.height-16/9)<.001 && Math.abs(r.y-(1920-r.height)/2)<1;})()")).GetBoolean());
        await browser.EvaluateAsync("abort.abort();true");
        Assert.Equal("hidden", (await browser.EvaluateAsync("document.querySelector('canvas').style.visibility")).GetString());
        Assert.Empty(browser.Errors);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Animation_failure_does_not_break_video_playback(bool frameFailure)
    {
        await using var h=new WatchHarness();
        Directory.CreateDirectory(Settings.VideoDirectory(h.DataDirectory));
        File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","welcome.mp4"),Path.Combine(Settings.VideoDirectory(h.DataDirectory),"welcome.mp4"));
        await using var browser=await ChromeBrowser.OpenVideoPageAsync(h,new Dictionary<string,string> {
            ["entry_animation.volume"]="23", ["entry_animation_viewers.10001"]="welcome.mp4"
        });
        await h.WaitForLogAsync("视频页 1 个推流软件");
        if (frameFailure)
            await browser.EvaluateAsync("(async()=>{const {registry}=await import('/video/animations/registry.mjs');registry['general-30'].create=()=>({frame(){throw new Error('frame failed')}});return true;})()");
        else await browser.EvaluateAsync("window.originalContext=HTMLCanvasElement.prototype.getContext;HTMLCanvasElement.prototype.getContext=function(type,...args){return type.startsWith('webgl')?null:originalContext.call(this,type,...args);};true");
        await using var updates=h.Client.WebService.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        h.Client.EntryAnimation.TestPlay(EntryTier.Parse(30)!.Value,"测试昵称");
        do { Assert.True(await updates.MoveNextAsync()); }
        while(updates.Current is not SourcePlayback {State:PlaybackState.Failed});
        Assert.Equal(frameFailure ? "frame failed" : "推流软件不支持 WebGL",((SourcePlayback)updates.Current).Reason);
        await browser.EvaluateAsync("if(window.originalContext)HTMLCanvasElement.prototype.getContext=originalContext;document.querySelector('video').muted=true;document.querySelector('video').loop=true;true");
        h.Client.WelcomeAnimation.TestPlay(10001);
        await browser.WaitAsync("document.querySelector('video').style.visibility==='visible'");
        Assert.Equal(.23,(await browser.EvaluateAsync("document.querySelector('video').volume")).GetDouble());
        Assert.Empty(browser.Errors);
    }

    [Fact]
    public async Task A_failed_load_fails_only_that_play()
    {
        await using var h=new WatchHarness();
        await using var browser=await ChromeBrowser.OpenVideoPageAsync(h);
        await browser.CommandAsync("Page.addScriptToEvaluateOnNewDocument",new {source="""
            window.failLoad=true;
            const realFetch=window.fetch;
            window.fetch=(url,...rest)=>failLoad&&String(url).endsWith('events.json')?Promise.resolve(new Response('',{status:500})):realFetch(url,...rest);
            """});
        await browser.CommandAsync("Page.reload");
        await browser.WaitAsync("document.readyState==='complete' && window.failLoad");
        await browser.EvaluateAsync("""
            (async()=>{
              window.animationModule=await import('/video/animations/runtime.mjs');
              window.abort=new AbortController();
              await animationModule.play({animation:'general-30',nickname:'昵称',volume:0,abort},document.querySelector('canvas'),()=>{}).catch(error=>{window.failed=error.message;});
              window.failLoad=false;
              await animationModule.play({animation:'general-30',nickname:'昵称',volume:0,abort},document.querySelector('canvas'),()=>{});
              return true;
            })()
            """);
        Assert.Equal("动画声音清单加载失败",(await browser.EvaluateAsync("failed")).GetString());
        Assert.Equal("visible",(await browser.EvaluateAsync("document.querySelector('canvas').style.visibility")).GetString());
        await browser.EvaluateAsync("abort.abort();true");
    }
}
