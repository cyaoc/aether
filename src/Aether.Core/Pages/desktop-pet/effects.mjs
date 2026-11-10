// The shows' effects (design package README 7.1), drawn by code on the 桌宠页's canvas, as the board draws them.
import { FOOT, H, PAGE, ease, k01, seeded } from './engine.mjs';

// Effects drawn behind her (glows, rays, rings on the ground, speed lines); the rest go in front. (seamGlow: the light of a
// blind box glows out around the box from behind her, so it does not whiten the box itself.)
export const BACK = new Set(['halo', 'rays', 'burst', 'shockwave', 'speed', 'spray', 'pillar', 'seamGlow']);

/** Draws effect `f`, `[type, from ms, ...]`, of state `st` around `her` box; `trail` is where the glowing sprite has just been. */
export function drawFx(g, f, st, her, now, trail) {
  const [type, from] = f, t = st._t == null ? now - from : st._t - from;
  if (t == null || t < 0) return;
  const head = { x: her.x + her.w / 2, y: her.y + her.h * 0.2 };
  // manga symbols sit at the upper left of her head, away from her face and from the bubble on the right
  const headL = { x: her.x + her.w * 0.14, y: her.y + her.h * 0.06 }, at = (fx, fy) => ({ x: her.x + her.w * fx, y: her.y + her.h * fy });
  const life = (u, inn, hold, out) => u < 0 ? 0 : u < inn ? ease(u / inn) : u < inn + hold ? 1 : Math.max(0, 1 - (u - inn - hold) / out);
  const r = seeded(type.length * 97 + Math.floor(from));
  const glow = a => `rgba(${st.glow || '255,236,170'},${a})`;
  const star = (x, y, rad, color, a) => { g.save(); g.globalAlpha = a; g.fillStyle = color; g.beginPath(); for (let i = 0; i < 8; i++) { const ang = i * Math.PI / 4, rr = i % 2 ? rad * 0.32 : rad; g.lineTo(x + Math.cos(ang) * rr, y + Math.sin(ang) * rr); } g.closePath(); g.fill(); g.restore(); };
  const heart = (x, y, s, a) => { g.save(); g.globalAlpha = a; g.fillStyle = '#FF7FA8'; g.beginPath(); g.moveTo(x, y + s * 0.3); g.bezierCurveTo(x, y, x - s * 0.5, y, x - s * 0.5, y + s * 0.3); g.bezierCurveTo(x - s * 0.5, y + s * 0.6, x, y + s * 0.8, x, y + s); g.bezierCurveTo(x, y + s * 0.8, x + s * 0.5, y + s * 0.6, x + s * 0.5, y + s * 0.3); g.bezierCurveTo(x + s * 0.5, y, x, y, x, y + s * 0.3); g.fill(); g.restore(); };
  const giftBox = (x, y, b, rot = 0) => { g.save(); g.translate(x, y); g.rotate(rot); g.fillStyle = '#FF9EB8'; g.strokeStyle = '#4A2E2A'; g.lineWidth = 1.5; g.beginPath(); g.roundRect(-b, -b, 2 * b, 2 * b, b * 0.3); g.fill(); g.stroke();
    g.fillStyle = '#E8454F'; g.fillRect(-b * 0.2, -b, b * 0.4, 2 * b); g.fillRect(-b, -b * 0.2, 2 * b, b * 0.4); g.beginPath(); g.ellipse(-b * 0.45, -b - b * 0.28, b * 0.42, b * 0.28, -0.5, 0, Math.PI * 2); g.ellipse(b * 0.45, -b - b * 0.28, b * 0.42, b * 0.28, 0.5, 0, Math.PI * 2); g.fill(); g.restore(); };
  switch (type) {
    case 'hearts': for (let i = 0; i < f[2]; i++) { const d = i * 180, u = t - d; if (u < 0 || u > 1400) continue; heart(head.x + (r() - 0.5) * 120, head.y - u * 0.09, 16 + r() * 8, 1 - u / 1400); } break;
    case 'sparkles': for (let i = 0; i < 10; i++) { const a = r() * Math.PI * 2, d = 60 + r() * 90, u = t - r() * 400; if (u < 0 || u > 1200) continue; star(head.x + Math.cos(a) * d, head.y + 30 + Math.sin(a) * d * 0.7, 9 + r() * 7, r() < 0.5 ? '#FFE27A' : '#fff', Math.sin(Math.PI * u / 1200)); } break;
    case 'confetti': { if (t > 2600) break; const colors = ['#FF7FA8', '#FFD45E', '#7FD3FF', '#B9F28A', '#fff']; for (let i = 0; i < 40; i++) { const vx = (r() - 0.5) * 0.5, vy = -0.55 - r() * 0.35, u = t; const x = FOOT.x + vx * u, y = FOOT.y - H * 0.6 + vy * u + 0.00045 * u * u; g.save(); g.globalAlpha = 1 - k01(u, 1800, 2600); g.fillStyle = colors[i % 5]; g.translate(x, y); g.rotate(u / 200 + i); g.fillRect(-4, -2, 8, 4); g.restore(); } break; }
    case 'beam': { if (t > 900) break; const bx = her.x + her.w * 0.45, by = her.y + her.h * 0.5; g.save(); g.globalAlpha = Math.sin(Math.PI * t / 900) * 0.85; const gr = g.createLinearGradient(bx, by, bx, by - 260); gr.addColorStop(0, glow(1)); gr.addColorStop(1, glow(0)); g.fillStyle = gr; g.beginPath(); g.moveTo(bx - 18, by); g.lineTo(bx - 70, by - 260); g.lineTo(bx + 70, by - 260); g.lineTo(bx + 18, by); g.fill(); g.restore(); for (let i = 0; i < 6; i++) star(bx + (r() - 0.5) * 100, by - 60 - r() * 160, 8, '#fff', Math.sin(Math.PI * t / 900)); break; }
    case 'puff': { if (t > 900) break; const bx = her.x + her.w * 0.45, by = her.y + her.h * 0.45; for (let i = 0; i < 5; i++) { g.save(); g.globalAlpha = 0.55 * (1 - t / 900); g.fillStyle = '#9A9AA6'; g.beginPath(); g.arc(bx + (i - 2) * 14, by - t * 0.06 - (i % 2) * 10, 10 + t * 0.02, 0, Math.PI * 2); g.fill(); g.restore(); } break; }
    case 'flash': { if (t > 450) break; const p = f[3] || { x: FOOT.x, y: FOOT.y - H * 0.5 }; g.save(); g.globalAlpha = (1 - t / 450) * 0.9; const rad = 60 + t * 0.5 * f[2]; const gr = g.createRadialGradient(p.x, p.y - 60, 0, p.x, p.y - 60, rad); gr.addColorStop(0, '#fff'); gr.addColorStop(1, 'rgba(255,255,255,0)'); g.fillStyle = gr; g.beginPath(); g.arc(p.x, p.y - 60, rad, 0, Math.PI * 2); g.fill(); g.restore(); break; }
    case 'trail': for (const q of trail) { const age = (performance.now() - q.rt) / 420; star(q.x + 18 * Math.sin(q.rt), q.y + 14 * Math.cos(q.rt * 1.3), 9 * (1 - age) + 3, ['#FFE27A', '#FFB3D1', '#fff'][Math.floor(q.rt) % 3], 1 - age); } break;
    case 'notes': for (let i = 0; i < 4; i++) { const u = (t + i * 500) % 2000; g.save(); g.globalAlpha = Math.sin(Math.PI * u / 2000); g.fillStyle = '#7C6CF0'; g.font = '700 22px sans-serif'; g.fillText(i % 2 ? '♪' : '♫', head.x + 50 + i * 8 + 10 * Math.sin(u / 300), head.y - u * 0.05); g.restore(); } break;

    // ---- manga symbols
    case 'bang': {
      const a = life(t, 140, 700, 200); if (!a) break;
      const s = t < 140 ? 0.4 + 0.75 * ease(t / 140) : 1 + 0.15 * Math.max(0, 1 - (t - 140) / 160);
      g.save(); g.globalAlpha = a; g.translate(headL.x - 14, headL.y - 4); g.rotate(-0.2); g.scale(s, s);
      g.font = '900 34px sans-serif'; g.textAlign = 'center'; g.lineWidth = 6; g.strokeStyle = '#fff'; g.strokeText('!', 0, 0); g.fillStyle = '#FF5A6E'; g.fillText('!', 0, 0);
      g.strokeStyle = '#FF5A6E'; g.lineWidth = 3;
      for (const k of [-1, 0, 1]) { const ang = -Math.PI / 2 + k * 0.6 - 0.2; g.beginPath(); g.moveTo(Math.cos(ang) * 26, Math.sin(ang) * 26 - 10); g.lineTo(Math.cos(ang) * 36, Math.sin(ang) * 36 - 10); g.stroke(); }
      g.restore(); break;
    }
    case 'sweat': {
      const per = f[2] || 1e9, u = t % per; if (u > 900) break;
      const p = u / 900, x = headL.x - 6, y = headL.y + 18 + 22 * ease(p);
      g.save(); g.globalAlpha = 1 - k01(p, 0.6, 1); g.fillStyle = '#9ADCFF'; g.strokeStyle = '#4A9BD1'; g.lineWidth = 1.5;
      g.beginPath(); g.moveTo(x, y - 12); g.quadraticCurveTo(x + 8, y, x, y + 6); g.quadraticCurveTo(x - 8, y, x, y - 12); g.fill(); g.stroke();
      g.fillStyle = '#fff'; g.beginPath(); g.arc(x - 2, y - 1, 1.8, 0, Math.PI * 2); g.fill(); g.restore(); break;
    }
    case 'anger': {
      const a = life(t, 120, f[2] || 1000, 200); if (!a) break;
      g.save(); g.globalAlpha = a; g.translate(headL.x, headL.y + 6); g.scale(1 + 0.15 * Math.sin(t / 70), 1 + 0.15 * Math.sin(t / 70));
      g.strokeStyle = '#E8454F'; g.lineWidth = 4; g.lineCap = 'round';
      for (let i = 0; i < 4; i++) { g.rotate(Math.PI / 2); g.beginPath(); g.arc(9, 9, 7, Math.PI, Math.PI * 1.5); g.stroke(); }
      g.restore(); break;
    }
    case 'gloom': {
      const a = 0.4 * life(t, 300, f[2] || 1e9, 300); if (!a) break;
      g.save(); g.globalAlpha = a; g.strokeStyle = '#4B4F80'; g.lineWidth = 3;
      for (let i = 0; i < 7; i++) { const x = her.x + her.w * (0.2 + i * 0.1), y0 = her.y - 6 + (i % 2) * 6; g.beginPath(); g.moveTo(x, y0); g.lineTo(x, y0 + her.h * (0.14 + 0.04 * Math.sin(i * 2.1 + now / 400))); g.stroke(); }
      g.restore(); break;
    }
    case 'dots': {
      const per = f[2] || 6000, u = t % per; if (u > 2600) break;
      g.save(); g.fillStyle = '#5C5F7A';
      for (let i = 0; i < 3; i++) { g.globalAlpha = life(u - i * 350, 120, 2000 - i * 350, 300); g.beginPath(); g.arc(headL.x - 26 + i * 12, headL.y - 4, 3.5, 0, Math.PI * 2); g.fill(); }
      g.restore(); break;
    }
    case 'sigh': {
      const per = f[2] || 7000, u = (t + 3000) % per; if (u > 1200) break;
      const p = u / 1200, x = her.x + her.w * 0.18 - 30 * p, y = her.y + her.h * 0.3 - 10 * p;
      g.save(); g.globalAlpha = 0.6 * (1 - p); g.fillStyle = '#fff'; g.strokeStyle = '#B8BCD0'; g.lineWidth = 1.5;
      for (const [dx, rr] of [[0, 7], [8, 5], [-8, 5]]) { g.beginPath(); g.arc(x + dx * (1 + p), y, rr * (1 + p * 0.8), 0, Math.PI * 2); g.fill(); g.stroke(); }
      g.restore(); break;
    }
    case 'steam': {
      const dur = f[2] || 1500; if (t > dur) break;
      g.save(); g.fillStyle = '#fff';
      for (let i = 0; i < 4; i++) {
        const u = (t + i * 250) % 1000 / 1000, side = i % 2 ? 1 : -1, x = head.x + side * (her.w * 0.32 + u * 10), y = her.y + 4 - u * 28;
        g.globalAlpha = 0.75 * (1 - u) * (1 - k01(t, dur - 300, dur)); g.beginPath(); g.arc(x, y, 5 + u * 6, 0, Math.PI * 2); g.fill();
      }
      g.restore(); break;
    }
    case 'dizzy': {
      const a = life(t, 200, f[2] || 1500, 300); if (!a) break;
      const c = f[3] ? at(...f[3]) : { x: head.x, y: her.y + her.h * 0.04 };
      for (let i = 0; i < 3; i++) { const ang = now / 260 + i * Math.PI * 2 / 3; star(c.x + Math.cos(ang) * 34, c.y + Math.sin(ang) * 8, 7, '#FFD45E', a * (0.6 + 0.4 * Math.sin(ang))); }
      break;
    }
    case 'tears': {
      const dur = f[2] || 1500; if (t > dur) break;
      g.save(); g.fillStyle = '#9ADCFF';
      for (let i = 0; i < 6; i++) {
        const u = (t + i * 160) % 480, side = i % 2 ? 1 : -1, x = head.x + side * (her.w * 0.18 + u * 0.12), y = head.y + 6 - u * 0.08 + 0.0006 * u * u;
        g.globalAlpha = 1 - u / 480; g.beginPath(); g.arc(x, y, 3.5, 0, Math.PI * 2); g.fill();
      }
      g.restore(); break;
    }
    case 'shock': {
      if (t > 360) break;
      g.save(); g.globalAlpha = 1 - t / 360; g.strokeStyle = '#5C5F7A'; g.lineWidth = 2.5; g.lineCap = 'round';
      for (let i = 0; i < 9; i++) {
        const ang = Math.PI * (1.05 + i * 0.11), r0 = her.w * 0.42 + t * 0.04;
        g.beginPath(); g.moveTo(head.x + Math.cos(ang) * r0, head.y + Math.sin(ang) * r0 * 0.9); g.lineTo(head.x + Math.cos(ang) * (r0 + 12), head.y + Math.sin(ang) * (r0 + 12) * 0.9); g.stroke();
      }
      g.restore(); break;
    }
    case 'brokenNote': {
      if (t > 900) break;
      const sep = 6 * k01(t, 200, 600), x = her.x + her.w * 0.82, y = her.y + her.h * 0.14 - 10 * ease(k01(t, 0, 200));
      g.save(); g.globalAlpha = 1 - k01(t, 600, 900); g.font = '700 30px sans-serif'; g.fillStyle = '#7C6CF0';
      for (const side of [-1, 1]) { g.save(); g.beginPath(); g.rect(side < 0 ? x - 20 : x, y - 40, 20, 50); g.clip(); g.fillText('♪', x - 9 + side * sep, y - side * sep * 0.5); g.restore(); }
      g.strokeStyle = '#E8454F'; g.lineWidth = 2.5; g.beginPath(); g.moveTo(x - 4, y - 28); g.lineTo(x + 3, y - 18); g.lineTo(x - 3, y - 10); g.lineTo(x + 4, y); g.stroke();
      g.restore(); break;
    }
    // ---- impacts and motion
    case 'impact': {
      if (t > 450) break;
      const p = f[2] ? at(...f[2]) : { x: st.x, y: st.y - 8 }, s = ease(k01(t, 0, 110)) * (1 - 0.3 * k01(t, 110, 450));
      g.save(); g.globalAlpha = 1 - k01(t, 250, 450); g.translate(p.x, p.y); g.scale(s, s); g.fillStyle = '#FFE27A'; g.strokeStyle = '#FF8A3D'; g.lineWidth = 3;
      g.beginPath(); for (let i = 0; i < 16; i++) { const ang = i * Math.PI / 8, rr = i % 2 ? 9 : 24 + (i % 4) * 3; g.lineTo(Math.cos(ang) * rr, Math.sin(ang) * rr); } g.closePath(); g.fill(); g.stroke();
      g.restore(); break;
    }
    case 'dust': {
      if (t > 600) break;
      const p = f[4] || (f[2] ? at(...f[2]) : { x: st.x, y: st.y }), n = f[3] || 4;
      g.save(); g.fillStyle = '#EDE3D2'; g.strokeStyle = '#CBBFA9'; g.lineWidth = 1.2;
      for (let i = 0; i < n; i++) { const side = i % 2 ? 1 : -1, d = (14 + i * 8) * ease(t / 600); g.globalAlpha = 0.85 * (1 - t / 600); g.beginPath(); g.arc(p.x + side * d, p.y - 4 - i * 2 - t * 0.01, 6 + i * 1.5 + t * 0.008, 0, Math.PI * 2); g.fill(); g.stroke(); }
      g.restore(); break;
    }
    case 'speed': {
      const a = life(t, 200, f[2] || 1e9, 200); if (!a) break;
      g.save(); g.strokeStyle = '#fff'; g.lineCap = 'round';
      for (let i = 0; i < 7; i++) { const y = her.y + her.h * (0.3 + i * 0.09), u = (now * 0.9 + i * 137) % 260, x = her.x + her.w * 0.7 + u; g.globalAlpha = a * 0.7 * (1 - u / 260); g.lineWidth = 2 + i % 2; g.beginPath(); g.moveTo(x, y); g.lineTo(x + 30 + (i % 3) * 14, y); g.stroke(); }
      g.restore(); break;
    }
    case 'shakeMarks': {
      if (t > (f[4] || 1200)) break;
      const p = at(f[2], f[3]);
      g.save(); g.strokeStyle = '#5C5F7A'; g.lineWidth = 2.5; g.lineCap = 'round'; g.globalAlpha = Math.floor(t / 90) % 2 ? 0.9 : 0.4;
      for (const side of [-1, 1]) for (let i = 0; i < 2; i++) { g.beginPath(); g.arc(p.x, p.y, 22 + i * 8, side < 0 ? Math.PI * 0.8 : -Math.PI * 0.2, side < 0 ? Math.PI * 1.2 : Math.PI * 0.2); g.stroke(); }
      g.restore(); break;
    }
    // ---- light
    case 'burst': {
      const a = 0.45 * life(t, 80, f[2] || 500, 300); if (!a) break;
      const c = { x: st.x, y: her.y + her.h * 0.4 };
      g.save(); g.globalAlpha = a; g.fillStyle = '#fff';
      for (let i = 0; i < 36; i++) {
        const ang = i / 36 * Math.PI * 2 + r() * 0.05, r0 = 150 + r() * 60, w = 0.012 + r() * 0.01;
        g.beginPath(); g.moveTo(c.x + Math.cos(ang - w) * 420, c.y + Math.sin(ang - w) * 420); g.lineTo(c.x + Math.cos(ang) * r0, c.y + Math.sin(ang) * r0); g.lineTo(c.x + Math.cos(ang + w) * 420, c.y + Math.sin(ang + w) * 420); g.fill();
      }
      g.restore(); break;
    }
    case 'rays': {
      const a = life(t, 300, f[2] || 1500, 400) * (f[3] || 0.45); if (!a) break;
      const c = f[4] || { x: st.x, y: her.y + her.h * 0.35 }; g.save(); g.globalAlpha = a; g.translate(c.x, c.y); g.rotate(now / 3000);
      const gr = g.createRadialGradient(0, 0, 20, 0, 0, 260); gr.addColorStop(0, glow(0.95)); gr.addColorStop(1, glow(0)); g.fillStyle = gr;
      for (let i = 0; i < 12; i++) { g.rotate(Math.PI / 6); g.beginPath(); g.moveTo(0, 0); g.lineTo(260, -26); g.lineTo(260, 26); g.closePath(); g.fill(); }
      g.restore(); break;
    }
    case 'halo': {
      const a = life(t, 300, f[2] || 1500, 500) * (f[3] || 0.5); if (!a) break;
      const c = { x: st.x, y: her.y + her.h * 0.45 }, rad = her.h * 0.75, gr = g.createRadialGradient(c.x, c.y, 0, c.x, c.y, rad);
      gr.addColorStop(0, glow(0.9)); gr.addColorStop(0.5, glow(0.35)); gr.addColorStop(1, glow(0));
      g.save(); g.globalAlpha = a; g.fillStyle = gr; g.beginPath(); g.arc(c.x, c.y, rad, 0, Math.PI * 2); g.fill(); g.restore(); break;
    }
    case 'shockwave': {
      if (t > 650) break;
      const p = ease(t / 650);
      const c = f[3] || { x: st.x, y: st.y - 2 };
      g.save(); g.globalAlpha = 0.9 * (1 - p); g.strokeStyle = f[2] || glow(1); g.lineWidth = 5 * (1 - p) + 1;
      g.beginPath(); g.ellipse(c.x, c.y, 30 + 150 * p, 8 + 34 * p, 0, 0, Math.PI * 2); g.stroke(); g.restore(); break;
    }
    case 'spark': {
      if (t > 700) break;
      const p = at(f[2], f[3]), n = f[4] || 8;
      for (let i = 0; i < n; i++) {
        const u = t - i * 30; if (u < 0) continue;
        const ang = i / n * Math.PI * 2 + 0.3, d = 8 + 46 * (1 - (1 - Math.min(1, u / 500)) ** 3);
        star(p.x + Math.cos(ang) * d, p.y + Math.sin(ang) * d, 7 * (1 - u / 700) + 2, i % 2 ? glow(1) : '#fff', 1 - k01(u, 300, 700));
      }
      break;
    }

    // ---- the tiers' own shows (README 3, 7.1)
    // a small gift box flies in from the edge of the page along an arc into her arms and turns into a ring of stars
    case 'giftArc': case 'giftStack': {
      const hands = at(0.5, 0.58), n = type === 'giftStack' ? 5 : 1;
      for (let i = 0; i < n; i++) {
        const side = i % 2 ? -1 : 1, u = t - i * 240; if (u < 0) continue;
        const from = { x: side > 0 ? PAGE.w + 20 : -20, y: her.y + 30 }, ctl = { x: side > 0 ? PAGE.w - 50 : 50, y: her.y - 120 }, p = Math.min(1, u / 600), q = 1 - p;
        if (u < 600) { const x = q * q * from.x + 2 * q * p * ctl.x + p * p * hands.x, y = q * q * from.y + 2 * q * p * ctl.y + p * p * (hands.y - i * 6); giftBox(x, y, 12, u / 200); }
        else if (u < 850) for (let j = 0; j < 8; j++) { const ang = j * Math.PI / 4, d = 10 + (u - 600) * 0.12; star(hands.x + Math.cos(ang) * d, hands.y - i * 6 + Math.sin(ang) * d, 6, j % 2 ? glow(1) : '#FFB3D1', 1 - (u - 600) / 250); }
      }
      break;
    }
    // the gift box drops from about one of her heights above her head to just over it, where her in-between frames
    // (giftIn) take it on into her hands
    case 'boxDrop': {
      if (t > 900) break;
      const top = at(0.5, -0.06), p = t / 900, y = top.y - H * 0.9 * (1 - p * p);
      g.save(); g.globalAlpha = 0.5; g.strokeStyle = '#E8454F'; g.lineWidth = 3; g.beginPath(); g.moveTo(top.x - 8, y - 30); g.quadraticCurveTo(top.x + 10 * Math.sin(t / 90), y - 70, top.x + 4, y - 110); g.stroke(); g.restore();
      giftBox(top.x, y, 20, Math.sin(t / 160) * 0.2); break;
    }
    // the blind box's seams leak the colour of what is inside before it opens
    case 'seamGlow': {
      const a = life(t, 200, (f[2] || 1200) - 400, 200); if (!a) break;
      const c = f[3] === 'shake' ? at(0.83, 0.44) : at(0.4, 0.5), rad = 34 + 6 * Math.sin(t / 70);
      g.save(); g.globalCompositeOperation = 'lighter'; g.globalAlpha = a; const gr = g.createRadialGradient(c.x, c.y, 0, c.x, c.y, rad); gr.addColorStop(0, glow(0.9)); gr.addColorStop(1, glow(0)); g.fillStyle = gr; g.beginPath(); g.arc(c.x, c.y, rad, 0, Math.PI * 2); g.fill();
      g.strokeStyle = glow(0.9); g.lineWidth = 2; for (let i = 0; i < 6; i++) { const ang = i * Math.PI / 3 + t / 300; g.beginPath(); g.moveTo(c.x + Math.cos(ang) * 14, c.y + Math.sin(ang) * 14); g.lineTo(c.x + Math.cos(ang) * (rad + 14), c.y + Math.sin(ang) * (rad + 14)); g.stroke(); }
      g.restore(); break;
    }
    // a beam of light from the top of the page down to a point (her, a gift on the ground, the box in her hands)
    case 'pillar': {
      const dur = f[2] || 1200, a = life(t, 150, dur - 450, 300); if (!a) break;
      const box = f[4] === 'box', x = f[3] ?? (box ? at(0.4, 0.5).x : st.x), y = box ? at(0.4, 0.5).y : f[4] ?? st.y, w = f[5] || 90;
      const gr = g.createLinearGradient(x - w / 2, 0, x + w / 2, 0); gr.addColorStop(0, glow(0)); gr.addColorStop(0.5, glow(0.75)); gr.addColorStop(1, glow(0));
      g.save(); g.globalCompositeOperation = 'lighter'; g.globalAlpha = a; g.fillStyle = gr; g.fillRect(x - w / 2, 0, w, y);
      g.fillStyle = 'rgba(255,255,255,0.55)'; g.fillRect(x - w * 0.08, 0, w * 0.16, y);
      g.beginPath(); g.ellipse(x, y, w * 0.7, w * 0.16, 0, 0, Math.PI * 2); g.fillStyle = glow(0.6); g.fill(); g.restore(); break;
    }
    // a big star rises out of the opened box to the upper page, where the fountain bursts from it
    case 'bigStar': {
      if (t > 1050) break;
      const from = at(0.4, 0.5), p = ease(Math.min(1, t / 1000)), x = from.x + (FOOT.x - from.x) * p, y = from.y + (200 - from.y) * p;
      g.save(); g.globalCompositeOperation = 'lighter'; const gr = g.createRadialGradient(x, y, 0, x, y, 60); gr.addColorStop(0, glow(0.9)); gr.addColorStop(1, glow(0)); g.fillStyle = gr; g.beginPath(); g.arc(x, y, 60, 0, Math.PI * 2); g.fill(); g.restore();
      star(x, y, 22 + 6 * Math.sin(t / 80), '#fff', 1); break;
    }
    // stars, hearts and little gifts thrown up to the top of the page, falling back down all over it
    case 'fountain': {
      const dur = f[2] || 2000; if (t > dur) break;
      const c = f[3];
      for (let i = 0; i < 26; i++) {
        const u = t - i * 18; if (u < 0) continue;
        const vx = (r() - 0.5) * 0.55, vy = -0.75 - r() * 0.45, kind = i % 3, x = c.x + vx * u, y = c.y + vy * u + 0.0007 * u * u, a = 1 - k01(t, dur - 500, dur);
        if (kind === 0) star(x, y, 9 + r() * 6, i % 2 ? glow(1) : '#fff', a);
        else if (kind === 1) heart(x, y, 14 + r() * 6, a);
        else { g.save(); g.globalAlpha = a; giftBox(x, y, 9, u / 200); g.restore(); }
      }
      break;
    }
    // ---- things
    case 'anchors': {
      if (t > (f[2] || 2400)) break;
      for (let i = 0; i < 5; i++) {
        const u = (t + i * 420) % 2100, x = st.x + (r() - 0.5) * 240, y = st.y - 30 - u * 0.15;
        if (f[3] === 'hearts') { heart(x, y, 16, 0.85 * Math.sin(Math.PI * u / 2100)); continue; }
        g.save(); g.globalAlpha = 0.85 * Math.sin(Math.PI * u / 2100); g.translate(x, y); g.rotate(0.3 * Math.sin(u / 300)); g.strokeStyle = '#F4C542'; g.lineWidth = 2.5; g.lineCap = 'round';
        g.beginPath(); g.moveTo(0, -9); g.lineTo(0, 8); g.moveTo(-5, -5); g.lineTo(5, -5); g.moveTo(-8, 3); g.quadraticCurveTo(0, 13, 8, 3); g.stroke(); g.beginPath(); g.arc(0, -11, 2.5, 0, Math.PI * 2); g.stroke(); g.restore();
      }
      break;
    }
    // ---- snow
    case 'snow': {
      const a = life(t, 500, (f[2] || 5000) - 1000, 500); if (!a) break;
      g.save(); g.fillStyle = '#fff';
      for (let i = 0; i < 40; i++) {
        const fall = 0.04 + r() * 0.05, x0 = r() * PAGE.w, y = (r() * PAGE.h + t * fall) % (PAGE.h + 20) - 10;
        g.globalAlpha = a * (0.5 + 0.5 * r()); g.beginPath(); g.arc(x0 + 12 * Math.sin(t / 700 + i), y, 1.5 + r() * 2.5, 0, Math.PI * 2); g.fill();
      }
      g.restore(); break;
    }
    case 'spray': {
      if (t > (f[2] || 3000)) break;
      g.save(); g.fillStyle = '#fff';
      for (let i = 0; i < 14; i++) {
        const u = (t + i * 70) % 600 / 600, x = her.x + her.w * 0.85 + u * 60 + (i % 4) * 4, y = st.y - 6 - 120 * u * (1 - u) * (0.6 + (i % 3) * 0.2);
        g.globalAlpha = 0.85 * (1 - u); g.beginPath(); g.arc(x, y, 2 + u * 4, 0, Math.PI * 2); g.fill();
      }
      g.restore(); break;
    }
    case 'snowBurst': {
      if (t > 800) break;
      const p = at(0.45, 0.8);
      g.save(); g.fillStyle = '#fff'; g.strokeStyle = '#D6E6F5'; g.lineWidth = 1.5;
      for (let i = 0; i < 12; i++) {
        const ang = Math.PI * (1.05 + i / 11 * 0.9), dd = 20 + 70 * ease(Math.min(1, t / 500));
        g.globalAlpha = 1 - k01(t, 400, 800); g.beginPath(); g.arc(p.x + Math.cos(ang) * dd, p.y + Math.sin(ang) * dd * 0.7, 8 + 6 * (i % 3), 0, Math.PI * 2); g.fill(); g.stroke();
      }
      g.restore(); break;
    }
    case 'snowShake': {
      if (t > (f[2] || 2000)) break;
      g.save(); g.fillStyle = '#fff';
      for (let i = 0; i < 10; i++) {
        const u = (t + i * 90) % 500, side = i % 2 ? 1 : -1, x = head.x + side * (her.w * 0.25 + u * 0.15), y = her.y + her.h * (0.15 + (i % 4) * 0.08) - u * 0.05 + 0.0005 * u * u;
        g.globalAlpha = 1 - u / 500; g.beginPath(); g.arc(x, y, 3 + i % 3, 0, Math.PI * 2); g.fill();
      }
      g.restore(); break;
    }
    case 'z': for (let i = 0; i < 3; i++) { const u = (now + i * 900) % 2700; g.save(); g.globalAlpha = Math.sin(Math.PI * u / 2700); g.fillStyle = '#C9D6FF'; g.font = `700 ${16 + i * 5}px sans-serif`; g.fillText('Z', her.x + her.w * 0.75 + u * 0.02, her.y + 10 - u * 0.035); g.restore(); } break;
  }
}
