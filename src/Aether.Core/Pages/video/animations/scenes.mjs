import {k, out, inq} from './toolkit.mjs';

export function createStage(P, app, {radial}, [dimWidth, dimHeight]) {
  const world = new P.Container(), ui = new P.Container(); app.stage.addChild(world, ui);
  const dim = new P.Sprite(radial([[0, 'rgba(6,6,12,0.55)'], [0.6, 'rgba(4,4,8,0.85)'], [1, 'rgba(0,0,0,1)']]));
  dim.anchor.set(.5); dim.position.set(960, 540); dim.width = dimWidth; dim.height = dimHeight; world.addChild(dim);
  return {world, ui, dim};
}
// An additive canvas in the world, drawn by hand each frame.
export function createFx(P, {tex}, world, width, height, x = 0, y = 0) {
  const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height;
  const texture = tex(canvas), sprite = new P.Sprite(texture); sprite.blendMode = 'add'; sprite.position.set(x, y); world.addChild(sprite);
  return {g: canvas.getContext('2d'), sprite, update: () => texture.source.update()};
}
export function createFigure(P, {radial}, world, palette, pose, ghostCount) {
  const rim = new P.Sprite(radial(palette.rimStops));
  rim.anchor.set(.5); rim.width = 1100; rim.height = 1100; world.addChild(rim);
  const ghosts = Array.from({length: ghostCount}, () => { const s = new P.Sprite(pose); s.alpha = 0; world.addChild(s); return s; });
  const hero = new P.Sprite(pose); hero.tint = 0xe0e0e0; world.addChild(hero);
  return {rim, ghosts, hero};
}
// The impact frames and the white flash; the move adds its own silhouettes to imp.
export function createStrike(P, {tex}, world, impact, bounds, lineSize) {
  const imp = new P.Container(); world.addChild(imp);
  const impBg = new P.Graphics().rect(...bounds).fill(0xffffff); imp.addChild(impBg);
  const impLines = new P.Sprite(tex(impact)); impLines.anchor.set(.5); impLines.width = impLines.height = lineSize; imp.addChild(impLines);
  const flash = new P.Graphics().rect(...bounds).fill(0xffffff); world.addChild(flash);
  return {imp, impBg, impLines, flash};
}

