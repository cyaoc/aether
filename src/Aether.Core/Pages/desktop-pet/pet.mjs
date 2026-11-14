// The 桌宠 on the 桌宠页's canvas, drawn as the design package's board draws her (DP-DesktopPet.dc.html), from the current
// character's Q 版 pictures and the numbers in her manifest (characters/README.md, desktopPet).
import { loadImage } from '../video/animations/toolkit.mjs';
import { CHEST, DONE, DROP, Engine, FOOT, GAME, GLOW, H, HANG, HOOK, HOP, NOTICE, PAGE, PK_H, SPRITE_H, bump, clamp, ease, k01, seeded, xfade } from './engine.mjs';
import { BACK, drawFx, giftBox } from './effects.mjs';
import { SANS } from '../gift-board/paint.mjs';
import { REMOTE } from '../gift-board/images.mjs';

const SPRITE_SPEED = 0.8;
// Her speech bubble, all of it, at 28 px text instead of 18, legible on a phone at 1080p (README 7).
const BUBBLE_SCALE = 28 / 18, BUBBLE_FONT = `500 ${18 * BUBBLE_SCALE}px ${SANS}`;
// The sprite keeps these drawings while it flies, since they carry something.
const CARRY = new Set(['crown', 'glow', 'lift', 'lift2', 'lid']);
// The sheets a show or 日常小动作 needs beyond those she always has: they load when it starts (the lottery's too as she waits
// through one, the chest game's when the 捣蛋鬼 is to come), not with the page, which would decode all her pictures at once
// (design package README 11).
const SEGMENTS = { hum: ['daily1'], gym: ['gym10'], hike: ['hike11'], bike: ['cycle12'], ski: ['ski9'], castle: ['castleSheet'],
  captain: ['voyage'], admiral: ['voyage'], governor: ['voyage'], lottery: ['lottery'], game: ['game13', 'game14', 'game15', 'game16', 'sp3'] };
// The 捣蛋鬼's drawings and the chest's, the same for every character (prankster/prankster.json).
const PRANKSTER = '/desktop-pet/prankster/';
// A gift's icon falling in the chest game covers the little box it stands in for, bow and all: that many of its half sides.
const ICON = 2.4;

// The box of what is drawn in a part of a picture, and the centre of its mass.
const box = (im, x, y, w, h) => {
  const c = document.createElement('canvas'); c.width = w; c.height = h;
  const g = c.getContext('2d'); g.drawImage(im, x, y, w, h, 0, 0, w, h);
  const a = g.getImageData(0, 0, w, h).data;
  let x0 = w, y0 = h, x1 = -1, y1 = -1, sa = 0, sx = 0, sy = 0;
  for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) {
    const al = a[(j * w + i) * 4 + 3];
    if (al > 40) { if (i < x0) x0 = i; if (i > x1) x1 = i; if (j < y0) y0 = j; if (j > y1) y1 = j; sa += al; sx += al * i; sy += al * j; }
  }
  return x1 < 0 ? null : { x: x + x0, y: y + y0, w: x1 - x0 + 1, h: y1 - y0 + 1, cx: x + sx / sa, cy: y + sy / sa };
};

// A sheet's 3 x 2 cells: each drawing anchors at its row's ground line and its own centre of mass (the figures are not centred
// in their thirds, so the cell centre would shift her sideways between poses), and every sheet is scaled by its first cell
// (the idle ruler).
const sheetCells = im => {
  const cw = im.width / 3, ch = im.height / 2, list = [];
  for (let i = 0; i < 6; i++) list.push(box(im, (i % 3) * cw, Math.floor(i / 3) * ch, cw, ch));
  const ground = [0, 1].map(r => Math.max(...list.slice(r * 3, r * 3 + 3).filter(Boolean).map(b => b.y + b.h)));
  return list.map((b, i) => b && { im, ...b, ox: (i % 3) * cw, oy: Math.floor(i / 3) * ch, ground: ground[Math.floor(i / 3)], ruler: list[0].h });
};

// A strip's four frames drawn from cell c: each keeps its anchor and ruler, so only the moving part moves.
const framesFrom = (im, c) => [0, 1, 2, 3].map(i => ({ im, ...box(im, i * im.width / 4, 0, im.width / 4, im.height), cx: c.cx - c.ox + i * im.width / 4, cy: c.cy - c.oy, ruler: c.ruler }));

const pixels = c => {
  const cv = document.createElement('canvas'); cv.width = c.w; cv.height = c.h;
  const g = cv.getContext('2d'); g.drawImage(c.im, c.x, c.y, c.w, c.h, 0, 0, c.w, c.h);
  return g.getImageData(0, 0, c.w, c.h).data;
};
// Where the rope a pulling drawing holds ends (or the sprite's fists are): the middle of what the drawing shows at its left
// edge, down its height.
const ropeEnd = c => {
  const a = pixels(c);
  let n = 0, sy = 0;
  for (let j = 0; j < c.h; j++) for (let k = 0; k < 3; k++) if (a[(j * c.w + k) * 4 + 3] > 40) { n++; sy += j; }
  c.rope = n ? sy / n / c.h : 0.5;
};
// Where the rope in a carrying drawing ends (tip, from the drawing's centre): the lowest thin strand in it that runs on up a
// little way (the round end of its tail narrows for a few rows only; the paper bag's rope is free of the tail for the last
// twenty rows or so). The chest's hook hangs LINK below it.
const ropeTip = c => {
  const a = pixels(c), thin = c.ruler * 0.11, rows = [];
  const strands = j => rows[j] ??= (() => {   // the middles of the thin runs of row j
    const out = []; let s = -1;
    for (let i = 0; i <= c.w; i++) { const on = i < c.w && a[(j * c.w + i) * 4 + 3] > 40; if (on && s < 0) s = i; if (!on && s >= 0) { if (i - s <= thin) out.push((s + i - 1) / 2); s = -1; } }
    return out;
  })();
  for (let j = c.h - 1; j > 0; j--) for (const mid of strands(j)) {
    let n = 1, m = mid;
    for (; j - n >= 0; n++) { const up = strands(j - n).find(q => Math.abs(q - m) < 6); if (up == null) break; m = up; }
    if (n >= c.ruler * 0.03) return (c.tip = { x: c.x + mid - c.cx, y: c.y + j - c.cy });
  }
  throw new Error('a carrying drawing holds no rope');
};

/** The 桌宠 drawn on `canvas` from `character`, her manifest; Engine decides what she does. */
export class Pet extends Engine {
  constructor(canvas, master, shows, character) {
    super(master, shows, character.desktopPet);
    Object.assign(this, { canvas, character, cells: {}, strips: {}, tweens: {}, sstrips: {}, pkstrips: {}, pkTweens: {}, icons: new Map(), loads: {}, props: {}, trail: [] });
  }

  picture(name) { return loadImage('/characters/current/' + name); }

  // Everything she always needs, her sprite's sheets, turnaround views and moving drawings included, and her props, whose sizes
  // place the big shows from their first moment; not the sheets of SEGMENTS.
  async boot() {
    const { sheets, views, props, poses: POSE } = this.art, later = Object.values(SEGMENTS).flat(), { idle, blink, happy, lookRight } = this.character.sprite;
    await Promise.all([
      ...Object.keys(sheets).filter(k => !later.includes(k)).map(k => this.loadSheet(k)),
      ...Object.entries({ sIdle: idle, sBlink: blink, sHappy: happy, sLook: lookRight }).map(async ([k, file]) => {
        const im = await this.picture(file), b = box(im, 0, 0, im.width, im.height); this.cells[k] = [{ im, ...b, ox: 0, oy: 0, ruler: b.h }];
      }),
      // The sprite's four turnaround views in a row: front, three-quarter and side (both facing the viewer's left), back.
      this.picture(views).then(im => {
        const cw = im.width / 4, ch = im.height, list = [];
        for (let i = 0; i < 4; i++) list.push(box(im, i * cw, 0, cw, ch));
        this.cells.views = list.map((b, i) => ({ im, ...b, ox: i * cw, oy: 0, ruler: list[0].h }));
      }),
      ...Object.entries(props).map(async ([k, file]) => { this.props[k] = await this.picture(file); })
    ]);
    await this.loadSpriteStrips();
    const { castleClosed: closed, castleOpen: open } = this.props;
    this.castle = { closed, open, box: box(closed, 0, 0, closed.width, closed.height) };
    const [k, i] = POSE.idle, c = this.cells[k][i];
    this.herLeft = (c.x - c.cx) * H / c.ruler;  // her left edge standing idle, from her centre
  }

