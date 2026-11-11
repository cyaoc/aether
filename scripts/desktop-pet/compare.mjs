// node scripts/desktop-pet/compare.mjs [--out <dir>] [--port 8820] [case ...]: plays every show of the 桌宠页 (each blind box
// ending, each lottery step, each 日常小动作 with and without its mishap), each 心情, waking up before a show, a 一轮送礼 and a
// big gift crowned as the new first on the 桌宠页 and on the design package's board, at the same moments from the same random
// seed, and compares the page's pixels with the board's. docs/design/desktop-pet/tools/server.mjs must be serving both on the
// port. Both are driven frame by frame on one made-up clock (their own clock, performance.now and the frame loop stopped
// before they start), so what moves on real time (in-between frames, the sprite's flight, cross-fades) moves alike. The page
// is drawn where the board draws it, at (1400, 420) on a 1920 × 1080 canvas over the board's 透明 room and the dashed outline
// of the page, so that both send the canvas the same drawing: Chrome's GPU canvas keeps what it drew before for later
// drawing, and pages that drew other frames first came out a level or two apart at some edges. Mismatched moments are saved
// as <case>-<ms>-board.png and -page.png in --out (default /tmp/desktop-pet-compare).
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const here = new URL('.', import.meta.url).pathname, eval_ = here + '../../docs/design/desktop-pet/tools/cdp-eval.mjs';
const args = process.argv.slice(2), opt = (k, d) => { const i = args.indexOf(k); return i < 0 ? d : args.splice(i, 2)[1]; };
const out = opt('--out', '/tmp/desktop-pet-compare'), port = opt('--port', 8820);
const NAME = { entry: '进场', gift: '普通礼物', giftStreak: '连着送', midGift: '中礼物', bigGift: '大礼物', blindBoxSmall: '盲盒·小', blindBoxMedium: '盲盒·中', blindBoxLarge: '盲盒·大',
  captain: '舰长', admiral: '提督', governor: '总督', castle: '城堡', newFirst: '新的第一名' };
// The board's show for each step of the lottery, the 试演 variants of its one row.
const STEP = { EntryOpen: '报名开场', EntryClosed: '报名截止', PoolOpen: '奖池开场', PoolCall: '奖池点名', PoolSpin: '奖池转动',
  RevealGrand: '揭晓·大奖', RevealSecond: '揭晓·二奖', RevealThird: '揭晓·三奖', NoEntries: '没人报名', End: '抽奖结束' };
const RESULT = { Gain: '赚', Even: '平', Loss: '亏', Jackpot: '大奖' };
const KIND = { hum: '哼歌', gym: '举哑铃', hike: '爬山', bike: '骑车', ski: '滑雪' };
const MOOD = { calm: 0, happy: 0, bored: 15, gloomy: 40, sleepy: 60 };
const { shows } = JSON.parse(fs.readFileSync(here + '../../src/Aether.Core/Pages/desktop-pet/shows.json', 'utf8'));
const at = (dur, extra = []) => [...new Set([200, 600, Math.round(dur * 0.25), Math.round(dur * 0.5), Math.round(dur * 0.75), dur - 150, ...extra])].sort((a, b) => a - b);

// Each case: what to do on the board and on the page at its start, and the moments to compare, in ms from its start.
const cases = [];
for (const row of shows) {
  for (const variant of row.tests) if (NAME[row.id] ?? STEP[variant]) cases.push({ id: row.id + (variant ? '-' + variant : ''),
    board: `dc.start(${JSON.stringify(NAME[row.id] ?? STEP[variant])}, ${JSON.stringify(variant ? { result: RESULT[variant] } : {})})`,
    page: `pet.start(${JSON.stringify(row.id)}, { nickname: '星河', result: ${JSON.stringify(variant)} })` });
  if (KIND[row.id]) for (const mishap of [false, true]) cases.push({ id: row.id + (mishap ? '-Mishap' : ''),
    board: `dc.start('日常', { kind: ${JSON.stringify(KIND[row.id])}, mishap: ${mishap} })`, page: `pet.start(${JSON.stringify(row.id)}, { mishap: ${mishap} })` });
}
for (const [mood, minutes] of Object.entries(MOOD)) cases.push({ id: mood, ms: at(30000, [23100, 23600, 24300]),
  set: `x.lastGift = x.lastAny = x.now() - ${minutes} * 60000 - 1; ${mood === 'happy' ? 'x.recent = [x.now(), x.now(), x.now()];' : ''}` });