function fitText(texts, width) {
  const scale = Math.min(1, width / Math.max(...texts.map(text => text.width)));
  for (const text of texts) text.scale.set(scale);
}
function letterbox(bars, height) {
  bars[0].clear().rect(0, 0, 1920, height).fill(0x000000);
  bars[1].clear().rect(0, 1080 - height, 1920, height).fill(0x000000);
}
export function sky(P, {tex, radial}, image, parent, bounds, shadeBounds) {
  const imageSprite = new P.Sprite(tex(image));
  [imageSprite.width, imageSprite.height] = bounds.slice(2);
  imageSprite.position.set(...bounds.slice(0, 2)); parent.addChild(imageSprite);
  const shade = new P.Sprite(radial([[0, 'rgba(0,0,0,0)'], [.7, 'rgba(0,0,0,.35)'], [1, 'rgba(0,0,0,.8)']]));
  shade.anchor.set(.5); shade.position.set(960, 540); [shade.width, shade.height] = shadeBounds; parent.addChild(shade);
}
export function createPieces(P, cells, parent, drawSky, burst, motion) {
  return cells.map(cell => {
    const c = new P.Container(); let host = c;
    for (const region of cell.regions ?? [cell.poly]) {
      const mask = new P.Graphics().poly(region.flat()).fill(0xffffff), inner = new P.Container();
      c.addChild(mask); inner.mask = mask; host.addChild(inner); host = inner;
    }
    drawSky(host); c.pivot.set(...cell.c); c.position.set(...cell.c);
    const dx = cell.c[0] - burst[0], dy = cell.c[1] - burst[1], length = Math.hypot(dx, dy) || 1, dir = [dx / length, dy / length];
    parent.addChild(c);
    return {...cell, c, dir, dist: motion.base + Math.min(motion.max, cell.n * motion.factor), rot: motion.rotation * Math.sign(dir[0] || 1) * (dir[1] < 0 ? 1 : motion.down)};
  });
}
export function scatter(pieces, e, drift, y, alpha) {
  for (const p of pieces) {
    p.dx = p.dir[0] * p.dist * e + drift; p.dy = p.dir[1] * p.dist * e + y; p.r = p.rot * e;
    p.c.position.set(p.c.pivot.x + p.dx, p.c.pivot.y + p.dy); p.c.rotation = p.r;
    p.c.alpha = alpha;
  }
}
// The blood-moon side shows through the openings, holds a beat once the moonlit pieces are gone, then gives way to the stream.
export function bloodSky(red, l, split, fadeFrom, fadeTo, drift, y = 0) {
  red.alpha = (l >= split ? 1 : 0) * (1 - inq(k(fadeFrom, fadeTo, l)));
  red.scale.set(1 + 0.03 * k(split, fadeTo, l)); red.position.set(960 + drift, 540 + y);
}
export function revealName(S, l, {start, end, ghostIn, ghostOut, ghostEnd, exit, finish, x = 70, y = 330, dx = -80}) {
  S.nameMask.clear().rect(-20, -120, (S.nameT.width + 40) * out(k(start, end, l)), 240).fill(0xffffff);
  S.nameGhosts.forEach((g, i) => { g.position.set(36 * (i + 1), -8 * (i + 1)); g.alpha = (i ? .22 : .45) * k(start, ghostIn, l) * (1 - k(ghostOut, ghostEnd, l)); });
  S.nameG.alpha = 1 - k(exit, finish - 120, l); if (x !== null && y !== null) S.nameG.position.set(x + dx * k(exit, finish, l) ** 2, y);
}
export function caption(S, l, {start, revealStart, revealEnd, exitStart, exitEnd, x, y, dx = 260, dy = -30, width = 400}) {
  const gone = k(exitStart, exitEnd, l);
  S.capMask.clear().rect(20, -90, Math.max(width, S.capName.width + 40) * out(k(revealStart, revealEnd, l)), 100).fill(0xffffff);
  S.cap.alpha = (l >= start ? 1 : 0) * (1 - gone); S.cap.position.set(x + dx * gone * gone, y + dy * gone);
}
// Two frames of black and white, from start on real time.
export function impactFrame(S, t, start, place) {
  const first = t >= start && t < start + 40, second = t >= start + 40 && t < start + 80;
  S.imp.visible = first || second;
  if (!S.imp.visible) return;
  S.impBg.tint = first ? 0xffffff : 0x000000;
  S.impLines.tint = first ? 0x000000 : 0xffffff;
  place(); S.silB.visible = first; S.silW.visible = second;
}
export function whiteFlash(sprite, t, start, end, alpha = .85) { sprite.alpha = t >= start && t < end ? alpha : 0; }
// The big hit's shockwave, radial blur and colour split, let go once its hit stop ends; extra filters sit after the wave.
export function strikeFilters(S, t, l, {hit, release, amplitude, zoom, split}, extra = []) {
  S.shock.time = (l - hit) / 1000; S.shock.amplitude = amplitude * (1 - k(hit, hit + 520, l));
  S.zoom.strength = zoom * (1 - out(k(hit, hit + 300, l)));
  const rs = split * (1 - out(k(hit, hit + 480, l)));
  S.rgb.red = {x: -rs, y: 0}; S.rgb.blue = {x: rs, y: 0};
  const released = t >= release, filters = [S.bloom];
  if (released && l < hit + 520) filters.push(S.shock);
  filters.push(...extra);
  if (released && S.zoom.strength > 0.005) filters.push(S.zoom);
  if (released && rs > 0.5) filters.push(S.rgb);
  S.world.filters = S.imp.visible ? null : filters;
}
export function framing(S, t, l, {height, barIn, barOut, dimIn, dimOut}) {
  letterbox(S.bars, height * out(k(...barIn, t)) * (1 - out(k(...barOut, l))));
  S.dim.alpha = dimIn.reduce((alpha, [amount, start, end]) => alpha + amount * out(k(start, end, l)), 0) * (1 - k(...dimOut, l));
}
export function aim(S, pivot, position, scale, rotation) {
  for (const c of [S.world, S.ui]) { c.pivot.set(...pivot); c.scale.set(scale); c.rotation = rotation; c.position.set(...position); }
}

export function createName(P, parent, nickname, ghostColor, dropShadow) {
  const style = {fontFamily: ['LXGW WenKai', 'sans-serif'], fontSize: 132, fill: 0xffffff, stroke: {color: 0xffffff, width: 4, join: 'round'}, letterSpacing: 16};
  const nameGhosts = [0, 1].map(() => {
    const text = new P.Text({text: nickname, style: {...style, fill: ghostColor, stroke: {color: ghostColor, width: 4, join: 'round'}}});
    text.anchor.set(0, .5); parent.addChild(text); return text;
  });
  const nameT = new P.Text({text: nickname, style: dropShadow ? {...style, dropShadow} : style});
  nameT.anchor.set(0, .5); parent.addChild(nameT);
  fitText([nameT, ...nameGhosts], 1100);
  const nameMask = new P.Graphics(); parent.addChild(nameMask); nameT.mask = nameMask;
  return {nameGhosts, nameT, nameMask};
}
export function createCaption(P, parent, nickname, width) {
  const capName = new P.Text({text: nickname, style: {fontFamily: ['LXGW WenKai', 'sans-serif'], fontSize: 52, fill: 0xffffff, stroke: {color: 0xffffff, width: 1.6, join: 'round'}, letterSpacing: 8}});
  fitText([capName], width);
  capName.anchor.set(0, 1); capName.position.set(30, -10); parent.addChild(capName);
  const capMask = new P.Graphics(); parent.addChild(capMask); capName.mask = capMask;
  return {capName, capMask};
}
