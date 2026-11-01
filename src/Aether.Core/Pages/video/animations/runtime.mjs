import {registry} from './registry.mjs';
import {AudioTrack, prepareCues} from './audio.mjs';

const root = '/video/';
const image = src => new Promise((resolve,reject) => {
  const img=new Image();img.onload=()=>resolve(img);img.onerror=()=>reject(new Error('动画贴图加载失败：'+src));img.src=src;
});
const script = src => new Promise((resolve,reject)=>{
  const tag=document.createElement('script');tag.src=src;tag.onload=resolve;tag.onerror=()=>reject(new Error('动画脚本加载失败'));document.head.append(tag);
});
let ready, application;
export function preload() {
  return ready ??= (async()=>{
    await script(root+'lib/pixi.js');await script(root+'lib/pixi-filters.js');
    const ctx=new AudioContext({latencyHint:'interactive'});
    const entries=Object.entries(registry),images={},cues={};
    for(const character of new Set(entries.map(([,entry])=>entry.character))) {
      await Promise.all(Object.entries(character.textures).map(async([key,path])=>images[key]=await image(root+path)));
      const response=await fetch(root+character.audio);
      if(!response.ok)throw new Error('动画声音清单加载失败');
      const events=await response.json(),buffers={};
      await Promise.all([...new Set(Object.values(events).flat().map(cue=>cue.file))].map(async file=>{
        const response=await fetch(root+'audio/'+encodeURIComponent(file));
        if(!response.ok)throw new Error('动画声音加载失败');
        buffers[file]=await ctx.decodeAudioData(await response.arrayBuffer());
      }));
      for(const [id,entry] of entries)
        if(entry.character===character)cues[id]=prepareCues(events[entry.audio],buffers);
    }
    await document.fonts.load('132px "LXGW WenKai"');
    return {ctx,images,cues};
  })();
}

export async function play(item,canvas,done) {
  const entry=registry[item.animation];if(!entry)throw new Error('未注册的动画：'+item.animation);
  const probe=document.createElement('canvas');
  const gl=probe.getContext('webgl2')||probe.getContext('webgl');
  if(!gl)throw new Error('推流软件不支持 WebGL');
  gl.getExtension('WEBGL_lose_context')?.loseContext();
  const {ctx,images,cues}=await preload();
  if(item.abort.signal.aborted)return;
  application ??= (async()=>{
    const app=new PIXI.Application();
    await app.init({canvas,width:1920,height:1080,backgroundAlpha:0,antialias:true,preference:'webgl',resolution:1,autoStart:false});
    return app;
  })();
  const app=await application;
  let track,frame,move,stopped=false;
  const stop=()=>{if(stopped)return;stopped=true;cancelAnimationFrame(frame);track?.stop();move?.destroy?.();for(const child of app.stage.removeChildren()) child.destroy({children:true});app.render();canvas.style.visibility='hidden';};
  item.abort.signal.addEventListener('abort',stop,{once:true});
  if(item.abort.signal.aborted){stop();return;}
  try {
    move=entry.create({app,canvas,images:entry.images.map(key=>images[key]),character:entry.character,nickname:item.nickname});
    track=new AudioTrack(ctx,cues[item.animation],item.volume,entry.duration/1000);
    ctx.resume().catch(()=>{});
    const start=performance.now();canvas.style.visibility='visible';
    const tick=now=>{
      if(stopped)return;
      try {
        const t=Math.min(now-start,entry.duration);move.frame(t);track.frame(t);app.render();
        if(t>=entry.duration){stop();done(true);return;}
        frame=requestAnimationFrame(tick);
      } catch(error){stop();done(false,String(error.message));}
    };
    tick(start);
  } catch(error){stop();throw error;}
}
