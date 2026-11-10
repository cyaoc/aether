// The 桌宠 on the 桌宠页's canvas, drawn as the design package's board draws her (DP-DesktopPet.dc.html), from the current
// character's Q 版 pictures and the numbers in her manifest (characters/README.md, desktopPet).
import { loadImage } from '../video/animations/toolkit.mjs';
import { Engine, FOOT, GLOW, H, PAGE, SPRITE_H, clamp, ease, k01, xfade } from './engine.mjs';
import { BACK, drawFx } from './effects.mjs';
import { SANS } from '../gift-board/paint.mjs';

const SPRITE_SPEED = 0.8;
// The sprite keeps these drawings while it flies, since they carry something.
const CARRY = new Set(['crown', 'glow']);
// The sheets a show or 日常小动作 needs beyond those she always has: they load when it starts (the lottery's too as she waits
// through one), not with the page, which would decode all her pictures at once (design package README 11).
const SEGMENTS = { hum: ['daily1'], gym: ['gym10'], hike: ['hike11'], bike: ['cycle12'], ski: ['ski9'], castle: ['castleSheet'],
  captain: ['voyage'], admiral: ['voyage'], governor: ['voyage'], lottery: ['lottery'] };

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

/** The 桌宠 drawn on `canvas` from `character`, her manifest; Engine decides what she does. */
export class Pet extends Engine {
  constructor(canvas, master, shows, character) {
    super(master, shows, character.desktopPet);
    Object.assign(this, { canvas, character, cells: {}, strips: {}, tweens: {}, sstrips: {}, loads: {}, props: {}, trail: [] });
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

  // A sheet is 3 x 2 cells; each drawing anchors at its row's ground line and its own centre of mass (the figures are not
  // centred in their thirds, so the cell centre would shift her sideways between poses), and every sheet is scaled by its
  // first cell (the idle ruler). Her strips drawn from its poses come with it, and only then is it drawn from.
  async measureSheet(k) {
    const { sheets, poses: POSE, sameSpot: SAME_SPOT, body: BODY, strips: STRIP, transitions: TRANS } = this.art, on = p => POSE[p][0] === k;
    const im = await this.picture(sheets[k]), cw = im.width / 3, ch = im.height / 2, list = [];
    for (let i = 0; i < 6; i++) list.push(box(im, (i % 3) * cw, Math.floor(i / 3) * ch, cw, ch));
    const ground = [0, 1].map(r => Math.max(...list.slice(r * 3, r * 3 + 3).filter(Boolean).map(b => b.y + b.h)));
    const cells = list.map((b, i) => b && { im, ...b, ox: (i % 3) * cw, oy: Math.floor(i / 3) * ch, ground: ground[Math.floor(i / 3)], ruler: list[0].h });
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
      this.sstrips[key] = [0, 1, 2, 3].map(i => ({ im, ...box(im, i * im.width / 4, 0, im.width / 4, im.height), cx: c.cx - c.ox + i * im.width / 4, cy: c.cy - c.oy, ruler: c.ruler }));
    }));
  }

  start(name, opts) {
    for (const k of SEGMENTS[name] ?? []) this.loadSheet(k);
    super.start(name, opts);
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
    // a pose whose sheet is still loading: she keeps the drawing she has until it is there
    if (!this.cells[this.art.poses[st.pose][0]]) st.pose = this.hv.pose;
    const her = this.herBox(st);
    const target = this.spritePos(st, her, now), sp = target && this.follow(target, dt, st);
    if (sp) { this.trail = this.trail.filter(q => rt - q.rt < 420); if (st.sprite === 'glow') this.trail.push({ x: sp.x, y: sp.y, rt }); }
    for (const f of st.fx) if (BACK.has(f[0])) drawFx(g, f, st, her, now, this);
    if (sp && st.sBehind) this.drawSprite(g, st, sp, now);
    this.drawHer(g, st, now);
    if (sp && !st.sBehind) this.drawSprite(g, st, sp, now);
    if (st.gift) this.drawGift(g, st.gift);
    for (const f of st.fx) if (!BACK.has(f[0])) drawFx(g, f, st, her, now, this);
    if (st.bubble?.text) this.drawBubble(g, st.bubble, her);
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
    if (st.pose !== hv.pose) Object.assign(hv, { tween: this.route(hv.pose, st.pose), pose: st.pose, at: rt, since: this.clock });
    // she breathes in every pose, in-between frames too (breathing that stopped when she left her idle pose made her head
    // jump up to 3 px at the change): her drawing scaled at her feet, a touch taller and narrower, then shorter and wider
    const s = 1 + 0.012 * Math.sin(now / 600), c = hv.tween?.[Math.floor(el(hv.at) / 90)] || this.cell(hv.pose), b = this.herBox(st, c);
    g.save(); g.translate(st.x, st.y); g.scale(st.sx / Math.sqrt(s), s); g.translate(-st.x, -st.y);
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
  // it), ['at', x, y] (a page point), or the named spots below.
  spritePos(st, her, now) {
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
    if (st.sp[0] === 'rel') return { x: her.x + her.w * st.sp[1], y: her.y + her.h * st.sp[2] };
    if (st.sp[0] === 'at') return { x: st.sp[1], y: st.sp[2] };
    if (st.sp === 'head') return { x: her.x + her.w * 0.25, y: her.y - SPRITE_H * 0.2 + 3 * Math.sin(now / 500) };
    if (st.sp === 'knee') return { x: her.x + her.w * 0.94, y: FOOT.y - 42 + 2 * Math.sin(now / 900) };
    if (st.sp[0] === 'crown') { const top = { x: FOOT.x, y: her.y - SPRITE_H * 0.55 }; const p = st.sp[1], d = st.sp[2]; return { x: home.x + (top.x - home.x) * p, y: home.y + (top.y - home.y) * p + 26 * d }; }
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
    } else if (p.d > 14 && !CARRY.has(key)) {
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
      g.save(); g.globalAlpha = st.sAlpha * alpha; g.translate(p.x, p.y + (key === 'pat' ? 3 * Math.abs(Math.sin(now / 180)) : 0)); g.rotate(tilt);
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

  drawBubble(g, b, her) {
    const pop = 0.6 + 0.4 * ease(k01(b.t, 0, 160)), alpha = k01(b.t, 0, 120);
    g.save();
    g.font = `500 18px ${SANS}`;
    const maxW = 230, lines = [];
    let cur = '';
    // a line never starts with closing punctuation: it stays on the line before, a little over the width
    for (const ch of [...b.text]) { if (g.measureText(cur + ch).width > maxW && !'！？。，、…～）」’”'.includes(ch)) { lines.push(cur); cur = ch; } else cur += ch; }
    lines.push(cur);
    const w = Math.max(...lines.map(l => g.measureText(l).width)) + 26, h = lines.length * 24 + 16;
    const anchor = b.at ? { x: b.at.x, y: b.at.y - 90 } : { x: her.x + her.w * 0.62, y: her.y - 6 };
    const x = clamp(anchor.x - w / 2 + 30, 10, PAGE.w - w - 10), y = clamp(anchor.y - h - 14, 8, PAGE.h - h - 8);
    g.globalAlpha = alpha;
    g.translate(x + w / 2, y + h); g.scale(pop, pop); g.translate(-(x + w / 2), -(y + h));
    g.fillStyle = '#fff'; g.strokeStyle = '#F0A3C0'; g.lineWidth = 2.5;
    g.beginPath(); g.roundRect(x, y, w, h, 14); g.fill(); g.stroke();
    const tx = clamp(anchor.x, x + 22, x + w - 22);
    g.beginPath(); g.moveTo(tx - 9, y + h - 1); g.lineTo(tx - 4, y + h + 13); g.lineTo(tx + 8, y + h - 1); g.closePath(); g.fill();
    g.beginPath(); g.moveTo(tx - 9, y + h); g.lineTo(tx - 4, y + h + 13); g.lineTo(tx + 8, y + h); g.stroke();
    g.fillStyle = '#4A3742'; g.textBaseline = 'top';
    lines.forEach((l, i) => g.fillText(l, x + 13, y + 9 + i * 24));
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
