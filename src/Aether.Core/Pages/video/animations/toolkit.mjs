// Geometry and drawing algorithms lifted from EV1–EV4 at 0464bd8.
export const C0 = [960, 540];
export const canvasOf = (w, h, draw) => { const c = document.createElement('canvas'); c.width = w; c.height = h; draw(c.getContext('2d')); return c; };
export const inside = (poly, x, y) => { let c = false; for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) { const a = poly[i], b = poly[j]; if ((a[1] > y) !== (b[1] > y) && x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0]) c = !c; } return c; };
export const k = (a, b, x) => Math.min(1, Math.max(0, (x - a) / (b - a)));
export const on2 = x => Math.floor(x / 83) * 83;
export const rnd = (i, s) => { const v = Math.sin(i * 127.1 + s * 311.7) * 43758.5453; return v - Math.floor(v); };
export const trace = (c, pts) => { c.beginPath(); pts.forEach((p, i) => i ? c.lineTo(p[0], p[1]) : c.moveTo(p[0], p[1])); };
const clipTo = (c, poly) => { trace(c, poly); c.closePath(); c.clip(); };
export const out = p => 1 - Math.pow(1 - p, 3);
export const inq = p => p * p;
export const quint = p => 1 - Math.pow(1 - p, 5);
export const smooth = p => p * p * (3 - 2 * p);
export const mix = (a, b, p) => a + (b - a) * p;
export const lerp = (a, b, f) => [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f];

// Real time keeps running through a hit stop while everything else holds. A stop is [logical time, real duration].
export function logicalTime(t, stops) {
  let held = 0;
  for (const [start, duration] of stops) {
    if (t < start + held) break;
    if (t < start + held + duration) return start;
    held += duration;
  }
  return t - held;
}
export const realTimes = stops => { let held = 0; return stops.map(([start, duration]) => { const at = start + held; held += duration; return at; }); };

export const spline = pts => {
  const out = [], seg = [];
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(pts.length - 1, i + 2)];
    const n = Math.max(2, Math.ceil(Math.hypot(p2[0] - p1[0], p2[1] - p1[1]) / 10));
    for (let j = 0; j < n; j++) { const t = j / n, t2 = t * t, t3 = t2 * t; out.push([0, 1].map(d => 0.5 * (2 * p1[d] + (p2[d] - p0[d]) * t + (2 * p0[d] - 5 * p1[d] + 4 * p2[d] - p3[d]) * t2 + (3 * p1[d] - p0[d] - 3 * p2[d] + p3[d]) * t3))); seg.push(i); }
  }
  out.push(pts[pts.length - 1]); seg.push(pts.length - 2); return { pts: out, seg };
};
export const pathOf = (pts, back) => {
  if (pts.length < 2) pts = [[960, 540], [961, 540]];
  const n = pts.length, L = [0], N = [];
  for (let i = 1; i < n; i++) L.push(L[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]));
  for (let i = 0; i < n; i++) { const a = pts[Math.max(0, i - 1)], b = pts[Math.min(n - 1, i + 1)], dx = b[0] - a[0], dy = b[1] - a[1], m = Math.hypot(dx, dy) || 1; N.push([-dy / m, dx / m]); }
  const tot = L[n - 1];
  const at = u => {
    const s = Math.min(1, Math.max(0, u)) * tot; let lo = 0, hi = n - 1;
    while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (L[mid] <= s) lo = mid; else hi = mid; }
    const f = (s - L[lo]) / ((L[hi] - L[lo]) || 1), p = pts[lo], q = pts[hi];
    return [p[0] + (q[0] - p[0]) * f, p[1] + (q[1] - p[1]) * f, N[lo][0] + (N[hi][0] - N[lo][0]) * f, N[lo][1] + (N[hi][1] - N[lo][1]) * f, back ? back[lo] : false];
  };
  return { pts, at };
};