  loadSheet(k) { return this.loads[k] ??= this.measureSheet(k); }

  // A sheet's cells (sheetCells). Her strips drawn from its poses come with it, and only then is it drawn from.
  async measureSheet(k) {
    const { sheets, poses: POSE, sameSpot: SAME_SPOT, body: BODY, strips: STRIP, transitions: TRANS } = this.art, on = p => POSE[p][0] === k;
    const cells = sheetCells(await this.picture(sheets[k]));
    for (const [p, q] of Object.entries(SAME_SPOT)) if (on(p)) {  // the earlier pose's centre, at the same place in this pose's cell
      const c = cells[POSE[p][1]], d = cells[POSE[q][1]];
      c.cx = d.cx - d.ox + c.ox;
    }
    for (const [p, dx] of Object.entries(BODY)) if (on(p)) {
      const spot = SAME_SPOT[p] || p;
      for (const q of Object.keys(POSE)) if ((SAME_SPOT[q] || q) === spot) { const c = cells[POSE[q][1]]; c.cx -= dx * c.ruler / H; }
    }
    // a strip's frames keep the anchor, ground and ruler of the cell they were drawn from, so only the moving part moves
    const framesOf = async (f, pose) => {
      const im = await this.picture(this.art.frames[f]), fw = im.width / 4, c = cells[POSE[pose][1]];
      return [0, 1, 2, 3].map(i => ({ im, ...box(im, i * fw, 0, fw, im.height), cx: c.cx - c.ox + i * fw, ground: c.ground - c.oy, ruler: c.ruler }));
    };
    const [strips, tweens] = await Promise.all([
      Promise.all(Object.entries(STRIP).filter(([pose]) => on(pose)).map(async ([pose, s]) => [pose, await framesOf(s.f, pose)])),
      Promise.all(Object.entries(TRANS).filter(([pair]) => on(pair.split('>')[0])).map(async ([pair, f]) => [pair, (await framesOf(f, pair.split('>')[0])).slice(1, 3)]))
    ]);
    Object.assign(this.strips, Object.fromEntries(strips));
    Object.assign(this.tweens, Object.fromEntries(tweens));
    this.cells[k] = cells;
    this.routes = null;
  }

  // The sprite's drawings that move, each from the drawing it was drawn from: its turnaround views, its sheets' cells or its
  // own pictures.
  async loadSpriteStrips() {
    const { spriteStrips: SSTRIP, sprite: SPRITE, frames: FRAMES } = this.art;
    await Promise.all(Object.entries(SSTRIP).map(async ([key, [f]]) => {
      const im = await this.picture(FRAMES[f]), c = key[0] === 'v' ? this.cells.views[+key[1]] : key in SPRITE ? this.cells[SPRITE[key][0]][SPRITE[key][1]] : this.cells[key][0];
      this.sstrips[key] = framesFrom(im, c);
    }));
  }

  // The chest game's pictures: her sheets of it (SEGMENTS.game) and the 捣蛋鬼's, its loops and in-betweens (frames 2 and 3 of a
  // strip drawn in the first pose's cell) and the chest's, loaded the first time it is to come (update); only then does it set
  // off (gameLoaded). Then where the rope ends in the drawings that pull it and those that carry the chest.
  loadGame() { return this.loads.game ??= this.measureGame(); }

  async measureGame() {
    const prankster = this.prankster = await (await fetch(PRANKSTER + 'prankster.json')).json(), picture = name => loadImage(PRANKSTER + name);
    await Promise.all([...SEGMENTS.game.map(k => this.loadSheet(k)),
      ...Object.entries(prankster.sheets).map(async ([k, file]) => { this.cells[k] = sheetCells(await picture(file)); }),
      ...Object.entries(prankster.props).map(async ([k, file]) => { this.props[k] = await picture(file); })]);
    await Promise.all([
      ...Object.entries(prankster.strips).map(async ([pose, s]) => { this.pkstrips[pose] = framesFrom(await picture(prankster.frames[s.f]), this.pkCell(pose)); }),
      ...Object.entries(prankster.transitions).map(async ([pair, f]) => { this.pkTweens[pair] = framesFrom(await picture(prankster.frames[f]), this.pkCell(pair.split('>')[0])).slice(1, 3); })]);
    // the chest's size on the page (its closed drawing), and its drawings' px to the page
    const { chest, chestOpen } = this.props;
    this.chestBox = { w: chest.width * H / prankster.anchor, h: chest.height * H / prankster.anchor, open: chestOpen.height * H / prankster.anchor, k: H / prankster.anchor };
    for (const pose of ['tug', 'haul', 'ouch']) ropeEnd(this.pkCell(pose));
    for (const pose of ['hold', 'hold2']) ropeEnd(this.cell(pose, this.art.sprite));
    for (const pose of ['carry', 'carryBag', 'carryCrown']) ropeTip(this.pkCell(pose));
    for (const pose of ['lift', 'lift2']) ropeTip(this.cell(pose, this.art.sprite));
    this.gameLoaded = true;
  }

  start(name, opts) {
    for (const k of SEGMENTS[name] ?? []) this.loadSheet(k);
    super.start(name, opts);
  }

  update(now) {
    if (this.gm.due) this.loadGame();
    super.update(now);
  }

  receive(state) {
    super.receive(state);
    if (this.lot) for (const k of SEGMENTS.lottery) this.loadSheet(k);
  }

  // The page scales with the browser source, 3 : 4 as she was designed at 480 × 640, and stands on its bottom edge.
  resize(width, height) {
    Object.assign(this.canvas, { width, height });
    const k = Math.min(width / PAGE.w, height / PAGE.h);
    this.view = { k, x: (width - PAGE.w * k) / 2, y: height - PAGE.h * k };
  }

  // ---- drawing -----------------------------------------------------------------------------------------------------
  // The 桌宠 switched off leaves the page transparent.
  frame() {
    const g = this.canvas.getContext('2d'), now = this.clock = this.now();
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.clearRect(0, 0, this.canvas.width, this.canvas.height);
    if (this.on) this.draw(g, this.stateAt(now), now);
  }

  draw(g, st, now) {
    g.save(); g.translate(this.view.x, this.view.y); g.scale(this.view.k, this.view.k);
    g.beginPath(); g.rect(0, 0, PAGE.w, PAGE.h); g.clip();
    const shake = this.shakeOf(st, now); g.translate(shake.x, shake.y);
    if (st.castle) this.drawCastle(g, st.castle);
    if (st.ship) this.drawShip(g, st.ship);
    if (st.admShip) this.drawAdmShip(g, st.admShip);
    if (st.emblem) this.drawProp(g, this.props.emblem, st.emblem.x, st.emblem.y + this.props.emblem.height * H / this.art.anchor * st.emblem.s / 2, st.emblem.s, st.emblem.a);
    const rt = performance.now(), dt = Math.min(100, rt - (this.rt ?? rt));
    this.rt = rt;
    // a pose whose sheet is still loading: she keeps the drawing she has until it is there, on the first frame idle's (boot)
    if (!this.cells[this.art.poses[st.pose][0]]) st.pose = this.hv?.pose ?? 'idle';
    const her = this.herBox(st);
    const bub = st.bubble?.text && this.bubbleBox(g, st, her, this.bubSide);
    this.bubSide = bub?.side;
    const target = this.spritePos(st, her, now, bub), sp = target && this.follow(target, dt, st);
    this.sTarget = target && { ...target, sp: st.sp };   // where it is flying to (smooth: is it on her head yet)
    if (sp) { this.trail = this.trail.filter(q => rt - q.rt < 420); if (st.sprite === 'glow') this.trail.push({ x: sp.x, y: sp.y, rt }); }
    for (const f of st.fx) if (BACK.has(f[0])) drawFx(g, f, st, her, now, this);
    if (sp && st.sBehind) this.drawSprite(g, st, sp, now);
    this.drawHer(g, st, now);
    if (sp && !st.sBehind && !st.sOver) this.drawSprite(g, st, sp, now);
    if (st.gift) this.drawGift(g, st.gift);
    for (const f of st.fx) if (!BACK.has(f[0])) drawFx(g, f, st, her, now, this);
    this.drawGame(g, st, now, her, bub);
    if (sp && st.sOver) this.drawSprite(g, st, sp, now);   // its fists on what the game draws (the chest's lid)
    if (bub) this.drawBubble(g, st.bubble, bub);
    g.restore();
  }

