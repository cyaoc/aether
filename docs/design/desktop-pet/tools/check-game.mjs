// node tools/check-game.mjs <port> [route ...]: plays the chest game's routes on the board (server.mjs on <port>, game
// 开) in headless Chrome, all at once, and lists every change of her pose they make, with how the board takes it: its
// own in-betweens or a road of them (tween), written as a plain switch (cut), or no way at all (none). check-board.mjs
// walks her own shows from her standing pose; the game's changes (crying on her knees, pulling, lying dragged) are
// only met on its routes. The routes jump the board's clock where nothing changes, and wait in real time where poses do.
import { execFileSync, spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const tools = path.dirname(fileURLToPath(import.meta.url));
const [port, ...only] = process.argv.slice(2);
const LOOT = [['do', 'x.gm.chest = 500; x.gm.pile = 6; x.visit()'], ['wait', 300], ['jump', 2500], ['wait', 1500],
  ['do', 'x.heard()'], ['wait', 2500], ['jump', 9100], ['until', 'x.gm.visit?.phase === "tug"'], ['wait', 2600]];
const EMPTY = [['do', 'x.visit()'], ['wait', 300], ['jump', 2500], ['jump', 9100], ['until', 'x.gm.visit?.phase === "peer"'], ['wait', 3000]];
const gift = (price, helper = false) => ['do', `x.give([{ gift: '小花花', price: ${price}, n: 1${helper ? ', helper: true' : ''} }])`];
const ZAP = [gift(60), ['until', 'x.gm.visit.cut == null && x.gm.visit.zapped != null'], ['wait', 600]];
const HITS = [gift(10), ['wait', 900], ['do', 'x.join()'], gift(10, true), ['wait', 1500], gift(10, true), gift(10, true), gift(10, true), gift(10, true), ['wait', 3000]];
const LOSE = [['jump', 49000], ['until', 'x.gm.visit.sprint != null'], ['wait', 1500], gift(10, true), ['wait', 800], ['jump', 9000],
  ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 1500], gift(10), ['until', 'x.cur'], ['until', '!x.cur'], ['wait', 800]];
const WIN = [gift(100), ['until', 'x.cur?.name === "守住了"'], ['until', '!x.cur'], ['wait', 1500]];
const ROUTES = {
  'hits': [...LOOT, ...HITS],
  'zap-hits': [...LOOT, ...ZAP, ...HITS],
  'lose': [...LOOT, ...LOSE],
  'zap-lose': [...LOOT, ...ZAP, ...LOSE],
  'win': [...LOOT, ...WIN],
  'zap-win': [...LOOT, ...ZAP, ...WIN],
  'empty-knocked': [...EMPTY, ['until', 'x.gm.visit?.phase === "sit"'], ['wait', 500], gift(100), ['until', 'x.cur?.name === "打跑了"'], ['until', '!x.cur'], ['wait', 800]],
  'empty-toss': [...EMPTY, ['until', 'x.gm.visit?.phase === "sit"'], ['jump', 45100], ['until', '!x.gm.visit'], ['wait', 1200]],
  'scare-tug': [...LOOT, ...ZAP, ['do', 'x.fire("舰长", { price: 1980 })'], ['until', 'x.cur?.name === "舰长"'], ['until', '!x.cur'], ['wait', 800]],
  'scare-air': [['do', 'x.gm.chest = 500; x.gm.pile = 6; x.visit()'], ['wait', 300], ['jump', 2500], ['wait', 3000], ['do', 'x.fire("舰长", { price: 1980 })'],
    ['until', 'x.cur?.name === "舰长"'], ['until', '!x.cur'], ['wait', 800]],
};
const props = { scene: '手动', controls: '隐藏', caption: '隐藏', room: '深色房间', game: '开' };
const url = `http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html&props=${encodeURIComponent(JSON.stringify(props))}`;
const play = steps => `(async () => {
  const x = dc, sleep = ms => new Promise(r => setTimeout(r, ms)), seen = {}, errors = [];
  await sleep(600);
  const dh = x.drawHer.bind(x);
  x.drawHer = (g, st, now) => {
    const a = x.hv?.pose, r = dh(g, st, now), b = x.hv?.pose;
    if (a && b && a !== b && !((a + '>' + b) in seen)) { const w = x.route(a, b); seen[a + '>' + b] = w == null ? 'none' : w.length ? 'tween' : 'cut'; }
    return r;
  };
  for (const [kind, arg] of ${JSON.stringify(steps)}) {
    try {
      if (kind === 'wait') await sleep(arg);
      if (kind === 'jump') { x.master.currentTime += arg / x.cfg.speed; await sleep(300); }
      if (kind === 'do') new Function('x', arg)(x);
      if (kind === 'until') { const ok = new Function('x', 'return ' + arg), t0 = performance.now(); while (!ok(x)) { if (performance.now() - t0 > 30000) throw new Error('never'); await sleep(16); } }
    } catch (e) { errors.push(kind + ' ' + arg + ': ' + e.message); }
  }
  return { seen, errors };
})()`;
const names = only.length ? only : Object.keys(ROUTES);
const runs = await Promise.all(names.map(name => new Promise(done => {
  const p = spawn('node', [path.join(tools, 'cdp-eval.mjs'), url, '5000', play(ROUTES[name])], { stdio: ['ignore', 'pipe', 'ignore'] });
  let out = ''; p.stdout.on('data', d => { out += d; }); p.on('close', () => { try { done([name, JSON.parse(out)]); } catch { done([name, { seen: {}, errors: ['no result'] }]); } });
})));
const all = {};
for (const [name, r] of runs) {
  if (r.errors?.length) console.log(`${name}: ERRORS ${r.errors.join(' | ')}`);
  for (const [pair, how] of Object.entries(r.seen || {})) (all[pair] ??= { how, routes: [] }).routes.push(name);
}
const pairs = Object.entries(all).sort((a, b) => a[1].how.localeCompare(b[1].how) || a[0].localeCompare(b[0]));
const count = h => pairs.filter(([, v]) => v.how === h).length;
console.log(`${names.length} routes: ${pairs.length} changes of her pose, ${count('tween')} with in-betweens, ${count('cut')} cut as written, ${count('none')} with no way across`);
for (const [pair, v] of pairs) if (v.how !== 'tween') console.log(`  ${v.how === 'none' ? 'no way' : 'cut'}: ${pair}（${v.routes.join('、')}）`);