// The cuts a straight blade leaves: a line run right across the sky, past the edge of everything the camera can see.
export const reach = (p, q, radius = 5000) => { const d = [p[0] - q[0], p[1] - q[1]], m = Math.hypot(d[0], d[1]), u = [d[0] / m, d[1] / m], w = [p[0] - C0[0], p[1] - C0[1]], b = w[0] * u[0] + w[1] * u[1], t = -b + Math.sqrt(b * b - (w[0] * w[0] + w[1] * w[1] - radius * radius)); return [p[0] + u[0] * t, p[1] + u[1] * t]; };
export const chord = (a, b, radius) => [reach(a, b, radius), reach(b, a, radius)];
export const chordOf = (path, u0, radius) => chord(path.at(u0), path.at(1), radius);
export const scarsOf = (paths, tails, radius) => tails.map((u, i) => chordOf(paths[i], u, radius));
// The stretch of each scar the camera can see, for embers to peel off.
export const seamsOf = (scars, view = [-60, -60, 1980, 1140], count = 1000) => scars.map(([a, b]) => pathOf(
  Array.from({ length: count + 1 }, (_, i) => [a[0] + (b[0] - a[0]) * i / count, a[1] + (b[1] - a[1]) * i / count])
    .filter(p => p[0] > view[0] && p[0] < view[2] && p[1] > view[1] && p[1] < view[3])));
// Each side of a line, as a polygon closed round a big circle.
export const sidesOf = line => [1, -1].map(dir => {
  const ang = p => Math.atan2(p[1] - C0[1], p[0] - C0[0]), a0 = ang(line[0]), a1 = ang(line[line.length - 1]);
  let da = a0 - a1; if (dir > 0) while (da <= 0) da += 2 * Math.PI; else while (da >= 0) da -= 2 * Math.PI;
  return [...line, ...Array.from({ length: 65 }, (_, i) => [C0[0] + 5000 * Math.cos(a1 + da * i / 64), C0[1] + 5000 * Math.sin(a1 + da * i / 64)])];
});

function shakeAt(t, t0, amp, dur, dir) {
  const p = k(t0, t0 + dur, t); if (p <= 0 || p >= 1) return [0, 0]; const a = amp * (1 - p) * (1 - p);
  if (!dir) return [Math.sin(t * .21) * a, Math.cos(t * .17) * a * .6];
  const s = Math.sin(t * .21) * a, c = Math.cos(t * .17) * a * .25; return [dir[0] * s - dir[1] * c, dir[1] * s + dir[0] * c];
}
// Shake runs on real time. A shake is [start, amplitude, duration, direction]; with a direction it mostly moves along it.
export const shakes = (t, list) => list.reduce(([x, y], shake) => { const [dx, dy] = shakeAt(t, ...shake); return [x + dx, y + dy]; }, [0, 0]);
export const projection = (cx, cy, z, rot, sx, sy) => q => { const x = (q[0] - cx) * z, y = (q[1] - cy) * z, c = Math.cos(rot), s = Math.sin(rot); return { x: 960 + sx + x * c - y * s, y: 540 + sy + x * s + y * c }; };