  cell(name, set = this.art.poses) {
    const f = set === this.art.poses && this.strips[name];
    if (f) {
      const s = this.art.strips[name];
      if (s.ms) return f[Math.floor(this.clock / s.ms) % 4];
      const steps = s.loop || s.once, total = steps.reduce((a, [, ms]) => a + ms, 0);
      let t = s.loop ? this.clock % total : this.clock - (this.hv?.pose === name ? this.hv.since : this.clock);
      for (const [i, ms] of steps) { if (t < ms || !ms) return f[i]; t -= ms; }
      return f[steps.at(-1)[0]];
    }
    const [k, i] = set[name] || [name, 0]; return this.cells[k][i];
  }

  herBox(st, c = this.cell(st.pose)) {
    const k = H / c.ruler * st.s;
    return { x: st.x + (c.x - c.cx) * k, y: st.y + (c.y - c.ground) * k, w: c.w * k, h: c.h * k, k };
  }

  // In-between frames from pose a to pose b: their own strip; else [] where she simply switches (cuts, that direction);
  // else the other way's strip played backwards; null where there are none.
  hop(a, b) {
    const there = this.tweens[`${a}>${b}`], back = this.tweens[`${b}>${a}`];
    return there || (this.art.cuts.includes(`${a}>${b}`) ? [] : null) || (back && [...back].reverse()) || null;
  }

  // A show can start, or take over a lesser one, whatever she is doing (humming, sitting bored, lying by her bike),
  // and most of those pairs have no in-betweens of their own. So she goes the shortest way through poses that have them
  // (at most five steps; standing is tried first, so it is the usual way through, and a movement played forwards before
  // one played backwards, so after a fall she gets up rather than the fall running back), playing their frames one
  // after another; null where there is no way (then the new pose just comes in).
  route(a, b) {
    const memo = this.routes ??= {}, key = `${a}>${b}`;
    if (key in memo) return memo[key];
    const from = { [a]: null }, forwards = (p, n) => this.tweens[`${p}>${n}`] || this.art.cuts.includes(`${p}>${n}`);
    let ring = [a];
    for (let depth = 0; depth < 5 && ring.length && !(b in from); depth++) {
      const next = [];
      for (const back of [false, true]) for (const p of ring) for (const n of Object.keys(this.art.poses)) {
        if (!(n in from) && this.hop(p, n) && !forwards(p, n) === back) { from[n] = p; next.push(n); }
      }
      ring = next;
    }
    if (!(b in from)) return memo[key] = null;
    const path = [b];
    while (path[0] !== a) path.unshift(from[path[0]]);
    return memo[key] = path.slice(1).flatMap((m, i) => this.hop(path[i], m));
  }

  // A pose change is one movement, not a cut: she moves through in-between frames, the pair's own or a way through
  // others (route); where she simply switches (cuts) or there is no way at all, the new pose just comes in.
  drawHer(g, st, now) {
    const rt = performance.now(), el = t0 => rt - t0;
    const hv = this.hv ??= { pose: st.pose, at: -1e9, since: this.clock, tween: null };
    if (st.pose !== hv.pose) Object.assign(hv, { tween: this.route(hv.pose, st.pose), ms: this.art.slow[`${hv.pose}>${st.pose}`] || 90, pose: st.pose, at: rt, since: this.clock, fromX: hv.x ?? st.x });
    // she breathes in every pose, in-between frames too (breathing that stopped when she left her idle pose made her head
    // jump up to 3 px at the change): her drawing scaled at her feet, a touch taller and narrower, then shorter and wider.
    // Some in-betweens are played slower (slow: her hair settling under the sprite's magic, her curling up to sulk).
    const i = Math.floor(el(hv.at) / (hv.ms || 90)), s = 1 + 0.012 * Math.sin(now / 600), c = hv.tween?.[i] || this.cell(hv.pose);
    // In the chest game her place goes with her pose (her fists on the ring, wherever each drawing has them): through the
    // in-betweens she moves from where the earlier pose stood to the later one's, a share at each frame (st.slide), not all
    // at once on the first of them.
    const x = st.x + (st.slide && hv.tween?.[i] ? (hv.fromX - st.x) * (1 - (i + 1) / (hv.tween.length + 1)) : 0), b = this.herBox({ ...st, x }, c);
    hv.x = st.x;
    g.save(); g.translate(x, st.y); g.scale(st.sx / Math.sqrt(s), s); g.translate(-x, -st.y);
    g.drawImage(c.im, c.x, c.y, c.w, c.h, b.x, b.y, b.w, b.h); g.restore();
  }

  // The sprite flies to where it is wanted instead of jumping there; its speed decides which way it faces.
  follow(target, dt, st) {
    const sv = this.sv;
    if (!sv) { this.sv = { ...target, vx: 0, vy: 0, d: 0 }; return this.sv; }
    const k = 1 - Math.exp(-dt / (st.orbit != null ? 90 : 200)), sec = Math.max(dt, 1) / 1000;
    let nx = sv.x + (target.x - sv.x) * k, ny = sv.y + (target.y - sv.y) * k;
    // no faster than SPRITE_SPEED px/ms: a long way is flown, never jumped; on the circle twice that, to keep up with it, but
    // flying back onto it from afar (down from a toss) at the plain speed
    const step = Math.hypot(nx - sv.x, ny - sv.y), most = SPRITE_SPEED * (st.orbit != null && Math.hypot(target.x - sv.x, target.y - sv.y) < 40 ? 2 : 1) * Math.max(dt, 1);
    if (step > most) { nx = sv.x + (nx - sv.x) * most / step; ny = sv.y + (ny - sv.y) * most / step; }
    sv.vx += ((nx - sv.x) / sec - sv.vx) * 0.3; sv.vy += ((ny - sv.y) / sec - sv.vy) * 0.3;
    sv.x = nx; sv.y = ny; sv.scale = target.scale; sv.d = Math.hypot(target.x - nx, target.y - ny);
    return sv;
  }

