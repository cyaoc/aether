using Aether.Core;
using Aether.Core.Tests.Support;

namespace Aether.Cli.Tests;

// One at a time: sound cues drop when software rendering falls behind real time.
[Collection("Animation in Chrome")]
public sealed class EntryAnimationMoveBrowserTests
{
    [Theory]
    [InlineData("30", 6800, 1950, 2760, 6300)]
    [InlineData("40", 7550, 1450, 2987, 7050)]
    [InlineData("50", 8480, 1590, 4193, 7980)]
    [InlineData("60", 9064, 1450, 6274, 8564)]
    public async Task Move_finishes_after_all_sound_cues_and_clears_the_stage(string tier, int duration, int capEnd, int nameStart, int nameEnd)
    {
        await using var h = new WatchHarness();
        await using var browser = await ChromeBrowser.OpenVideoPageAsync(h);
        await browser.CommandAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x = 10, y = 10, button = "left", clickCount = 1 });
        await browser.CommandAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x = 10, y = 10, button = "left", clickCount = 1 });
        await browser.WatchFramesAsync($"general-{tier}", $$"""
            if(t>=860 && t<={{capEnd}})check(scene.S.capName,'caption',t);
            if(t>={{nameStart}} && t<={{nameEnd}})check(scene.S.nameT,'name',t);
            window.tail={t,hero:scene.S.hero.alpha,dim:scene.S.dim.alpha,bar:scene.S.bars[0].height};
            """, $$"""
            window.fitFailures=[];
            window.check=(text,label,t)=>{
              const bounds=text.getBounds();
              if(bounds.minX<0 || bounds.maxX>1920 || text.text!==context.nickname)
                fitFailures.push({label,t,left:bounds.minX,right:bounds.maxX,text:text.text});
            };
            for(const t of [860,1000,{{capEnd}},{{nameStart}},{{nameEnd}}])scene.frame(t);
            """);
        await browser.EvaluateAsync($$"""
            (async()=>{
              window.cues=await (await fetch('/video/audio/events.json')).json();
              window.started=0;
              const start=AudioBufferSourceNode.prototype.start;
              AudioBufferSourceNode.prototype.start=function(...args){started++;return start.apply(this,args);};
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