// What one play owns on the GPU: the runtime disposes of it when the play ends.
export function resources(P) {
  const F = P.filters, textures = [], filters = [];
  const tex = img => { const texture = P.Texture.from(img); if (img instanceof HTMLCanvasElement) textures.push(texture); return texture; };
  const filter = instance => { filters.push(instance); return instance; };
  const silhouette = (img, color) => tex(canvasOf(img.width, img.height, g => { g.drawImage(img, 0, 0); g.globalCompositeOperation = 'source-in'; g.fillStyle = color; g.fillRect(0, 0, img.width, img.height); }));
  const radial = stops => tex(canvasOf(512, 512, g => { const r = g.createRadialGradient(256, 256, 0, 256, 256, 256); for (const [o, c] of stops) r.addColorStop(o, c); g.fillStyle = r; g.fillRect(0, 0, 512, 512); }));
  // Each hit sets its own strength and centre per frame.
  const post = {
    bloom: filter(new F.AdvancedBloomFilter({ threshold: 0.9, bloomScale: 1.15, brightness: 1, blur: 7, quality: 5 })),
    shock: filter(new F.ShockwaveFilter({ center: { x: 0, y: 0 }, speed: 1500, amplitude: 54, wavelength: 240, brightness: 1.15, radius: -1 })),
    shock2: filter(new F.ShockwaveFilter({ center: { x: 0, y: 0 }, speed: 1100, amplitude: 30, wavelength: 420, brightness: 1.08, radius: -1 })),
    zoom: filter(new F.ZoomBlurFilter({ strength: 0, center: { x: 0, y: 0 }, innerRadius: 60 })),
    rgb: filter(new F.RGBSplitFilter({ red: { x: 0, y: 0 }, green: { x: 0, y: 0 }, blue: { x: 0, y: 0 } }))
  };
  return { tex, silhouette, radial, filter, post, dispose: () => { for (const texture of textures) texture.destroy(true); for (const instance of filters) instance.destroy(); } };
}

export const stroke = (g, w, color) => { g.lineWidth = w; g.strokeStyle = color; g.stroke(); };
export function beginFx(canvases, transform) {
  for (const c of canvases) { c.setTransform(1, 0, 0, 1, 0, 0); c.clearRect(0, 0, c.canvas.width, c.canvas.height); c.setTransform(...transform); c.globalCompositeOperation = 'lighter'; c.lineCap = 'round'; c.lineJoin = 'round'; }
}

// A slash: a sharp crescent whose head is the blade tip, held bright through its hit stop, then frayed into strands.
// EV1 measures its strands over the whole path instead of the lit stretch: span, base and fraySeed are how it says so.
export const slash = ({ band, arcLine }, palette, P, { head, tail, head2, width, fray, seed, alpha = 1, canvas, span = head - tail, base = tail, fraySeed = seed + 2 }) => {
  const a = (1 - fray) * alpha, w = width * (1 - 0.55 * fray);
  band(P, tail, head, w * 2.6, 0.25, palette.slashAura, 0.16 * a, canvas);
  band(P, tail, head, w, 0.08, palette.slash, 0.8 * a, canvas);
  band(P, tail, head, w * 0.34, 0.03, palette.slashCore, a, canvas);
  for (let s = 0; s < 7; s++) {
    const off = 10 + s * w * 0.12, len = (0.55 + rnd(s, seed) * 0.4) * span;
    arcLine(P, Math.max(tail, head - len), head - rnd(s, seed + 1) * 0.08 * span, off, 1 + (s % 2), 'rgba(' + palette.strand[0] + ',' + (palette.strand[1] - s * 18) + ',' + (palette.strand[2] - s * 18) + ',' + (0.55 * a) + ')', canvas);
  }
  arcLine(P, Math.max(tail, head2 - 0.7 * span), head2, w * 1.35, 2, 'rgba(255,255,255,' + 0.8 * a + ')', canvas);
  if (fray > 0) {
    for (let s = 0; s < 9; s++) {
      const off = (s / 8 - 0.3) * 90 + fray * 50 * (s - 3), v0 = 0.08 + fray * (0.2 + rnd(s, fraySeed) * 0.5), v1 = 1 - fray * (0.1 + rnd(s, fraySeed + 1) * 0.6);
      if (v1 > v0) arcLine(P, base + span * v0, base + span * v1, off, 1 + rnd(s, fraySeed + 2) * 1.6, 'rgba(' + palette.fray[0] + ',' + (palette.fray[1] + s * 12) + ',' + palette.fray[2] + ',' + (0.6 * (1 - fray)) + ')', canvas);
    }
  }
};