  // st.sp: null (home), ['home', dx, dy] (home moved by), ['rel', fx, fy] (a point of her drawing's box, as fractions of
  // it), ['at', x, y] (a page point), ['tossed', hop] (over her head, hopping by hop of her height, and above bub, her
  // bubble, when that is over her head, however many lines it has), or the named spots below.
  spritePos(st, her, now, bub) {
    if (st.orbit == null) this.orb = null;
    if (st.sp?.[0] === 'gone') return null;
    const home = this.home(now);
    // Circling her waist: on the far half of the circle it passes behind her, so it never covers her face.
    // It is a little bigger on the near half, and drawSprite turns it by st.orbitA.
    // It joins the circle where it is and goes round from there (README 1.1): when a circle starts, its angle is the one of
    // its own place, on the near half in front of her; and when the circle's clock jumps (a show handing over to the happy
    // idle), the angle carries on instead. It used to take the angle straight from the clock, so the point it flew to sat
    // anywhere on the circle and it dashed across her to it, up to 29 px a frame, as if it had jumped.
    if (st.orbit != null) {
      const R = st.orbitR || 135, raw = st.orbit / 1000 * Math.PI * 1.2;
      if (!this.orb) this.orb = { shift: (this.sv ? Math.acos(clamp((this.sv.x - st.x) / R, -1, 1)) : raw) - raw, raw };
      else if (Math.abs(raw - this.orb.raw) > 0.6) this.orb.shift = this.orb.a - raw;
      const a = st.orbitA = this.orb.a = raw + this.orb.shift; this.orb.raw = raw; st.sBehind = Math.sin(a) < 0;
      return { x: st.x + Math.cos(a) * R, y: (st.orbitY ?? FOOT.y - 0.34 * H) + Math.sin(a) * 26, scale: 1 + 0.1 * Math.sin(a) };
    }
    if (!st.sp) return home;
    if (st.sp[0] === 'home') return { x: home.x + st.sp[1], y: home.y + st.sp[2] };
    const rel = (fx, fy) => ({ x: her.x + her.w * fx, y: her.y + her.h * fy });
    if (st.sp[0] === 'rel') return rel(st.sp[1], st.sp[2]);
    if (st.sp[0] === 'at') return { x: st.sp[1], y: st.sp[2] };
    if (st.sp === 'head') return { x: her.x + her.w * 0.25, y: her.y - SPRITE_H * 0.2 + 3 * Math.sin(now / 500) };
    if (st.sp === 'knee') return { x: her.x + her.w * 0.94, y: FOOT.y - 42 + 2 * Math.sin(now / 900) };
    if (st.sp[0] === 'crown') { const top = { x: FOOT.x, y: her.y - SPRITE_H * 0.55 }; const p = st.sp[1], d = st.sp[2]; return { x: home.x + (top.x - home.x) * p, y: home.y + (top.y - home.y) * p + 26 * d }; }
    if (st.sp[0] === 'tossed') return rel(0.5, (bub && !bub.side ? Math.min(-0.5, (bub.y - SPRITE_H * 0.55 - her.y) / her.h) : -0.5) - st.sp[1]);
    if (st.sp[0] === 'home-from-top') { const top = { x: FOOT.x, y: her.y - SPRITE_H * 0.55 }; const p = st.sp[1]; return { x: top.x + (home.x - top.x) * p, y: top.y + (home.y - top.y) * p }; }
    if (st.sp[0] === 'to-castle') { const p = st.sp[1]; return { x: home.x + (FOOT.x - home.x) * p, y: home.y + (300 - home.y) * p, scale: st.sp[2] }; }
    if (st.sp[0] === 'mast') return { x: st.sp[1], y: st.sp[2] };
    return home;
  }

  // The sprite turns the way it goes. Going round her (anticlockwise seen from above) it faces front, three-quarter, side and
  // back in turn; flying somewhere it shows its side or three-quarter, leaning in, and does its action (patting, sleeping,
  // the flag...) only once it has arrived, landing into it with a squash. The views face left and are mirrored for right.
  drawSprite(g, st, p, now) {
    const { spriteStrips: SSTRIP, sprite: SPRITE } = this.art;
    let key = st.sprite, flip = !!st.sFlip, tilt = 0;
    if (st.sView) key = st.sView;
    else if (st.orbit != null) {
      const hx = -Math.sin(st.orbitA), d = Math.abs(Math.atan2(hx, Math.cos(st.orbitA)));
      key = d < Math.PI / 8 ? key : d < 3 * Math.PI / 8 ? 'v1' : d < 5 * Math.PI / 8 ? 'v2' : 'v3'; flip = key[0] === 'v' && hx > 0;
    } else if (p.d > 14 && !CARRY.has(key) && !st.sStay) {   // sStay: it only follows her head (the chest game), it does not fly
      key = Math.abs(p.vx) > 2 * Math.abs(p.vy) ? 'v2' : 'v1'; flip = p.vx > 0; tilt = clamp(p.vx / 2500, -0.18, 0.18);
    }
    const rt = performance.now(), id = key + (flip ? '<' : ''), sc = this.sc ??= { id, prev: null, at: -1e9, land: false };
    if (sc.id !== id) { sc.land = st.orbit == null && key[0] !== 'v'; sc.prev = sc.id; sc.id = id; sc.at = rt; }
    const u = rt - sc.at, [out, inn] = sc.prev ? xfade(u) : [0, 1], q = k01(u, 0, 320);
    const land = sc.land ? 1 - 0.14 * Math.sin(2 * Math.PI * q) * (1 - q) : 1;
    if (key === 'pat') { tilt = 0.12 * Math.sin(now / 180); }
    const pick = k => this.sstrips[k]?.[Math.floor(now / SSTRIP[k][1]) % 4] ?? (k[0] === 'v' && k.length === 2 ? this.cells.views[+k[1]] : k in SPRITE ? this.cell(k, SPRITE) : this.cells[k][0]);
    const one = (id, alpha) => {
      let k = id.replace('<', '');
      if (k === 'sIdle' && now % 4500 < 140) k = 'sBlink';
      const c = pick(k), s = SPRITE_H / c.ruler * st.sScale * (p.scale || 1), bottom = (c.y + c.h - c.cy) * s;
      g.save(); g.globalAlpha = st.sAlpha * alpha; g.translate(p.x, p.y + (key === 'pat' ? (st.patHard ? 7 : 3) * Math.abs(Math.sin((st.patPhase ?? now) / 180)) : 0)); g.rotate(tilt);
      g.translate(0, bottom); g.scale((id.endsWith('<') ? -1 : 1) / Math.sqrt(land), land); g.translate(0, -bottom);
      g.drawImage(c.im, c.x, c.y, c.w, c.h, (c.x - c.cx) * s, (c.y - c.cy) * s, c.w * s, c.h * s);
      g.restore();
    };
    if (out > 0) one(sc.prev, out);
    one(id, inn);
  }

  // The castle, its centre at c.x, c.y; its gate opens over it as c.open goes to 1.
  drawCastle(g, c) {
    const w = this.castle.closed.width * c.s, h = this.castle.closed.height * c.s;
    g.save(); g.globalAlpha = c.a;
    g.drawImage(this.castle.closed, c.x - w / 2, c.y - h / 2, w, h);
    if (c.open) { g.globalAlpha = c.a * c.open; g.drawImage(this.castle.open, c.x - w / 2, c.y - h / 2, w, h); }
    g.restore();
  }

  // The flagship, its bow deck at s.x, s.y.
  drawShip(g, s) {
    const im = this.props.flagship, k = H / this.art.anchor * s.s, d = this.art.points.flagship.deck;
    g.save(); g.globalAlpha = s.a; g.drawImage(im, s.x - d[0] * k, s.y - d[1] * k, im.width * k, im.height * k); g.restore();
  }

