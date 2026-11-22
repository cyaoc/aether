// B站 PK 对手情报卡 (README 15): what her sprite prints over her head when a 1-on-1 PK is matched, 10 s before it
// starts. Two sides, the sprite turns it over half way: 对手情报 (the other streamer: 段位 with its stars and 全站排名,
// 今日首胜, 关注, 大航海) and 对面在线榜前三 (each one's 本场消费, with their 荣耀等级, 大航海 and 灯牌 as tags; never their
// names: they are the other room's viewers). What B站 did not answer is left out, a side with nothing on it too.
// Page px (1080p stream, 1:1), Noto Sans SC 500/800, drawn as the 账单卡 is. The design package's lib/pk-intel.js, ported
// whole; its self-check is the page's tests' (DesktopPetEngine.test.mjs).
import { SANS } from '../gift-board/paint.mjs';

export const PK_INTEL = (() => {
  const W = 400, PAD = 20, IN = W - 2 * PAD, HEAD = 52;
  // the other side is blue, as on B站's own PK bar; her pink stays on the edge of the paper she prints
  const INK = '#4A3742', MUTED = '#8B6B7B', PAPER = '#FFFDF9', EDGE = '#A9C1F5', RULE = '#C9D7F7', BLUE = '#3D74E0';
  // 大航海 by B站's guard_level: each its own word and colour; gold only for 总督, the highest
  const GUARD = { 1: { word: '总督', fill: '#F2C14E', ink: '#5A3A00' }, 2: { word: '提督', fill: '#8456D8', ink: '#fff' }, 3: { word: '舰长', fill: BLUE, ink: '#fff' } };
  const F = (w, px) => `${w} ${px}px ${SANS}`;

  // how many: as written up to 9999, then in 万, one decimal under 10万 (1.2万, 3万), whole above (12万)
  const count = v => v < 10000 ? String(Math.round(v)) : v < 100000 ? `${Math.round(v / 1000) / 10}万` : `${Math.round(v / 10000)}万`;
  // as the 账单卡: a line never starts with closing punctuation
  const wrap = (g, text, maxW) => { const lines = []; let cur = ''; for (const ch of [...text]) { if (cur && g.measureText(cur + ch).width > maxW && !'！？。，、…～）」’”'.includes(ch)) { lines.push(cur); cur = ch; } else cur += ch; } lines.push(cur); return lines; };
  const star = (g, x, y, r, color, points = 5) => {
    g.save(); g.fillStyle = color; g.beginPath();
    for (let i = 0; i < points * 2; i++) { const a = i * Math.PI / points - Math.PI / 2, d = i % 2 ? r * (points === 5 ? 0.45 : 0.32) : r; g.lineTo(x + Math.cos(a) * d, y + Math.sin(a) * d); }
    g.fill(); g.restore();
  };
  const pill = (g, text, x, base, fill, ink, px = 24) => {
    g.save(); g.font = F(800, px); const w = g.measureText(text).width + 20;
    g.fillStyle = fill; g.beginPath(); g.roundRect(x, base - px - 6, w, px + 12, (px + 12) / 2); g.fill();
    g.fillStyle = ink; g.textAlign = 'left'; g.textBaseline = 'middle'; g.fillText(text, x + 10, base - px / 2); g.restore();
    return w;
  };
  // a viewer's tags, in order: 荣耀等级, 大航海, 灯牌 (only a 灯牌 of the other streamer's, B站 says whose it is)
  const tags = v => [v.wealth > 0 && ['荣耀' + v.wealth, '#EFE6F7', INK], GUARD[v.guard] && [GUARD[v.guard].word, GUARD[v.guard].fill, GUARD[v.guard].ink],
    v.medal > 0 && ['灯牌' + v.medal, '#E0558E', '#fff']].filter(Boolean);
  // the 关注/大航海/首胜 cells there are
  const cells = c => [c.firstWin != null && ['今日首胜', c.firstWin ? ['已拿到', '#8B6B7B'] : ['还没拿', '#CC6A1F']], c.fans != null && ['关注', count(c.fans)], c.guards != null && ['大航海', count(c.guards)]].filter(Boolean);

  // Where things go on each side, each side as tall as it needs (it changes height edge on, as it turns over: a taller
  // side for both left the front with an empty foot). A side is null when there is nothing for it.
  function layout(g, c) {
    g.save(); g.font = F(500, 28); const name = wrap(g, c.name || '', IN - 60); g.restore();
    let y = HEAD + 14; const nameY = y; y += name.length * 38 + 8;
    const rule1 = y; y += 12;
    const rankY = c.rank ? y : null; if (c.rank) y += c.rank.place ? 76 : 48;
    const rule2 = c.rank && cells(c).length ? y : null; if (rule2 != null) y += 12;
    const cellsY = cells(c).length ? y : null; if (cellsY != null) y += 76;
    const front = c.rank || cells(c).length ? { name, nameY, rule1, rankY, rule2, cellsY, h: y + 12 } : null;
    const rows = (c.top3 || []).slice(0, 3).map(v => ({ v, tags: tags(v) }));
    let b = HEAD + 14; for (const r of rows) { r.y = b; b += 44 + (r.tags.length ? 40 : 0) + 8; }
    const back = rows.length ? { rows, h: b + 6 } : null;
    return { front, back };
  }

  function paper(g, h, title) {
    const p = new Path2D(); p.roundRect(0, 0, W, h, 18);
    g.save(); g.shadowColor = 'rgba(40,40,80,0.32)'; g.shadowBlur = 16; g.shadowOffsetY = 6; g.fillStyle = PAPER; g.fill(p); g.restore();
    g.save(); g.clip(p);
    const gr = g.createLinearGradient(0, 0, W, 0); gr.addColorStop(0, '#4F7BE8'); gr.addColorStop(1, '#8E6AE6');
    g.fillStyle = gr; g.fillRect(0, 0, W, HEAD); g.globalAlpha = 0.16; g.fillStyle = '#fff'; g.fillRect(0, 0, W, HEAD * 0.45);
    g.restore();
    g.lineWidth = 3; g.strokeStyle = EDGE; g.stroke(p);
    g.save(); g.font = F(800, 26); g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillStyle = '#fff';
    g.shadowColor = 'rgba(30,30,90,0.4)'; g.shadowBlur = 4; g.fillText(title, W / 2, HEAD / 2 + 1); g.restore();
    g.save(); g.font = F(800, 26); const tw = g.measureText(title).width; g.restore();
    star(g, W / 2 - tw / 2 - 18, HEAD / 2, 8, '#fff', 4); star(g, W / 2 + tw / 2 + 18, HEAD / 2, 8, '#fff', 4);
  }
  const rule = (g, y0) => { g.save(); g.strokeStyle = RULE; g.lineWidth = 2; g.setLineDash([7, 6]); g.beginPath(); g.moveTo(PAD, y0); g.lineTo(W - PAD, y0); g.stroke(); g.restore(); };

  function front(g, c, L) {
    const f = L.front;
    paper(g, f.h, '对手情报');
    // VS and the other streamer's name, in full
    pill(g, 'VS', PAD, f.nameY + 32, BLUE, '#fff');
    g.save(); g.font = F(500, 28); g.fillStyle = INK; g.textBaseline = 'top';
    f.name.forEach((l, i) => g.fillText(l, PAD + 60, f.nameY + i * 38)); g.restore();
    rule(g, f.rule1);
    if (f.rankY != null) {   // 段位 and its stars (苍穹主宰 has none), 全站排名 under it
      const r = c.rank, stars = r.top ? 0 : r.stars || 0;
      g.save(); g.font = F(800, 32); const nw = g.measureText(r.name).width, sw = stars ? stars * 26 + 8 : 0, x0 = W / 2 - (nw + sw) / 2;
      g.fillStyle = INK; g.textBaseline = 'alphabetic'; g.fillText(r.name, x0, f.rankY + 38);
      for (let i = 0; i < stars; i++) star(g, x0 + nw + 8 + 13 + i * 26, f.rankY + 26, 11, '#8E6AE6');
      if (r.place) { g.font = F(500, 24); g.fillStyle = MUTED; g.textAlign = 'center'; g.fillText(`全站排名 ${r.place}`, W / 2, f.rankY + 70); }
      g.restore();
    }
    if (f.rule2 != null) rule(g, f.rule2);
    if (f.cellsY != null) {   // the cells side by side: the label over its value
      const cs = cells(c), cw = IN / cs.length;
      cs.forEach(([label, value], i) => {
        const cx = PAD + cw * (i + 0.5);
        g.save(); g.textAlign = 'center'; g.textBaseline = 'alphabetic'; g.font = F(500, 24); g.fillStyle = MUTED; g.fillText(label, cx, f.cellsY + 26);
        if (Array.isArray(value)) { g.font = F(800, 24); const w = g.measureText(value[0]).width + 20; pill(g, value[0], cx - w / 2, f.cellsY + 66, value[1], '#fff'); }
        else { g.font = F(800, 30); g.fillStyle = INK; g.fillText(value, cx, f.cellsY + 64); }
        g.restore();
      });
    }
  }

  function back(g, c, L) {
    paper(g, L.back.h, '对面在线榜前三');
    for (const [i, { v, tags: ts, y }] of L.back.rows.entries()) {
      // the place in a blue ring, the 本场消费 large, its tags under it
      g.save(); g.fillStyle = BLUE; g.beginPath(); g.arc(PAD + 17, y + 22, 17, 0, Math.PI * 2); g.fill();
      g.fillStyle = '#fff'; g.font = F(800, 24); g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText(String(i + 1), PAD + 17, y + 23);
      g.textAlign = 'left'; g.textBaseline = 'alphabetic'; g.font = F(500, 24); g.fillStyle = MUTED; g.fillText('本场', PAD + 46, y + 32);
      const lw = g.measureText('本场 ').width; g.font = F(800, 32); g.fillStyle = INK; const num = count(v.score); g.fillText(num, PAD + 46 + lw, y + 34);
      const nw = g.measureText(num).width; g.font = F(500, 24); g.fillStyle = MUTED; g.fillText(' 电池', PAD + 46 + lw + nw, y + 34);
      let x = PAD + 46; for (const [text, fill, ink] of ts) x += pill(g, text, x, y + 78, fill, ink) + 8;
      g.restore();
    }
  }

  // The card at x, y on its side (turn: 0 its front, 1 its back; between, it is turning over, edge on at 0.5); its height.
  function draw(g, c, x, y, turn = 0) {
    const L = layout(g, c), side = turn < 0.5 ? 'front' : 'back';
    if (!L[side]) return 0;
    const k = Math.abs(Math.cos(Math.PI * turn));
    g.save(); g.translate(x + W / 2, y); g.scale(Math.max(0.02, k), 1); g.translate(-W / 2, 0);
    (side === 'front' ? front : back)(g, c, L);
    g.restore();
    return L[side].h;
  }

  // The boards' cases (made-up streamers and numbers): a usual match, the top 段位, B站 answering only part, a long name
  // with nobody on the other room's 在线榜, a newcomer.
  const SAMPLES = {
    普通: { name: '星星点灯', rank: { name: '钻石传说', stars: 3, place: 458 }, firstWin: false, fans: 34613, guards: 123,
      top3: [{ score: 12345, wealth: 31, guard: 3, medal: 25 }, { score: 5200, wealth: 22, medal: 21 }, { score: 880, wealth: 12 }] },
    最高段位: { name: '月下Lily', rank: { name: '苍穹主宰', top: true, place: 40 }, firstWin: true, fans: 314100, guards: 2650,
      top3: [{ score: 131452, wealth: 48, guard: 1, medal: 32 }, { score: 52000, wealth: 40, guard: 2, medal: 30 }, { score: 20000, wealth: 35, guard: 3, medal: 28 }] },
    只查到一部分: { name: '星星点灯', fans: 34613, top3: [{ score: 3000 }, { score: 1200, wealth: 8 }] },
    长名字没人上榜: { name: '一只名字特别特别长的主播啊啊啊长长长长', rank: { name: '黄金战神', stars: 2, place: 0 }, firstWin: false, fans: 8800, guards: 12, top3: [] },
    新人: { name: '小新', rank: { name: '青铜萌新', stars: 1 }, firstWin: false, fans: 120, guards: 0, top3: [{ score: 52, wealth: 3 }] }
  };
  return { W, count, layout, draw, SAMPLES };
})();