cases.push({ id: 'wake-entry', set: 'x.lastGift = x.lastAny = x.now() - 41 * 60000;', ms: at(2600),
  board: `dc.fire('进场')`, page: `pet.entry({ uid: 1, nickname: '星河' })` });
// A 一轮 of five gifts (the fifth takes over with 连着送) and one of blind boxes shaken until its result opens them.
const flower = { uid: 1, nickname: '星河', topGift: '小花花', boxSpend: 0, boxes: 0, castle: false, ended: false };
cases.push({ id: 'round-gifts', ms: at(5000, [380, 1450, 1500]),
  board: `dc.give(Array.from({ length: 5 }, () => ({ gift: '小花花', price: 10, n: 1 })))`,
  page: `for (let n = 1; n <= 5; n++) later(n * 350 - 350, () => pet.gift({ ...${JSON.stringify(flower)}, spend: n * 1000, count: n, topCount: n }))` });
cases.push({ id: 'round-boxes', ms: at(7000, [3000, 3800, 3900, 4800]),
  board: `dc.give([{ gift: '爱心抱枕', price: 160, n: 1, box: '心动盲盒', boxPrice: 150 }])`,
  page: `pet.gift({ ...${JSON.stringify(flower)}, boxSpend: 15000, boxes: 1 }); later(3000, () => pet.gift({ ...${JSON.stringify(flower)}, boxSpend: 15000, boxes: 1, ended: true }));
    later(3800, () => pet.blindBox({ uid: 1, nickname: '星河', tally: { spend: 15000, openedValue: 16000, outcome: 'Gain' }, boxes: 1, bestGift: '爱心抱枕', bestPrice: 16000 }))` });
// A big gift that takes the 礼物榜's first place, crowned after its show.
const ranks = rows => JSON.stringify({ rows: rows.map(([uid, nickname, num]) => ({ id: `${uid}/gift:1`, uid, nickname, num })), options: {}, running: true, fromGift: true });
cases.push({ id: 'bigGift-newFirst', ms: at(8600, [6200, 7000, 7700]),
  board: `dc.give([{ gift: '小电视飞船', price: 12450, n: 1, top: true }])`,
  page: `pet.ranked(${ranks([[2, '甲', 5]])}); pet.gift({ ...${JSON.stringify(flower)}, topGift: '小电视飞船', spend: 1245000, count: 1, topCount: 1 });
    pet.ranked(${ranks([[1, '星河', 1], [2, '甲', 5]])})` });
const chosen = args.length ? cases.filter(c => args.includes(c.id)) : cases;

// The board ready to play as x, or the page as x, drawn over the board's room where the board draws it, with every sheet
// loaded; then the canvas both are drawn on.
const SIDES = {
  board: {
    url: `http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html&props=` + encodeURIComponent(JSON.stringify({ scene: '手动', room: '透明', caption: '隐藏', controls: '隐藏' })),
    setup: `const x = dc; await new Promise(r => { const t = setInterval(() => { x.frame(); if (x.ready && x.master) { clearInterval(t); r(); } }, 50); });
      const canvas = document.getElementById('dp-cv'), reset = () => x.reset();`
  },
  page: {
    url: `http://localhost:${port}/desktop-pet`,
    setup: `const { pet } = await import('/desktop-pet/page.mjs'), { seeded } = await import('/desktop-pet/engine.mjs'), x = pet;
      const g = pet.canvas.getContext('2d'), clear = g.clearRect.bind(g);
      pet.resize(1920, 1080); pet.view = { k: 1, x: 1400, y: 420 };
      g.clearRect = (...a) => {
        clear(...a); for (let y = 0; y < 1080; y += 40) for (let x = 0; x < 1920; x += 40) { g.fillStyle = (x + y) / 40 % 2 ? '#9a9a9a' : '#b4b4b4'; g.fillRect(x, y, 40, 40); }
        g.save(); g.setLineDash([10, 8]); g.strokeStyle = 'rgba(255,255,255,0.7)'; g.lineWidth = 2; g.strokeRect(1400, 420, 480, 640); g.restore();
      };
      await Promise.all(Object.keys(pet.art.sheets).map(k => pet.loadSheet(k)));
      const canvas = pet.canvas, reset = () => x.reset(seeded(7));`
  }
};

