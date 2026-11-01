// Geometry and drawing algorithms lifted from EV1–EV4 at 0464bd8.
export const canvasOf = (w, h, draw) => { const c = document.createElement('canvas'); c.width = w; c.height = h; draw(c.getContext('2d')); return c; };
export const inside = (poly, x, y) => { let c = false; for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) { const a = poly[i], b = poly[j]; if ((a[1] > y) !== (b[1] > y) && x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0]) c = !c; } return c; };
export const k = (a, b, x) => Math.min(1, Math.max(0, (x - a) / (b - a)));
export const on2 = x => Math.floor(x / 83) * 83;
export const rnd = (i, s) => { const v = Math.sin(i * 127.1 + s * 311.7) * 43758.5453; return v - Math.floor(v); };
export const trace = (c, pts) => { c.beginPath(); pts.forEach((p, i) => i ? c.lineTo(p[0], p[1]) : c.moveTo(p[0], p[1])); };
export const clipTo = (c, poly) => { trace(c, poly); c.closePath(); c.clip(); };
export const out = p => 1 - Math.pow(1 - p, 3);
export const inq = p => p * p;
export const quint = p => 1 - Math.pow(1 - p, 5);
export const smooth = p => p * p * (3 - 2 * p);
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
const C0 = [960,540];
export const reach = (p, q, radius = 5000) => { const d = [p[0] - q[0], p[1] - q[1]], m = Math.hypot(d[0], d[1]), u = [d[0] / m, d[1] / m], w = [p[0] - C0[0], p[1] - C0[1]], b = w[0] * u[0] + w[1] * u[1], t = -b + Math.sqrt(b * b - (w[0] * w[0] + w[1] * w[1] - radius * radius)); return [p[0] + u[0] * t, p[1] + u[1] * t]; };
export const onScreen = (p, view = [-60,-60,1980,1140]) => p[0]>view[0] && p[0]<view[2] && p[1]>view[1] && p[1]<view[3];
export const chordOf = (path,u0,radius=5000) => { const a=path.at(u0),b=path.at(1);return [reach(a,b,radius),reach(b,a,radius)]; };
export const scars = (paths,tails) => tails.map((u,i)=>chordOf(paths[i],u));
export const dense = ([a,b], count=1000) => Array.from({length:count+1},(_,i)=>[a[0]+(b[0]-a[0])*i/count,a[1]+(b[1]-a[1])*i/count]);
export const at = (place,key,points) => points.map(point=>place(key,point));
export function shakeAt(t,t0,amp,dur,dir) {
 const p=k(t0,t0+dur,t); if(p<=0||p>=1)return [0,0]; const a=amp*(1-p)*(1-p);
 if(!dir)return [Math.sin(t*.21)*a,Math.cos(t*.17)*a*.6];
 const s=Math.sin(t*.21)*a,c=Math.cos(t*.17)*a*.25;return [dir[0]*s-dir[1]*c,dir[1]*s+dir[0]*c];
}
export function textures(P) {
 const owned=[];
 const tex = img => { const texture=P.Texture.from(img);if(img instanceof HTMLCanvasElement)owned.push(texture);return texture; };
    const silhouette = (img, color) => tex(canvasOf(img.width, img.height, g => { g.drawImage(img, 0, 0); g.globalCompositeOperation = 'source-in'; g.fillStyle = color; g.fillRect(0, 0, img.width, img.height); }));
    const radial = (stops) => tex(canvasOf(512, 512, g => { const r = g.createRadialGradient(256, 256, 0, 256, 256, 256); for (const [o, c] of stops) r.addColorStop(o, c); g.fillStyle = r; g.fillRect(0, 0, 512, 512); }));
 return {tex,silhouette,radial,dispose:()=>{for(const texture of owned)texture.destroy(true);}};
}
export const stroke = (g,w,color) => { g.lineWidth=w;g.strokeStyle=color;g.stroke(); };
export function drawing(g, gb = g, segments = 90) {
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
    const glint = (x, y, p, len) => {
      if (p <= 0 || p >= 1) return;
      const a = Math.sin(p * Math.PI);
      g.strokeStyle = 'rgba(255,255,255,' + a + ')'; g.lineWidth = 2.5;
      g.beginPath(); g.moveTo(x - len * a, y); g.lineTo(x + len * a, y); g.moveTo(x, y - len * 0.6 * a); g.lineTo(x, y + len * 0.6 * a); g.stroke();
    };
return {band,arcLine,glint};
}

// sample supplies position, normal and seed so fixed clusters and random blade-tip trails share the same particles.
export function sparks(g, elapsed, count, power, seed, sample, color) {
  if(elapsed<=0||elapsed>=1000)return;
  for(let i=0;i<count;i++) {
    const life=420+rnd(i,seed)*560;if(elapsed>life)continue;
    const [x0,y0,nx,ny]=sample(i,seed),side=rnd(i,seed+2)<.5?-1:1;
    const v=(300+rnd(i,seed+3)*1300)*power,s=elapsed/1000,spread=(rnd(i,seed+4)-.5)*.9;
    const dx=nx*side+spread,dy=ny*side+spread*.3,px=x0+dx*v*s,py=y0+dy*v*s+500*s*s,q=1-elapsed/life;
    g.strokeStyle='rgba('+(i%4?color:'255,255,255')+','+q+')';g.lineWidth=1.5+rnd(i,seed+5)*2.5;
    g.beginPath();g.moveTo(px,py);g.lineTo(px-dx*v*.03,py-dy*v*.03);g.stroke();
  }
}

export const slash = ({band,arcLine},palette,P, head, tail, head2, w, fray, seed, alphaMul = 1, cv) => {
  const a = (1 - fray) * alphaMul; w *= 1 - 0.55 * fray;
  band(P, tail, head, w * 2.6, 0.25, palette.slashAura, 0.16 * a,cv);
  band(P, tail, head, w, 0.08, palette.slash, 0.8 * a,cv);
  band(P, tail, head, w * 0.34, 0.03, palette.slashCore, a,cv);
  for (let s = 0; s < 7; s++) {
    const off = 10 + s * w * 0.12, len = (0.55 + rnd(s, seed) * 0.4) * (head - tail);
    arcLine(P, Math.max(tail, head - len), head - rnd(s, seed + 1) * 0.08 * (head - tail), off, 1 + (s % 2), 'rgba('+palette.strand[0]+',' + (palette.strand[1] - s * 18) + ',' + (palette.strand[2] - s * 18) + ',' + (0.55 * a) + ')',cv);
  }
  arcLine(P, Math.max(tail, head2 - 0.7 * (head - tail)), head2, w * 1.35, 2, 'rgba(255,255,255,' + 0.8 * a + ')',cv);
  if (fray > 0) {
    for (let s = 0; s < 9; s++) {
      const off = (s / 8 - 0.3) * 90 + fray * 50 * (s - 3), v0 = 0.08 + fray * (0.2 + rnd(s, seed + 2) * 0.5), v1 = 1 - fray * (0.1 + rnd(s, seed + 3) * 0.6);
      if (v1 > v0) arcLine(P, tail + (head - tail) * v0, tail + (head - tail) * v1, off, 1 + rnd(s, seed + 4) * 1.6, 'rgba('+palette.fray[0]+',' + (palette.fray[1] + s * 12) + ','+palette.fray[2]+',' + (0.6 * (1 - fray)) + ')',cv);
    }
  }
};

export function cutCells(scars,C0,RR,VIEW) {
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