// sample supplies position, normal and seed so fixed clusters and random blade-tip trails share the same particles.
export function sparks(g, elapsed, count, power, seed, sample, color) {
  if (elapsed <= 0 || elapsed >= 1000) return;
  for (let i = 0; i < count; i++) {
    const life = 420 + rnd(i, seed) * 560; if (elapsed > life) continue;
    const [x0, y0, nx, ny] = sample(i, seed), side = rnd(i, seed + 2) < .5 ? -1 : 1;
    const v = (300 + rnd(i, seed + 3) * 1300) * power, s = elapsed / 1000, spread = (rnd(i, seed + 4) - .5) * .9;
    const dx = nx * side + spread, dy = ny * side + spread * .3, px = x0 + dx * v * s, py = y0 + dy * v * s + 500 * s * s, q = 1 - elapsed / life;
    g.strokeStyle = 'rgba(' + (i % 4 ? color : '255,255,255') + ',' + q + ')'; g.lineWidth = 1.5 + rnd(i, seed + 5) * 2.5;
    g.beginPath(); g.moveTo(px, py); g.lineTo(px - dx * v * .03, py - dy * v * .03); g.stroke();
  }
}

export function drawing(g, gb, palette, segments = 90) {
  const band = (P, u0, u1, w, inner, rgb, alpha, cv) => {
    if (u1 - u0 < 0.002) return;
    const N = segments;
    let prev = null;
    for (let i = 0; i <= N; i++) {
      const f = i / N, [px, py, nx, ny, back] = P.at(u0 + (u1 - u0) * f);
      const ww = w * Math.pow(Math.sin(Math.PI * Math.min(1, f * 1.08)), 0.5) * (0.25 + 0.75 * f);
      const o = [px - nx * ww, py - ny * ww], q = [px + nx * ww * inner, py + ny * ww * inner];
      if (prev) {
        const c = cv || (back ? gb : g);
        c.fillStyle = 'rgba(' + rgb + ',' + (alpha * Math.pow(f, 0.9)) + ')';
        c.beginPath(); c.moveTo(prev[0][0], prev[0][1]); c.lineTo(o[0], o[1]); c.lineTo(q[0], q[1]); c.lineTo(prev[1][0], prev[1][1]); c.closePath(); c.fill();
      }
      prev = [o, q];
    }
  };
  const arcLine = (P, u0, u1, off, width, color, cv) => {
    if (u1 - u0 < 0.002) return;
    const c = cv || g;
    c.strokeStyle = color; c.lineWidth = width; c.beginPath();
    let pen = false;
    for (let i = 0; i <= 40; i++) {
      const [px, py, nx, ny, back] = P.at(u0 + (u1 - u0) * i / 40);
      if (back && !cv) { pen = false; continue; }
      pen ? c.lineTo(px - nx * off, py - ny * off) : c.moveTo(px - nx * off, py - ny * off); pen = true;
    }
    c.stroke();
  };
  const glint = (x, y, p, len, height = len * 0.6) => {
    if (p <= 0 || p >= 1) return;
    const a = Math.sin(p * Math.PI);
    g.strokeStyle = 'rgba(255,255,255,' + a + ')'; g.lineWidth = 2.5;
    g.beginPath(); g.moveTo(x - len * a, y); g.lineTo(x + len * a, y); g.moveTo(x, y - height * a); g.lineTo(x, y + height * a); g.stroke();
  };
  const draw = (path, options) => slash({ band, arcLine }, palette, path, options);
  return {
    glint,
    slash: draw,
    // A slash that lands on the pose at hit and frays from end, hold ms before it is gone.
    swing: (path, l, { from, hit, end = hit, hold, tail, ...options }) => {
      if (l >= from + 4 && l < end + hold) draw(path, { head: out(k(from + 4, hit - 4, l)), tail: tail * out(k(from + 20, hit, l)), head2: out(k(from + 12, hit + 8, l)), fray: k(end, end + hold, l), ...options });
    },
    // Sparks thrown off a hit at the blade tip, mostly across the cut.
    tipSparks: (path, l, t0, count, power, seed) => sparks(g, l - t0, count, power, seed, (i, s) => path.at(.82 + rnd(i, s + 1) * .18), palette.spark)
  };
}

