import {k, out} from './toolkit.mjs';

export function fitText(texts, width) {
  const scale = Math.min(1, width / Math.max(...texts.map(text => text.width)));
  for (const text of texts) text.scale.set(scale);
}
export function letterbox(bars, height) {
  bars[0].clear().rect(0, 0, 1920, height).fill(0x000000);
  bars[1].clear().rect(0, 1080 - height, 1920, height).fill(0x000000);
}
export function sky(P, tex, radial, image, parent, bounds, shadeBounds, tint = 0xffffff) {
  const imageSprite = new P.Sprite(tex(image));
  [imageSprite.width,imageSprite.height] = bounds.slice(2);
  imageSprite.position.set(...bounds.slice(0,2)); imageSprite.tint=tint; parent.addChild(imageSprite);
  const shade = new P.Sprite(radial([[0,'rgba(0,0,0,0)'],[.7,'rgba(0,0,0,.35)'],[1,'rgba(0,0,0,.8)']]));
  shade.anchor.set(.5); shade.position.set(960,540); [shade.width,shade.height]=shadeBounds; parent.addChild(shade);
}
export function pieces(P, cells, parent, drawSky, burst, motion) {
  return cells.map(cell => {
    const c=new P.Container(); let host=c;
    for(const region of cell.regions ?? [cell.poly]) {
      const mask=new P.Graphics().poly(region.flat()).fill(0xffffff),inner=new P.Container();
      c.addChild(mask);inner.mask=mask;host.addChild(inner);host=inner;
    }
    drawSky(host);c.pivot.set(...cell.c);c.position.set(...cell.c);
    const dx=cell.c[0]-burst[0],dy=cell.c[1]-burst[1],length=Math.hypot(dx,dy)||1,dir=[dx/length,dy/length];
    parent.addChild(c);
    return {...cell,c,dir,dist:motion.base+Math.min(motion.max,cell.n*motion.factor),rot:motion.rotation*Math.sign(dir[0]||1)*(dir[1]<0?1:motion.down)};
  });
}
export function revealName(S,l,{start,end,ghostIn,ghostOut,ghostEnd,exit,finish,x=70,y=330,dx=-80}) {
  S.nameMask.clear().rect(-20,-120,(S.nameT.width+40)*out(k(start,end,l)),240).fill(0xffffff);
  S.nameGhosts.forEach((g,i)=>{g.position.set(36*(i+1),-8*(i+1));g.alpha=(i?.22:.45)*k(start,ghostIn,l)*(1-k(ghostOut,ghostEnd,l));});
  S.nameG.alpha=1-k(exit,finish-120,l);if(x!==null && y!==null)S.nameG.position.set(x+dx*k(exit,finish,l)**2,y);
}
export function caption(S,l,{start,revealStart,revealEnd,exitStart,exitEnd,x,y,dx=260,dy=-30,width=400}) {
  const gone=k(exitStart,exitEnd,l);
  S.capMask.clear().rect(20,-90,Math.max(width,S.capName.width+40)*out(k(revealStart,revealEnd,l)),100).fill(0xffffff);
  S.cap.alpha=(l>=start?1:0)*(1-gone);S.cap.position.set(x+dx*gone*gone,y+dy*gone);
}
export function impactFrame(S,first,second,place) {
  S.imp.visible=first||second;
  if(!S.imp.visible)return;
  S.impBg.tint=first?0xffffff:0x000000;
  S.impLines.tint=first?0x000000:0xffffff;
  place();S.silB.visible=first;S.silW.visible=second;
}
export function whiteFlash(sprite,t,start,end,alpha=.85) { sprite.alpha=t>=start&&t<end?alpha:0; }
export function framing(S,t,l,{height,barIn,barOut,dimIn,dimOut}) {
  letterbox(S.bars,height*out(k(...barIn,t))*(1-out(k(...barOut,l))));
  S.dim.alpha=dimIn.reduce((alpha,[amount,start,end])=>alpha+amount*out(k(start,end,l)),0)*(1-k(...dimOut,l));
}