  // The admiral's ship (bottom centre at s.x, s.y, rocked by s.rot): a purple glow behind it when it answers her salute;
  // while it moves (s.speed 0..1) a white wake trails its stern and spray flies off its bow; its flags strung bow to
  // mainmast to foremast to stern as it dresses (s.flags 0..1).
  drawAdmShip(g, s) {
    if (!s.a) return;
    const im = this.props.admship, k = H / this.art.anchor * s.s, w = im.width * k, h = im.height * k, c = GLOW.A, now = performance.now();
    g.save(); g.translate(s.x, s.y); g.rotate(s.rot || 0);
    if (s.glow) {
      const gr = g.createRadialGradient(0, -h * 0.5, 0, 0, -h * 0.5, w * 0.8); gr.addColorStop(0, `rgba(${c},${0.6 * s.glow})`); gr.addColorStop(1, `rgba(${c},0)`);
      g.globalCompositeOperation = 'lighter'; g.fillStyle = gr; g.fillRect(-w, -h * 1.4, w * 2, h * 1.7); g.globalCompositeOperation = 'source-over';
    }
    if (s.speed > 0.05) {  // lilac-edged so they show against the white cloud waves and the sky
      g.lineCap = 'round';
      for (const [col, wd] of [[`rgba(${c},${0.7 * s.speed})`, 4], [`rgba(255,255,255,${0.95 * s.speed})`, 0]]) {
        g.strokeStyle = col;
        for (let i = 0; i < 3; i++) { const len = w * (0.3 + 0.4 * s.speed) * (1 - i * 0.2); g.lineWidth = 6 - i * 1.6 + wd; g.beginPath(); g.moveTo(-w * 0.5, -h * 0.07 + i * 9); g.quadraticCurveTo(-w * 0.5 - len * 0.5, -h * 0.01 + i * 10 + 3 * Math.sin(now / 200 + i), -w * 0.5 - len, -h * 0.09 + i * 9); g.stroke(); }
      }
      for (let i = 0; i < 8; i++) {
        const u = (now / 420 + i / 8) % 1, x = w * 0.48 + u * 34 * s.speed, y = -h * 0.08 - Math.sin(Math.PI * u) * 28 * s.speed, r = (4.5 - 2.5 * u) * (0.6 + 0.4 * s.speed);
        g.fillStyle = `rgba(255,255,255,${0.95 * s.speed})`; g.strokeStyle = `rgba(${c},${0.8 * s.speed})`; g.lineWidth = 1.5; g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2); g.fill(); g.stroke();
      }
    }
    g.drawImage(im, -w / 2, -h, w, h);
    g.restore();
    g.save();
    if (s.flags) {
      const pts = this.art.points.admship.rig.map(q => this.admPoint(s, q)), segs = pts.slice(1).map((q, i) => [pts[i], q]), n = 22, cols = ['#FF6B6B', '#FFD45E', '#7FD3FF', '#C4A2FF', '#9DF0C0', '#fff'];
      const total = segs.reduce((a, [p0, p1]) => a + Math.hypot(p1.x - p0.x, p1.y - p0.y), 0), upto = total * s.flags;
      g.strokeStyle = '#3A2A26'; g.lineWidth = 1.2; g.beginPath(); g.moveTo(pts[0].x, pts[0].y);
      let run = 0;
      for (const [p0, p1] of segs) { const len = Math.hypot(p1.x - p0.x, p1.y - p0.y), u = Math.min(1, Math.max(0, (upto - run) / len)); if (u > 0) g.lineTo(p0.x + (p1.x - p0.x) * u, p0.y + (p1.y - p0.y) * u); run += len; }
      g.stroke();
      for (let i = 1; i < n; i++) {
        let d = total * i / n; if (d > upto) break;
        let j = 0; while (j < segs.length - 1 && d > Math.hypot(segs[j][1].x - segs[j][0].x, segs[j][1].y - segs[j][0].y)) { d -= Math.hypot(segs[j][1].x - segs[j][0].x, segs[j][1].y - segs[j][0].y); j++; }
        const [p0, p1] = segs[j], len = Math.hypot(p1.x - p0.x, p1.y - p0.y), fx = p0.x + (p1.x - p0.x) * d / len, fy = p0.y + (p1.y - p0.y) * d / len, sw = 3 * Math.sin(performance.now() / 150 + i);
        g.fillStyle = cols[i % cols.length]; g.beginPath(); g.moveTo(fx - 6, fy); g.lineTo(fx + 6, fy); g.lineTo(fx + sw, fy + 15); g.closePath(); g.fill(); g.stroke();
      }
    }
    g.restore();
  }

  // A prop at her scale times s, its bottom centre at x, y.
  drawProp(g, im, x, y, s = 1, a = 1, src) {
    if (!a || !s) return;
    const k = H / this.art.anchor * s, [sx, sy, sw, sh] = src || [0, 0, im.width, im.height];
    g.save(); g.globalAlpha = a; g.drawImage(im, sx, sy, sw, sh, x - sw * k / 2, y - sh * k, sw * k, sh * k); g.restore();
  }

  // The big gift box (bottom centre at b.x, b.y) under its balloons; once open, its lid (the top of the closed box) flies off.
  drawGift(g, b) {
    const box = this.props.giftbox, k = H / this.art.anchor * b.s, top = b.y - box.height * k;
    if (b.balloons) {
      const r = b.balloons.rise;
      this.drawProp(g, this.props.balloons, b.x + 20 * Math.sin(r / 300) * Math.min(1, r / 400), top - r * 0.35 + 4, 1, b.balloons.a);
    }
    this.drawProp(g, b.open ? this.props.giftboxOpen : box, b.x, b.y, b.s);
    if (b.lid != null && b.lid < 900) {
      const u = b.lid, lidH = box.height * 0.42;
      g.save(); g.translate(b.x + u * 0.12, top + lidH * k / 2 - u * 0.55 + 0.0004 * u * u); g.rotate(u / 400);
      this.drawProp(g, box, 0, lidH * k / 2, b.s, 1 - u / 900, [0, 0, box.width, lidH]); g.restore();
    }
  }

  // ---- the chest game's drawings (gameState places them) -------------------------------------------------------------
  // The chest on a rope len long swings like a weight from pivot: it lags when what carries it starts off and swings on when
  // that stops, and settles. Returns the rope's angle from straight down.
  swing(pivot, len) {
    const rt = performance.now(), s = this.sw ??= { a: 0, w: 0, x: pivot.x, vx: 0, rt };
    const dt = Math.min(40, rt - s.rt);
    s.rt = rt;
    if (dt > 0) {
      const vx = clamp((pivot.x - s.x) / dt, -0.6, 0.6), ax = clamp((vx - s.vx) / dt, -0.004, 0.004);
      s.w += (-1.6e-5 * Math.sin(s.a) - 0.3 * ax / len * Math.cos(s.a) - 0.0016 * s.w) * dt;
      Object.assign(s, { a: clamp(s.a + s.w * dt, -0.5, 0.5), vx, x: pivot.x });
    }
    return s.a;
  }

  // The drawing of the 捣蛋鬼 to show now: a loop's frame or its cell. Where its rope ends is the cell's (pkCell): a loop keeps
  // its fists and rope still.
  pkFrame(pose) { const f = this.pkstrips[pose]; return f ? f[Math.floor(this.clock / this.prankster.strips[pose].ms) % 4] : this.pkCell(pose); }

  // The 捣蛋鬼 at p, its drawing's centre: every sheet's first cell is its idle drawing, the ruler that brings its sheets to one
  // size. A change of its pose plays the in-betweens drawn for it (the other way round, backwards), 90 ms each, as hers do,
  // and the lean it pulls at (rot) goes over to the new one with them. Turning round (its flip changes) it changes at once:
  // the in-betweens are drawn facing one way.
  drawPk(g, p) {
    const rt = performance.now(), pv = this.pv ??= { pose: p.pose, at: -1e9, tween: null, rot: p.rot, flip: p.flip };
    if (p.pose !== pv.pose) {
      const there = this.pkTweens[`${pv.pose}>${p.pose}`], back = this.pkTweens[`${p.pose}>${pv.pose}`];
      Object.assign(pv, { tween: !!pv.flip === !!p.flip && (there || back?.slice().reverse()) || null, pose: p.pose, at: rt, from: pv.rot });
    }
    pv.flip = p.flip;
    const i = Math.floor((rt - pv.at) / 90), tw = pv.tween?.[i];
    const c = tw || this.pkFrame(p.pose), rot = tw ? pv.from + (p.rot - pv.from) * (i + 1) / (pv.tween.length + 1) : p.rot;
    const s = PK_H / c.ruler * p.scale, t = p.trim || 0;   // trim: that much of the rope it holds, at the drawing's left, is left off
    pv.rot = rot;
    g.save(); g.globalAlpha = p.a; g.translate(p.x, p.y); g.rotate(rot); g.scale(p.flip ? -s : s, s);
    g.drawImage(c.im, c.x + t, c.y, c.w - t, c.h, c.x + t - c.cx, c.y - c.cy, c.w - t, c.h); g.restore();
  }

  // A ring of the chest, hung from the link at p and swung to point along ux, uy (straight down when nothing holds it).
  drawRing(g, p, ux = 0, uy = 1) {
    const { hang } = this.prankster.ring, k = this.chestBox.k;
    g.save(); g.translate(p.x, p.y); g.rotate(Math.atan2(uy, ux) - Math.PI / 2); g.scale(k, k);
    g.drawImage(this.props.chestRing, -hang[0], -hang[1]); g.restore();
  }

  // A gift falling in the chest game: its icon (B站's picture, loaded as the 礼物榜页 loads them), or a little box while there
  // is none or it is not there yet; b its half side.
  dropGift(g, url, x, y, b, rot) {
    if (url && !this.icons.has(url)) { this.icons.set(url, null); loadImage(url, REMOTE).then(im => this.icons.set(url, im), () => {}); }
    const im = this.icons.get(url);
    if (!im) return giftBox(g, x, y, b, rot);
    g.save(); g.translate(x, y); g.rotate(rot); g.drawImage(im, -ICON * b / 2, -ICON * b / 2, ICON * b, ICON * b); g.restore();
  }

  // The chest game's drawings, over her and everything of hers: the chest (standing, or hanging by the handle the hook rope
  // is in, that end up), the 捣蛋鬼 and its rope, what drops, the numbers, the fight's board along the top of the page, and what
  // the 捣蛋鬼 says. Nothing of it passes behind her: it flies over her head or stays on her right.
  drawGame(g, st, now, her, bub) {
    const gm = this.gm, L = this.gl, v = gm.visit, box = this.chestBox;
    if (!this.gameLoaded) return;   // nothing of the game is on the page before its pictures are there
    const c = L.chest, pk = L.pk, top = CHEST.y - box.h, sv = this.sv, K = box.k, RING = this.prankster.ring;
    const text = (str, x, y, font, fill) => { g.font = font; g.lineJoin = 'round'; g.lineWidth = 4; g.strokeStyle = 'rgba(40,26,54,0.88)'; g.strokeText(str, x, y); g.fillStyle = fill; g.fillText(str, x, y); };
    // A rope from a to the ring that hangs from the link at p: a pull on the rope, or the chest's own weight on it, swings the
    // ring out toward a, and a slack rope leaves it hanging; the rope ends in a hook round the far side of the ring. It sags
    // by sag of its length, trembles when it is pulled taut, and shows its twist all along.
    const rope = (a, p, sag = 0, taut = false, pull = taut) => {
      const far = RING.reach * K, d0 = Math.hypot(a.x - p.x, a.y - p.y) || 1, u = pull ? { x: (a.x - p.x) / d0, y: (a.y - p.y) / d0 } : { x: 0, y: 1 };
      this.drawRing(g, p, u.x, u.y);
      const h = { x: p.x + u.x * far, y: p.y + u.y * far };   // the middle of the ring's band on its far side: the hook is round it
      // the hook's shank lies the way the rope leaves it (w), the rope's end b tied to it
      const dh = Math.hypot(a.x - h.x, a.y - h.y) || 1, wx = (a.x - h.x) / dh, wy = (a.y - h.y) / dh + 2 * sag, dw = Math.hypot(wx, wy), w = { x: wx / dw, y: wy / dw };
      const b = { x: h.x + w.x * HOOK, y: h.y + w.y * HOOK };
      const d = Math.hypot(b.x - a.x, b.y - a.y), m = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 + sag * d + (taut ? 0.9 * Math.sin(now / 31) : 0) };
      const on = q => ({ x: (1 - q) * (1 - q) * a.x + 2 * q * (1 - q) * m.x + q * q * b.x, y: (1 - q) * (1 - q) * a.y + 2 * q * (1 - q) * m.y + q * q * b.y });
      g.save(); g.lineCap = 'round';
      for (const [lw, col] of [[7, '#6B4A2A'], [4.5, '#D9AE72']]) { g.strokeStyle = col; g.lineWidth = lw; g.beginPath(); g.moveTo(a.x, a.y); g.quadraticCurveTo(m.x, m.y, b.x, b.y); g.stroke(); }
      g.strokeStyle = 'rgba(107,74,42,0.8)'; g.lineWidth = 1.2;
      for (let s = 5; s < d - 2; s += 6) {
        const q = on(s / d), n = on(Math.min(1, (s + 1) / d)), ang = Math.atan2(n.y - q.y, n.x - q.x) + 1;
        g.beginPath(); g.moveTo(q.x - 2.6 * Math.cos(ang), q.y - 2.6 * Math.sin(ang)); g.lineTo(q.x + 2.6 * Math.cos(ang), q.y + 2.6 * Math.sin(ang)); g.stroke();
      }
      // the hook: steel, its shank from the rope's end, its bend round the ring's band, its point coming back
      const th = Math.atan2(w.x, -w.y);
      for (const [lw, col] of [[4.6, '#3B4050'], [2.4, '#D7DCE6']]) {
        g.strokeStyle = col; g.lineWidth = lw; g.beginPath(); g.moveTo(b.x, b.y);
        g.lineTo(h.x - w.y * 2.8 + w.x, h.y + w.x * 2.8 + w.y); g.arc(h.x, h.y, 2.8, th, th + Math.PI);
        g.lineTo(h.x + w.y * 2.8 + w.x * 2.4, h.y - w.x * 2.8 + w.y * 2.4); g.stroke();
      }
      g.restore();
    };
    // The chest stands or hangs with its foot at O, turned by A (at: a point of it on the page). hook: the link its hooked ring
    // hangs from; pivot: what a hanging chest hangs from; ropes: the ropes that end in that ring.
    const ex = box.w / 2 - RING.x * K, ey = RING.y * K, ropes = [];
    let hook = null, pivot = null, O = null, A = 0;
    const at = (x, y) => ({ x: O.x + x * Math.cos(A) - y * Math.sin(A), y: O.y + x * Math.sin(A) + y * Math.cos(A) });
    if (c) {
      const im = this.props[L.open ? 'chestOpen' : 'chest'], r = seeded(11);
      // Hanging from the 捣蛋鬼's arms, or from the sprite where the sprite really is, the chest swings on the rope like a
      // weight (swing), the hooked ring up; let go of in mid-air it waits as it hung for the sprite.
      if (c.carried && sv) { const t = this.spTip(st.sprite === 'lift2' ? 'lift2' : 'lift'); pivot = { x: sv.x + t.x, y: sv.y + t.y }; } else if (c.hang) pivot = c.hang;
      if (pivot) {
        const a = this.swing(pivot, c.len) * (1 - (c.level || 0));
        hook = { x: pivot.x + c.len * Math.sin(a), y: pivot.y + c.len * Math.cos(a) };
        A = (HANG * (c.lift ?? 1) + a * 0.8) * (1 - (c.level || 0));
        gm.hung = { hook, rot: A };
        ropes.push([pivot, hook, 0, false, true]);
      } else { this.sw = null; if (c.still) { hook = c.still; A = c.rot; } }
      if (hook) { O = hook; O = at(-ex, ey); }
      else if (c.spin) { A = c.spin; O = { x: c.x, y: c.y - box.h / 2 }; O = at(0, box.h / 2); }
      else {
        O = { x: c.x, y: c.y };
        if (L.ropes || L.dropped) hook = at(ex, -ey);
        // the rope the 捣蛋鬼 dropped lies from its hook, still in the ring, along the ground until the sprite takes it up
        if (L.dropped) ropes.push([{ x: hook.x + 52, y: CHEST.y - 3 }, hook, 0.35]);
      }
      g.save(); g.translate(O.x, O.y); g.rotate(A);
      this.drawProp(g, im, 0, 0, this.art.anchor / this.prankster.anchor);
      // the gifts heap up out of it, each where its number puts it
      if (L.open) for (let i = 0; i < Math.min(gm.pile, 18); i++) giftBox(g, (r() - 0.5) * box.w * 0.62, -box.h + 6 - r() * 10 - Math.floor(i / 5) * 8, 9, (r() - 0.5) * 0.9);
      g.restore();
      // its rope to the chest on the ground, or falling with it: from the end of the rope its drawing holds, or from under its
      // arm on the side the ring is; taut while it pulls, slack while it does not
      if (L.loose && hook) ropes.push([{ x: hook.x + 30, y: hook.y - 46 }, hook, 0.25]);
      else if (pk && hook && !pivot && (L.ropes || c.still)) ropes.push([this.pkCell(pk.pose).rope != null ? this.pkEnd(pk) : { x: clamp(hook.x + 20, pk.x - 30, pk.x + 24), y: pk.y + 16 }, hook, L.slack ? 0.3 : 0.03, !L.slack, !L.slack || !!c.still]);
      // its two rings: hers hangs unless her fists hold it (her drawing's own ring), the other unless a rope's hook is in it
      if (!L.held || c.x == null) this.drawRing(g, at(-ex, -ey));
      if (!ropes.length) this.drawRing(g, at(ex, -ey));
      for (const one of ropes) rope(...one);
    } else this.sw = null;
    if (pk) this.drawPk(g, pk);
    // the sprite starts at the chest that came down: a '!' over it
    if (L.notice != null && sv) {
      const t = L.notice, a = k01(t, 0, 90) * (1 - k01(t, NOTICE - 150, NOTICE)), s = 0.6 + 0.4 * ease(k01(t, 0, 140));
      g.save(); g.globalAlpha = a; g.translate(sv.x + 16, sv.y - SPRITE_H * 0.62); g.rotate(0.15); g.scale(s, s);
      g.font = `900 28px ${SANS}`; g.textAlign = 'center'; g.lineWidth = 5; g.strokeStyle = '#fff'; g.strokeText('!', 0, 0); g.fillStyle = '#FF5A6E'; g.fillText('!', 0, 0);
      g.restore();
    }
    // straining under the chest it sweats
    if (L.sweat > 0 && pk) for (let i = 0; i < 3; i++) {
      const w = (now + i * 230) % 700;
      g.save(); g.globalAlpha = (1 - w / 700) * L.sweat; g.fillStyle = '#BFE6FF'; g.beginPath(); g.ellipse(pk.x - 22 - i * 9 - w * 0.04, pk.y - 44 - w * 0.05, 3, 5, -0.5, 0, Math.PI * 2); g.fill(); g.restore();
    }
    // a gift falls on the 捣蛋鬼, or a helper's on her head, and bounces into the chest
    for (const d of gm.drops) {
      const u = now - d.at, to = d.her || !pk ? { x: her.x + her.w / 2, y: her.y + 8 } : { x: pk.x, y: pk.y - PK_H * 0.42 }, b = d.big ? 20 : 11, into = c?.x != null ? c : { x: to.x + 60 };
      if (u < 0) continue;
      if (u < DROP) this.dropGift(g, d.icon, to.x, -30 + (to.y + 30) * (u / DROP) ** 2, b, u / 160);
      else if (u < DROP + 600) { const p = (u - DROP) / 600; this.dropGift(g, d.icon, to.x + (into.x - to.x) * p, to.y + (top - to.y) * p - 70 * Math.sin(Math.PI * p), b, u / 110); }
      if (u >= DROP && u < DROP + 240) {
        const q = (u - DROP) / 240;
        g.save(); g.globalAlpha = 1 - q; g.strokeStyle = '#FFE27A'; g.lineWidth = 3; g.lineCap = 'round';
        for (let i = 0; i < 6; i++) { const a = i * Math.PI / 3 + 0.3; g.beginPath(); g.moveTo(to.x + Math.cos(a) * (10 + 14 * q), to.y + Math.sin(a) * (10 + 14 * q)); g.lineTo(to.x + Math.cos(a) * (18 + 22 * q), to.y + Math.sin(a) * (18 + 22 * q)); g.stroke(); }
        g.restore();
      }
    }
    // she looks about at a danmaku; a moth flies out of the empty chest
    if (L.ask != null && L.ask < 1300) { g.save(); g.globalAlpha = 1 - k01(L.ask, 900, 1300); g.textAlign = 'center'; text('？', her.x + her.w * 0.82, her.y - 2 - 8 * bump(L.ask, 0, 300), `800 30px ${SANS}`, '#B79BE8'); g.restore(); }
    if (c?.x != null && L.open && L.moth > 0 && L.moth < 2600) {
      const m = L.moth, x = c.x + 26 * Math.sin(m / 190), y = top - 10 - m * 0.07, w = 4 + 3 * Math.abs(Math.sin(m / 45));
      g.save(); g.globalAlpha = 1 - k01(m, 2000, 2600); g.fillStyle = '#B9B2A6'; g.beginPath(); g.ellipse(x - w, y, w, 5, -0.4, 0, Math.PI * 2); g.ellipse(x + w, y, w, 5, 0.4, 0, Math.PI * 2); g.fill();
      g.fillStyle = '#7A7468'; g.fillRect(x - 1, y - 4, 2, 8); g.restore();
    }
    // the lightning set piece: its cowlick charges, then the bolt jumps to her head and the page flashes
    if (L.zap != null && pk) {
      const z = L.zap, tip = { x: pk.x, y: pk.y - PK_H * 0.58 }, head = { x: her.x + her.w / 2, y: her.y + her.h * 0.18 };
      if (z < 900) {
        const rad = 8 + 18 * (z / 900) + 3 * Math.sin(now / 40), gr = g.createRadialGradient(tip.x, tip.y, 0, tip.x, tip.y, rad);
        gr.addColorStop(0, '#FFF7C2'); gr.addColorStop(1, 'rgba(255,226,122,0)');
        g.save(); g.globalAlpha = 0.4 + 0.5 * (z / 900); g.fillStyle = gr; g.beginPath(); g.arc(tip.x, tip.y, rad, 0, Math.PI * 2); g.fill(); g.restore();
      } else if (z < 1250) {
        const q = (z - 900) / 350, r = seeded(Math.floor(z / 60)), pts = [tip];
        for (let i = 1; i < 6; i++) pts.push({ x: tip.x + (head.x - tip.x) * i / 6 + (r() - 0.5) * 36, y: tip.y + (head.y - tip.y) * i / 6 + (r() - 0.5) * 30 });
        pts.push(head);
        g.save(); g.globalAlpha = 1 - q * 0.6; g.lineJoin = 'round'; g.lineCap = 'round';
        for (const [w, col] of [[9, '#FFD45E'], [4, '#fff']]) { g.strokeStyle = col; g.lineWidth = w; g.beginPath(); pts.forEach((p, i) => (i ? g.lineTo(p.x, p.y) : g.moveTo(p.x, p.y))); g.stroke(); }
        g.globalAlpha = 0.45 * (1 - q); g.fillStyle = '#fff'; g.fillRect(0, 0, PAGE.w, PAGE.h); g.restore();
      }
    }
    // what a hit took off it, or a helper's gift gave it back, rises under the fight's board
    gm.nums.forEach((n, i) => {
      const u = now - n.at, x = PAGE.w / 2, y = 206 - u * 0.03 + (gm.nums.length - 1 - i) * 24;
      g.save(); g.globalAlpha = 1 - k01(u, 700, 1100); g.textAlign = 'center'; text(n.text, x, y, `800 ${n.small ? 15 : 22}px ${SANS}`, n.heal ? '#9BE8A6' : '#FFE27A'); g.restore();
    });
    // the fight's board along the top of the page: the countdown, what its life still asks for, and how to side with it
    if (v?.phase === 'tug') {
      // The countdown grows anxious as the minute runs out: from GAME.urge left it beats at each second, harder and harder,
      // warming from white to amber and growing; in the last 10 s (sprint) it turns red, grows on, beats twice a second in
      // the last 5, shakes and glows red behind, and what its life still asks for beats with it.
      const left = Math.max(0, v.left), sec = Math.ceil(left / 1000), w = 220, x = (PAGE.w - w) / 2, y = 92;
      const urge = k01(GAME.urge - left, 0, GAME.urge - GAME.sprint), last = k01(GAME.sprint - left, 0, GAME.sprint);
      const since = 1 - (left % 1000) / 1000, beat = urge > 0 ? Math.exp(-(left < 5000 ? since * 2 % 1 : since) * 7) : 0;
      const s = 1 + beat * (0.07 * urge + 0.16 * last), shake = last * (1 + 2 * last) * Math.sin(now / 23) * (0.4 + 0.6 * beat);
      const rgb = (p, q, k) => `rgb(${p.map((c, i) => Math.round(c + (q[i] - c) * k))})`;
      const col = last > 0 ? rgb([255, 177, 74], [255, 59, 92], last) : rgb([255, 255, 255], [255, 177, 74], urge);
      g.save(); g.textAlign = 'center'; g.textBaseline = 'middle';
      if (last > 0) {
        const r = g.createRadialGradient(PAGE.w / 2, 34, 4, PAGE.w / 2, 34, 70);
        r.addColorStop(0, `rgba(255,59,92,${(0.18 + 0.22 * beat) * last})`); r.addColorStop(1, 'rgba(255,59,92,0)');
        g.fillStyle = r; g.fillRect(PAGE.w / 2 - 70, 0, 140, 104);
      }
      g.save(); g.translate(PAGE.w / 2 + shake, 34); g.scale(s, s); text(`0:${String(sec).padStart(2, '0')}`, 0, 0, `800 ${Math.round(30 + 4 * urge + 12 * last)}px ${SANS}`, col); g.restore();
      const sh = 1 + 0.08 * beat * last;
      g.save(); g.translate(PAGE.w / 2, y - 18); g.scale(sh, sh); text(`还差 ${Math.max(1, Math.ceil(v.hp))} 电池`, 0, 0, `800 20px ${SANS}`, '#FFE27A'); g.restore();
      g.fillStyle = 'rgba(40,26,54,0.75)'; g.beginPath(); g.roundRect(x, y, w, 14, 7); g.fill();
      g.fillStyle = '#B79BE8'; g.beginPath(); g.roundRect(x + 2, y + 2, Math.max(8, (w - 4) * clamp(v.hp / v.max, 0, 1)), 10, 5); g.fill();
      const join = `发「${this.settings.pranksterKeyword}」帮它`;
      text(v.helpers.size ? `${join} · 已有 ${v.helpers.size} 人` : join, PAGE.w / 2, y + 30, `500 14px ${SANS}`, '#fff');
      if (now - v.joined < 2000) text(`${v.joiner} 入伙了`, PAGE.w / 2, y + 56, `800 16px ${SANS}`, '#D9C7FF');
      g.restore();
    }
    // What it says, in a narrow bubble of its own colour that never covers it: over its head where there is room above it,
    // else beside it on its roomier side. Hauling the chest off or leaving it shouts from where it was; beaten, knocked or
    // scared off it goes slowly and its bubble goes with it, and off the page it takes its bubble with it. Where her bubble
    // (bub) is in the way, it waits for hers to go.
    if (pk && v?.say && now - v.say.at < 3000 && !(DONE.has(v.phase) && (pk.x > PAGE.w || pk.y < 0))) {
      const at = ['leave', 'lost'].includes(v.phase) ? v.from : pk, b = { text: v.say.text, t: now - v.say.at, stroke: '#B79BE8', maxW: 112, at: { x: at.x, y: at.y } };
      const box = this.bubbleBox(g, { bubble: b, y: 0, s: 1 }, her), top = at.y - PK_H * 0.62, half = PK_H * 0.52;
      if (top - box.h - 22 >= 8) Object.assign(box, { x: clamp(at.x - box.w / 2 + 40, 10, PAGE.w - box.w - 10), y: top - box.h - 22, tip: { x: at.x + 10, y: top }, side: 0 });
      else {
        const side = PAGE.w - (at.x + half) >= box.w + 30 ? 1 : -1;
        Object.assign(box, { x: side > 0 ? at.x + half + 22 : at.x - half - 22 - box.w, y: clamp(at.y - box.h / 2, 8, PAGE.h - box.h - 8), tip: { x: at.x + side * half, y: at.y - 6 }, side });
      }
      if (!(bub && box.x < bub.x + bub.w && bub.x < box.x + box.w && box.y < bub.y + bub.h && bub.y < box.y + box.h)) this.drawBubble(g, b, box);
    }
  }

  // Her speech bubble's lines and box: over her head, or, where that runs off the top of the page (her on the 总督's deck, a
  // long nickname), beside her face on her roomier side from her eye line, clear of her hat and the emblem. One at a point
  // (the castle's gate) stays over it. side: 0 over her, 1 on her right, -1 on her left. Whether it fits over her head goes
  // by the top of her head standing (her feet less H), not of her drawing, which her jump's frames move by 60-70 px; and
  // once beside her (was, its side the frame before) it goes back only with a HOP more room, or her hops up and down the
  // 总督's stair would flip it back and forth in the middle of a line. A new bubble after none is placed afresh.
  bubbleBox(g, st, her, was) {
    const b = st.bubble, s = BUBBLE_SCALE;
    // a line never starts with closing punctuation: it stays on the line before, a little over the width
    const wrap = maxW => {
      g.save(); g.font = BUBBLE_FONT;
      const lines = [];
      let cur = '';
      for (const ch of [...b.text]) { if (ch === '\n') { lines.push(cur); cur = ''; continue; } if (g.measureText(cur + ch).width > maxW && !'！？。，、…～）」’”'.includes(ch)) { lines.push(cur); cur = ch; } else cur += ch; }
      lines.push(cur);
      const w = Math.max(...lines.map(l => g.measureText(l).width)) + 26 * s;
      g.restore();
      return { lines, w, h: (lines.length * 24 + 16) * s };
    };
    const up = wrap((b.maxW || 230) * s), anchor = b.at ? { x: b.at.x, y: b.at.y - 90 } : { x: her.x + her.w * 0.62, y: her.y - 6 };
    if (b.at || st.y - H * st.s - 6 - up.h - 14 * s >= 8 + (was ? HOP : 0)) return { ...up, x: clamp(anchor.x - up.w / 2 + 30 * s, 10, PAGE.w - up.w - 10), y: clamp(anchor.y - up.h - 14 * s, 8, PAGE.h - up.h - 8), tip: anchor, side: 0 };
    const l = her.x + her.w * 0.15, r = her.x + her.w * 0.85, side = PAGE.w - r >= l ? 1 : -1;  // her head's edges
    const beside = wrap(Math.min(230 * s, (side > 0 ? PAGE.w - r : l) - 10 - 14 * s - 26 * s));
    return { ...beside, x: side > 0 ? r + 14 * s : l - 14 * s - beside.w, y: clamp(her.y + her.h * 0.3, 8, PAGE.h - beside.h - 8), tip: { x: side > 0 ? r : l, y: her.y + her.h * 0.42 }, side };
  }

  drawBubble(g, b, { lines, x, y, w, h, tip, side }) {
    const pop = 0.6 + 0.4 * ease(k01(b.t, 0, 160)), alpha = k01(b.t, 0, 120), s = BUBBLE_SCALE;
    g.save();
    // it pops out of its edge by her: its bottom over her head, its side beside her face
    const ox = x + w * (1 - side) / 2, oy = side ? y + h / 2 : y + h;
    g.globalAlpha = alpha;
    g.translate(ox, oy); g.scale(pop, pop); g.translate(-ox, -oy);
    g.fillStyle = '#fff'; g.strokeStyle = b.stroke || '#F0A3C0'; g.lineWidth = 2.5 * s;
    g.beginPath(); g.roundRect(x, y, w, h, 14 * s); g.fill(); g.stroke();
    // the tail, drawn pointing down from that edge, is turned to point sideways at her cheek beside her face
    g.save();
    if (side) g.transform(0, 1, -side, 0, ox, clamp(tip.y, y + 22 * s, y + h - 22 * s)); else g.translate(clamp(tip.x, x + 22 * s, x + w - 22 * s), oy);
    g.beginPath(); g.moveTo(-9 * s, -1); g.lineTo(-4 * s, 13 * s); g.lineTo(8 * s, -1); g.closePath(); g.fill();
    g.beginPath(); g.moveTo(-9 * s, 0); g.lineTo(-4 * s, 13 * s); g.lineTo(8 * s, 0); g.stroke();
    g.restore();
    g.font = BUBBLE_FONT; g.fillStyle = '#4A3742'; g.textBaseline = 'top';
    lines.forEach((l, i) => g.fillText(l, x + 13 * s, y + (9 + i * 24) * s));
    g.restore();
  }

  // An impact shakes the whole corner for a moment, harder for bigger moments (st.shake: [from, px, ms]).
  shakeOf(st, now) {
    let x = 0, y = 0;
    for (const [from, amp, dur] of st.shake || []) {
      const u = (st._t ?? now) - from;
      if (u >= 0 && u < dur) { const k = amp * (1 - u / dur); x += k * Math.sin(u / 17); y += k * Math.cos(u / 23); }
    }
    return { x, y };
  }
}