// Motes drawn in to a blade, hilt to tip, while a wind-up holds; with no tip they all go to the hilt.
export function gather(g, l, { hilt, tip = hilt, start, span, count, color, seed = 1, life = [300, 260], ring = [260, 360], reach = 1, gap = 1 }) {
  for (let i = 0; i < count; i++) {
    const s0 = start + rnd(i, seed) * span, dur = life[0] + rnd(i, seed + 1) * life[1], p = (l - s0) / dur;
    if (p < 0 || p > 1) continue;
    const to = lerp(hilt, tip, rnd(i, seed + 6)), ang = rnd(i, seed + 2 + gap) * Math.PI * 2, r0 = (ring[0] + rnd(i, seed + 3 + gap) * ring[1]) * reach, r = r0 * (1 - p * p), r2 = r0 * (1 - Math.max(0, p - 0.12) ** 2);
    g.strokeStyle = i % 3 ? 'rgba(' + color + ',' + (0.9 * Math.sin(p * Math.PI)) + ')' : 'rgba(255,255,255,' + Math.sin(p * Math.PI) + ')';
    g.lineWidth = (1.5 + rnd(i, seed + 4 + gap) * 2) * reach;
    g.beginPath(); g.moveTo(to[0] + Math.cos(ang) * r, to[1] + Math.sin(ang) * r); g.lineTo(to[0] + Math.cos(ang) * r2, to[1] + Math.sin(ang) * r2); g.stroke();
  }
}
// EV1 and EV2's charge: sparks pulled into the hilt from further out, over longer, on seeds laid out one closer.
export const charge = (g, l, { hilt, span, count, color }) => gather(g, l, { hilt, start: 650, span, count, color, life: [380, 260], ring: [360, 420], gap: 0 });

export function glow(c, x, y, r, rgb, a) {
  if (a <= 0.003) return;
  const gr = c.createRadialGradient(x, y, 0, x, y, r); gr.addColorStop(0, 'rgba(' + rgb + ',' + a + ')'); gr.addColorStop(1, 'rgba(' + rgb + ',0)');
  c.fillStyle = gr; c.beginPath(); c.arc(x, y, r, 0, Math.PI * 2); c.fill();
}

// Kicked-up grit, thrown one way along the ground.
export function spray(g, l, palette, x0, y0, t0, n, dir, seed) {
  const sp = l - t0; if (sp <= 0 || sp >= 800) return;
  for (let i = 0; i < n; i++) {
    const life = 300 + rnd(i, seed) * 480; if (sp > life) continue;
    const q = sp / life, s = sp / 1000, v = 300 + rnd(i, seed + 1) * 1500, up = 300 + rnd(i, seed + 2) * 1300;
    const px = x0 + dir * v * s + (rnd(i, seed + 3) - 0.5) * 200, py = Math.min(y0, y0 - up * s + 1800 * s * s), n2 = 18 + rnd(i, seed + 4) * 40;
    g.strokeStyle = 'rgba(' + (i % 5 ? palette.grit : palette.gritCore) + ',' + (1 - q) + ')'; g.lineWidth = 2 + rnd(i, seed + 5) * 3.5;
    g.beginPath(); g.moveTo(px, py); g.lineTo(px - dir * n2, py + (rnd(i, seed + 6) - 0.5) * 16); g.stroke();
  }
}

// A column of light from the ground up to where he has gone, narrowing and fading as f runs from 0 to 1.
export function pillar(gb, palette, x, y, top, width, f) {
  const w = width * (1 - f) * (1 - f) + 4;
  for (const [m, rgb, a] of palette.pillar) { gb.fillStyle = 'rgba(' + rgb + ',' + a * (1 - f) + ')'; gb.beginPath(); gb.moveTo(x - w * m, y); gb.lineTo(x + w * m, y); gb.lineTo(top[0] + 40, top[1]); gb.lineTo(top[0] + 20, top[1]); gb.closePath(); gb.fill(); }
}