// Plays one case from T0 on a grid of 60 frames a second, a gift or a result in the frame at or after its time, and keeps
// each moment's hash (and picture, for the moments of keep) in the first frame at or after it.
const PLAY = `async (c, keep, res) => {
  const T0 = 100000; x.master.currentTime = fake = T0;
  reset(); x.sv = x.hv = x.sc = x.orb = x.bubSide = null; x.rt = undefined; x.trail = []; x.nextDaily = Infinity;
  const timers = [], later = (ms, fn) => timers.push([T0 + ms, fn]);
  c.run(x, x, x, later);
  const dur = c.ms ? null : x.cur.pre + x.cur.dur, ms = c.ms || [200, 600, Math.round(dur * 0.25), Math.round(dur * 0.5), Math.round(dur * 0.75), dur - 150];
  res[c.id] = {};
  for (let i = 0, left = [...ms]; left.length; i++) {
    const t = T0 + i * 1000 / 60; x.master.currentTime = fake = t;
    for (const [when, fn] of timers.splice(0)) if (when <= t) fn(); else timers.push([when, fn]);
    x.frame();
    while (left.length && t >= T0 + left[0]) {
      const m = left.shift(), cg = crop.getContext('2d');
      cg.drawImage(canvas, 1400, 420, 480, 640, 0, 0, 480, 640);
      const data = cg.getImageData(0, 0, 480, 640).data, hash = [...new Uint8Array(await crypto.subtle.digest('SHA-256', data))].map(b => b.toString(16).padStart(2, '0')).join('');
      res[c.id][m] = keep.includes(c.id + ':' + m) ? { hash, png: crop.toDataURL('image/png') } : { hash };
    }
  }
}`;

// Each case's start on one side, as a function of the board or page (x, pet, dc) and later.
const starts = side => `[${chosen.map(c => `(x, pet, dc, later) => { ${c.set ?? ''} ${c[side] ?? ''} }`).join(',')}]`;

// Runs every case on one side and returns, for each, the moments' hashes (and the pictures of `keep`, case:ms).
function run(side, keep = []) {
  const js = `(async () => {
    let fake = 0; performance.now = () => fake;
    ${SIDES[side].setup}
    x.master.pause();
    const crop = document.createElement('canvas'); crop.width = 480; crop.height = 640;
    const play = ${PLAY}, runs = ${starts(side)}, res = {};
    for (const [i, c] of ${JSON.stringify(chosen.map(c => ({ id: c.id, ms: c.ms })))}.entries()) await play({ ...c, run: runs[i] }, ${JSON.stringify(keep)}, res);
    return res;
  })()`;
  return JSON.parse(execFileSync('node', [eval_, SIDES[side].url, '8000', js, 'window.requestAnimationFrame = () => 0;'], { encoding: 'utf8', maxBuffer: 1 << 30, timeout: 60 * 60000 }));
}

const [boardRes, pageRes] = [run('board'), run('page')];
const mismatched = [];
for (const c of chosen) for (const m of Object.keys(boardRes[c.id])) if (boardRes[c.id][m].hash !== pageRes[c.id]?.[m]?.hash) mismatched.push(c.id + ':' + m);
const moments = chosen.reduce((n, c) => n + Object.keys(boardRes[c.id]).length, 0);
console.log(`${chosen.length} cases, ${moments} moments: ${moments - mismatched.length} identical, ${mismatched.length} different`);
if (mismatched.length) {
  fs.mkdirSync(out, { recursive: true });
  const [b, p] = [run('board', mismatched), run('page', mismatched)];
  for (const key of mismatched) {
    const [id, m] = key.split(':');
    for (const [side, res] of [['board', b], ['page', p]]) fs.writeFileSync(`${out}/${id}-${m}-${side}.png`, Buffer.from(res[id][m].png.split(',')[1], 'base64'));
    console.log('  different: ' + key);
  }
  process.exitCode = 1;
}
