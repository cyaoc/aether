using Aether.Core;
using Aether.Core.Tests.Support;

namespace Aether.Cli.Tests;

public sealed class EntryAnimationBrowserTests
{
    [Fact]
    public async Task Animation_finishes_and_an_unknown_animation_does_not_break_the_next_play()
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
        await browser.EvaluateAsync("(async()=>{window.animationModule = await import('/video/animations/runtime.mjs'); await animationModule.preload();return true;})()");
        await browser.EvaluateAsync("(async()=>{const {registry}=await import('/video/animations/registry.mjs');const create=registry['general-30'].create;registry['general-30'].create=context=>{const scene=create(context),frame=scene.frame.bind(scene);scene.frame=t=>{frame(t);window.tail={t,hero:scene.S.hero.alpha,dim:scene.S.dim.alpha,bar:scene.S.bars[0].height};};return scene;};return true;})()");
        await browser.EvaluateAsync("(async()=>{window.completed=[]; window.abort=new AbortController(); await animationModule.play({animation:'general-30',nickname:'さくら月影',volume:0,abort},document.querySelector('canvas'),(success,reason)=>completed.push({success,reason}));return true;})()");
        await browser.WaitAsync("completed.length===1", 15);
        Assert.True((await browser.EvaluateAsync("completed[0].success")).GetBoolean(), (await browser.EvaluateAsync("JSON.stringify(completed)")).GetString());
        Assert.True((await browser.EvaluateAsync("tail.t===6800 && tail.hero===0 && tail.dim===0 && tail.bar===0")).GetBoolean());
        Assert.Equal("hidden", (await browser.EvaluateAsync("document.querySelector('canvas').style.visibility")).GetString());
        await browser.EvaluateAsync("(async()=>{try { await animationModule.play({animation:'unknown',abort},document.querySelector('canvas'),()=>{}); } catch(e) { window.unknown=e.message; };return true;})()");
        Assert.Contains("未注册", (await browser.EvaluateAsync("unknown")).GetString());
        await browser.CommandAsync("Emulation.setDeviceMetricsOverride", new {width=1080,height=1920,deviceScaleFactor=1,mobile=false});
        await browser.EvaluateAsync("(async()=>{const {registry}=await import('/video/animations/registry.mjs');const create=registry['general-30'].create;registry['general-30'].create=context=>(window.scene=create(context));return true;})()");
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
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real animation browser acceptance.");
        await using var h=new WatchHarness();
        Settings.Save(h.DataDirectory,new Dictionary<string,string> {
            ["web_service.port"]=Aether.Core.Tests.WelcomeAnimationTests.FreePort().ToString(),
            ["entry_animation.volume"]="23", ["entry_animation_viewers.10001"]="welcome_danmaku.mp4"
        });
        Directory.CreateDirectory(Settings.VideoDirectory(h.DataDirectory));
        File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","welcome.mp4"),Path.Combine(Settings.VideoDirectory(h.DataDirectory),"welcome_danmaku.mp4"));
        await h.Client.ApplyWebSettingsAsync(Settings.Load(h.DataDirectory));
        await using var browser=new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory,h.Client.WebService.Status.VideoAddress);
        await h.WaitForLogAsync("视频页 1 个推流软件");
        if (frameFailure)
            await browser.EvaluateAsync("(async()=>{const {registry}=await import('/video/animations/registry.mjs');registry['general-30'].create=()=>({frame(){throw new Error('frame failed')}});return true;})()");
        else await browser.EvaluateAsync("window.originalContext=HTMLCanvasElement.prototype.getContext;HTMLCanvasElement.prototype.getContext=function(type,...args){return type.startsWith('webgl')?null:originalContext.call(this,type,...args);};true");
        var source=new PlaybackSource(h.Client.WebService,h.Time,h.Logger,"进场动画",true);
        await using var updates=h.Client.WebService.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        source.TestPlay(new PlaybackContent.Animation("general-30","测试昵称"));
        do { Assert.True(await updates.MoveNextAsync()); }
        while(updates.Current is not SourcePlayback {State:PlaybackState.Failed});
        Assert.Equal(frameFailure ? "frame failed" : "推流软件不支持 WebGL",((SourcePlayback)updates.Current).Reason);
        await browser.EvaluateAsync("if(window.originalContext)HTMLCanvasElement.prototype.getContext=originalContext;document.querySelector('video').muted=true;document.querySelector('video').loop=true;true");
        h.Client.WelcomeAnimation.TestPlay(10001);
        await browser.WaitAsync("document.querySelector('video').style.visibility==='visible'");
        Assert.Equal(.23,(await browser.EvaluateAsync("document.querySelector('video').volume")).GetDouble());
        Assert.Empty(browser.Errors);
    }
}