// Stones flung up out of the ground, tumbling; q is how far through the burst they are.
export function stones(g, l, color, x0, y0, t0, q, count, seed) {
  for (let i = 0; i < count; i++) {
    const s = (l - t0) / 1000, a0 = -1.57 + (rnd(i, seed) - 0.5) * 1.5, v = 700 + rnd(i, seed + 1) * 1500, x = x0 + Math.cos(a0) * v * s, y = y0 + Math.sin(a0) * v * s + 900 * s * s, r = 6 + rnd(i, seed + 2) * 20, a2 = rnd(i, seed + 3) * 6 + s * 9;
    g.fillStyle = 'rgba(' + color[0] + ',' + (color[1] + (i % 3) * 40) + ',' + color[2] + ',' + (1 - q) + ')'; g.beginPath(); for (let j = 0; j < 4; j++) { const b = a2 + j * 1.57; j ? g.lineTo(x + Math.cos(b) * r, y + Math.sin(b) * r) : g.moveTo(x + Math.cos(b) * r, y + Math.sin(b) * r); } g.closePath(); g.fill();
  }
}

// Embers peel off the lips of every seam as the sky opens. shear pushes EV1's along the seam as well as off it.
export function seamEmbers(g, l, { start, span, count, seams, color, drift = 0, y = 0, shear = 0 }) {
  if (l < start || l >= start + span) return;
  const e = l - start;
  for (let i = 0; i < count; i++) {
    const t0 = rnd(i, 30) * 520, life = 600 + rnd(i, 31) * 700, q = (e - t0) / life;
    if (q <= 0 || q >= 1) continue;
    const [x0, y0, nx, ny] = seams[i % seams.length].at(rnd(i, 32)), side = i % 2 ? 1 : -1, v = 30 + rnd(i, 33) * 110;
    const px = x0 + (nx * v * 1.4 + ny * shear) * side * q + drift, py = y0 + (ny * v * 1.4 - nx * shear) * side * q - 40 * q + y;
    g.fillStyle = 'rgba(' + color[0] + ',' + (color[1] + (i % 3) * 50) + ',' + color[2] + ',' + Math.sin(q * Math.PI) * 0.9 + ')';
    g.beginPath(); g.arc(px, py, 1.2 + rnd(i, 34) * 2.2, 0, Math.PI * 2); g.fill();
  }
}

// Embers rise during the zanshin hold.
export function risingEmbers(g, l, { from, to, color, fadeOut = [to - 400, to], count = 46, across = [200, 1700], rise = [1000, 520, 200] }) {
  if (l <= from || l >= to) return;
  const fade = k(from, from + 500, l) * (1 - k(fadeOut[0], fadeOut[1], l));
  for (let i = 0; i < count; i++) {
    const life = 2200 + rnd(i, 16) * 1800, ph = ((l + rnd(i, 17) * life) % life) / life;
    const px = across[0] + rnd(i, 18) * across[1] + Math.sin(ph * 6 + i) * 20, py = rise[0] - ph * rise[1] - rnd(i, 19) * rise[2];
    g.fillStyle = 'rgba(' + color[0] + ',' + (color[1] + (i % 3) * 50) + ',' + color[2] + ',' + (Math.sin(ph * Math.PI) * fade * 0.8) + ')';
    g.beginPath(); g.arc(px, py, 1.5 + rnd(i, 20) * 2.5, 0, Math.PI * 2); g.fill();
  }
}

