using Aether.Core;
using Aether.Core.Tests.Support;

namespace Aether.Cli.Tests;

public sealed class ComboAnimationBrowserTests
{
    [Theory]
    [InlineData("30", 6800, 1950, 2760, 6300)]
    [InlineData("40", 7550, 1450, 2987, 7050)]
    [InlineData("50", 8480, 1590, 4193, 7980)]
    [InlineData("60", 9064, 1450, 6274, 8564)]
    public async Task Combo_finishes_after_all_sound_cues_and_clears_the_stage(string tier, int duration, int capEnd, int nameStart, int nameEnd)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real animation browser acceptance.");
        await using var h = new WatchHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> {
            ["web_service.port"] = Aether.Core.Tests.WelcomeAnimationTests.FreePort().ToString()
        });
        await h.Client.ApplyWebSettingsAsync(Settings.Load(h.DataDirectory));
        await using var browser = new ChromeBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.VideoAddress);
        await browser.EvaluateAsync("(async()=>{window.animationModule=await import('/video/animations/runtime.mjs');await animationModule.preload();window.cues=await (await fetch('/video/audio/events.json')).json();return true;})()");
        await browser.CommandAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x = 10, y = 10, button = "left", clickCount = 1 });
        await browser.CommandAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x = 10, y = 10, button = "left", clickCount = 1 });
        await browser.EvaluateAsync($$"""
            (async()=>{
              window.started=0;
              const start=AudioBufferSourceNode.prototype.start;
              AudioBufferSourceNode.prototype.start=function(...args){started++;return start.apply(this,args);};
              const {registry}=await import('/video/animations/registry.mjs');
              const entry=registry['general-{{tier}}'],create=entry.create;
              entry.create=context=>{
                const scene=create(context),frame=scene.frame.bind(scene);
                window.fitFailures=[];
                const capEnd={{capEnd}},nameStart={{nameStart}},nameEnd={{nameEnd}};
                const check=(text,label,t)=>{
                  const bounds=text.getBounds();
                  if(bounds.minX<0 || bounds.maxX>1920 || text.text!==context.nickname)
                    fitFailures.push({label,t,left:bounds.minX,right:bounds.maxX,text:text.text});
                };
                scene.frame=t=>{
                  frame(t);
                  if(t>=860 && t<=capEnd)check(scene.S.capName,'caption',t);
                  if(t>=nameStart && t<=nameEnd)check(scene.S.nameT,'name',t);
                  window.tail={t,hero:scene.S.hero.alpha,dim:scene.S.dim.alpha,bar:scene.S.bars[0].height};
                };
                for(const t of [860,1000,capEnd,nameStart,nameEnd])scene.frame(t);
                return scene;
              };
              window.completed=[];window.abort=new AbortController();
              await animationModule.play({animation:'general-{{tier}}',nickname:'长昵称中文Englishかな𰻞繁體測試',volume:0,abort},document.querySelector('canvas'),(success,reason)=>completed.push({success,reason}));
              return true;
            })()
            """);
        await browser.WaitAsync("completed.length===1", 20);
        Assert.True((await browser.EvaluateAsync("completed[0].success")).GetBoolean(),
            (await browser.EvaluateAsync("JSON.stringify(completed)")).GetString());
        Assert.True((await browser.EvaluateAsync($"tail.t==={duration} && tail.hero===0 && tail.dim===0 && tail.bar===0")).GetBoolean());
        Assert.Equal((await browser.EvaluateAsync($"cues['{tier}'].length")).GetInt32(), (await browser.EvaluateAsync("started")).GetInt32());
        Assert.True((await browser.EvaluateAsync("fitFailures.length===0")).GetBoolean(),
            (await browser.EvaluateAsync("JSON.stringify(fitFailures.slice(0,3))")).GetString());
        Assert.Equal("hidden", (await browser.EvaluateAsync("document.querySelector('canvas').style.visibility")).GetString());
        Assert.Empty(browser.Errors);
    }
}
