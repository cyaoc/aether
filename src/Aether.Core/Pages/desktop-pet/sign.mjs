// B站 PK (design package README 15): her sign, the board's SIGN, page px. Codex drew her holding a blank one; tools/sign.py
// wiped it out of her drawings, and her manifest's signs say where it was in each (characters/README.md). The page draws this
// one there, bigger than codex's every way (so it covers what was wiped), with the words: paper and a pink marker border as
// her 账单卡 has, a little uneven, as she cut it out herself. The engine says what it says (words), Pet draws it (draw).
import { SANS } from '../gift-board/paint.mjs';
import { PK_INTEL } from './pk-intel.mjs';

export const SIGN = (() => {
  // 252 wide: 差9999票 and 差9.9万票 fit at full size (user: “有的时候是差几千票、这时候放不下”; it was 206)
  const W = 252, H = 140, R = 13, HOLD = 21, PAD = 16, INK = '#5B3A4B', RED = '#E23A5E';
  // a rounded rectangle, its outline pushed in or out by under a pixel along the way (made at its first drawing: node, which
  // runs the engine, has no Path2D)
  let shape;
  const outline = () => {
    const path = new Path2D(), n = 240, P = 2 * (W - 2 * R) + 2 * (H - 2 * R) + 2 * Math.PI * R;
    const segs = [['t', W - 2 * R], ['tr', Math.PI * R / 2], ['r', H - 2 * R], ['br', Math.PI * R / 2], ['b', W - 2 * R], ['bl', Math.PI * R / 2], ['l', H - 2 * R], ['tl', Math.PI * R / 2]];
    const CORNER = { tr: [W - R, R, -90], br: [W - R, H - R, 0], bl: [R, H - R, 90], tl: [R, R, 180] };
    for (let i = 0; i < n; i++) {
      let s = i / n * P, k = 't', x, y, nx, ny;
      for ([k, ] of segs) { const L = segs.find(q => q[0] === k)[1]; if (s <= L) break; s -= L; }
      if (k === 't') [x, y, nx, ny] = [R + s, 0, 0, -1]; else if (k === 'r') [x, y, nx, ny] = [W, R + s, 1, 0];
      else if (k === 'b') [x, y, nx, ny] = [W - R - s, H, 0, 1]; else if (k === 'l') [x, y, nx, ny] = [0, H - R - s, -1, 0];
      else { const [cx, cy, a0] = CORNER[k], a = (a0 + s / (Math.PI * R / 2) * 90) * Math.PI / 180; [x, y, nx, ny] = [cx + R * Math.cos(a), cy + R * Math.sin(a), Math.cos(a), Math.sin(a)]; }
      const u = i / n * 2 * Math.PI, d = (Math.sin(3 * u + 0.7) * 0.5 + Math.sin(7 * u + 2.1) * 0.3) * 0.9;
      path[i ? 'lineTo' : 'moveTo'](x + nx * d, y + ny * d);
    }
    path.closePath();
    return path;
  };
  // lines: rows of [text, weight, px, red]; centred, rows 4 px apart; a run's pop (0..1) makes it a little bigger
  function draw(g, lines) {
    const path = shape ??= outline();
    g.save(); g.shadowColor = 'rgba(60,30,45,0.24)'; g.shadowBlur = 6; g.shadowOffsetX = 2; g.shadowOffsetY = 4; g.fillStyle = '#FFF7EC'; g.fill(path); g.restore();
    g.save(); g.lineWidth = 6; g.lineJoin = 'round'; g.strokeStyle = '#F0A3C0'; g.stroke(path); g.restore();
    const F = (w, px) => `${w} ${px}px ${SANS}`, rows = lines.map(r => ({ r, h: Math.max(...r.map(q => q[2])) }));
    g.save(); g.textBaseline = 'alphabetic'; g.textAlign = 'left';
    for (const row of rows) row.w = row.r.reduce((a, [s, w, px]) => (g.font = F(w, px), a + g.measureText(s).width), 0);
    let y = H / 2 - (rows.reduce((a, r) => a + r.h, 0) + 4 * (rows.length - 1)) / 2 + 2;
    for (const { r, h, w: rw } of rows) {
      let x = W / 2 - rw / 2;
      for (const [s, w, px, red, pop = 0] of r) {
        g.font = F(w, px); const tw = g.measureText(s).width, k = 1 + 0.18 * pop;
        g.save(); g.translate(x + tw / 2, y + h * 0.86 - px * 0.36); g.scale(k, k); g.fillStyle = red ? RED : INK; g.fillText(s, -tw / 2, px * 0.36); g.restore();
        x += tw;
      }
      y += h + 4;
    }
    g.restore();
  }
  // the words on it for the show kind: the double bonus's condition from B站's own (差10票翻倍, 差1人翻倍, 差3000票翻倍, maybe
  // 差1.2万票翻倍), 2倍, a countdown at n. What it cannot read it shows as B站 wrote it, on one line.
  const words = (kind, opts, n, pop) => {
    if (kind === 'pkDouble') return [[['2倍', 800, 64, true]], [['加成中！', 500, 36]]];
    if (kind !== 'pkCondition') return [[[kind === 'pkLastSeconds' ? '最后' : '双倍还剩', 500, 36]], [[String(n), 800, 72, true, pop]]];
    const m = /差\s*([\d.]+)\s*(万?)\s*(票|人)/.exec(opts.text || '差10票翻倍');
    if (!m) return [[[(opts.text || '').replace(/翻倍.*$/, ''), 800, 36]], [['翻倍！', 500, 36]]];
    // how many as the 对手情报卡 writes it: up to 9999 as it is, then in 万 (PK_INTEL.count)
    return [[['差', 800, 44], [PK_INTEL.count(Math.round(parseFloat(m[1]) * (m[2] ? 10000 : 1))), 800, 52, true], [m[3], 800, 44]], [['翻倍！', 500, 36]]];
  };
  return { W, H, HOLD, PAD, draw, words };
})();