// The straight cuts hang behind the hero from each hit, full length. Once the last lands the others dim, so its cut holds
// the frame; before the break they flare once each, in cut order.
export function flareScars(gb, l, palette, { scars, hits, split, flare, step, dim: [amount, dimFrom, dimTo], drift, y = 0, open }) {
  const flick = 0.8 + 0.2 * Math.sin(l * 0.045);
  scars.forEach((pts, i) => {
    const a = k(hits[i], hits[i] + 120, l) * (1 - k(split + 450, split + 950, l));
    if (a <= 0) return;
    const fl = Math.sin(Math.PI * k(flare + step * i, flare + step * i + 160, l)), wide = Math.max(open, 0.6 * fl);
    const pa = a * (i < scars.length - 1 ? Math.max(1 - amount * k(dimFrom, dimTo, l), fl, open) : 1);
    gb.save(); gb.translate(drift, y); trace(gb, pts);
    stroke(gb, 12 + 40 * wide, 'rgba(' + palette.seamGlow + ',' + (0.2 + 0.25 * fl) * pa * flick + ')');
    stroke(gb, 3 + 10 * wide, 'rgba(' + palette.seamEdge + ',' + (0.6 + 0.3 * fl) * pa + ')');
    stroke(gb, 1.4 + 3 * wide, 'rgba(255,255,255,' + pa * (1 - 0.6 * open) + ')');
    gb.restore();
  });
}

// Once open, each piece carries a lit lip along its own edges.
export function pieceLips(gb, pieces, scars, palette, alpha) {
  for (const p of pieces) {
    gb.save();
    gb.translate(p.c.pivot.x + p.dx, p.c.pivot.y + p.dy); gb.rotate(p.r); gb.translate(-p.c.pivot.x, -p.c.pivot.y);
    for (const region of p.regions ?? [p.poly]) clipTo(gb, region);
    for (const pts of scars) { trace(gb, pts); stroke(gb, 10, 'rgba(' + palette.seamLip + ',' + 0.35 * alpha + ')'); stroke(gb, 2, 'rgba(' + palette.seamCore + ',' + 0.9 * alpha + ')'); }
    gb.restore();
  }
}

// The bare steel catches the light as it slides home.
export function steelGlint(g, l, start, end, a, b) {
  if (l < start || l >= end) return;
  g.strokeStyle = 'rgba(255,255,255,' + 0.85 * Math.sin(k(start, end, l) * Math.PI) + ')'; g.lineWidth = 3;
  g.beginPath(); g.moveTo(a[0], a[1]); g.lineTo(b[0], b[1]); g.stroke();
}

export function cutCells(scars, C0, RR, VIEW) {
  const clipHalf = (poly, [a, b], sgn) => {
    const side = p => sgn * ((b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])), out = [];
    for (let i = 0; i < poly.length; i++) {
      const p = poly[i], q = poly[(i + 1) % poly.length], sp = side(p), sq = side(q);
      if (sp >= 0) out.push(p);
      if ((sp >= 0) !== (sq >= 0)) { const t = sp / (sp - sq); out.push([p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t]); }
    }
    return out;
  };
  const disc = Array.from({ length: 96 }, (_, i) => [C0[0] + RR * Math.cos(i / 96 * 2 * Math.PI), C0[1] + RR * Math.sin(i / 96 * 2 * Math.PI)]);
  const cells = [];
  for (let m = 0; m < 1 << scars.length; m++) {
    let poly = disc;
    scars.forEach((line, i) => { if (poly.length >= 3) poly = clipHalf(poly, line, m >> i & 1 ? 1 : -1); });
    if (poly.length < 3) continue;
    const xs = poly.map(q => q[0]), ys = poly.map(q => q[1]);
    if (Math.max(...xs) < VIEW[0] || Math.min(...xs) > VIEW[2] || Math.max(...ys) < VIEW[1] || Math.min(...ys) > VIEW[3]) continue;
    let n = 0, cx = 0, cy = 0;
    for (let x = VIEW[0]; x <= VIEW[2]; x += 25) for (let y = VIEW[1]; y <= VIEW[3]; y += 25) if (inside(poly, x, y)) { n++; cx += x; cy += y; }
    cells.push({ poly, n, c: n ? [cx / n, cy / n] : poly.reduce((a, q) => [a[0] + q[0] / poly.length, a[1] + q[1] / poly.length], [0, 0]) });
  }
  return cells;
}
