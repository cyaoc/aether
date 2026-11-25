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
// n 人气票 (her side) or 小花花 (its side) from one of the board's viewers; of(k) is k of the way from the middle to an end
// (span 票: 300 for the chest of 500 电池, 100 before 2026-11-24), so of(0.55) of hers set off the lightning and of(1) of
// either side's end the fight there and then (秒杀), whatever the span
const tok = (n, side = 1) => ['do', `x.give([{ gift: '${side > 0 ? '人气票' : '小花花'}', price: 1, n: ${n}, viewer: x.someone() }])`];
const of = k => `Math.round(x.gm.visit.span * ${k})`;
const ZAP = [tok(of(0.55)), ['until', 'x.gm.visit.cut == null && x.gm.visit.zapped != null'], ['wait', 600]];
const SPRINT = [['jump', 49000], ['until', 'x.gm.visit.sprint != null'], ['wait', 1500]];
const AIR = [['do', 'x.gm.chest = 500; x.gm.pile = 6; x.visit()'], ['wait', 300], ['jump', 2500], ['wait', 1500], ['until', 'x.gm.visit?.phase === "carry"']];
const HITS = [tok(1), ['wait', 900], tok(1, -1), ['wait', 1500], tok(1, -1), tok(1, -1), tok(1, -1), tok(1, -1), ['wait', 3000]];
// it gets ahead in the last stretch (of(0.6) of its: also past her of(0.55) after the lightning) and has it at 0:00
const LOSE = [['jump', 49000], ['until', 'x.gm.visit.sprint != null'], ['wait', 1500], tok(of(0.6), -1), ['wait', 800], ['jump', 9000],
  ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 1500], ['do', "x.give([{ gift: '爱心抱枕', price: 10, n: 1 }])"], ['until', 'x.cur'], ['until', '!x.cur'], ['wait', 800]];
const WIN = [tok(of(1)), ['until', 'x.cur?.name === "守住了"'], ['until', '!x.cur'], ['wait', 1500]];
// the last stretch: ahead she holds, behind or level she is dragged; at 0:00 and the grace the side the line is on wins
const BUZZER = [...SPRINT, tok(5), ['wait', 2500], ['jump', 7000], ['until', 'x.cur?.name === "守住了"'], ['until', '!x.cur'], ['wait', 1500]];
const FLIP = [...SPRINT, tok(5), ['wait', 2500], tok(10, -1), ['wait', 2500], ['jump', 6000], ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 800]];
export const ROUTES = {
  'hits': [...LOOT, ...HITS],
  'zap-hits': [...LOOT, ...ZAP, ...HITS],
  'lose': [...LOOT, ...LOSE],
  'zap-lose': [...LOOT, ...ZAP, ...LOSE],
  'win': [...LOOT, ...WIN],
  'buzzer-win': [...LOOT, ...BUZZER],
  'sprint-flip': [...LOOT, ...FLIP],
  'steal-kill': [...LOOT, tok(of(1), -1), ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 800]],
  // (her of(0.45) more before its bolt strikes, 900 ms into the lightning: struck, it has a quarter more, and they are no 秒杀)
  'zap-kill': [...LOOT, tok(of(0.55)), ['until', 'x.gm.visit.cut != null'], ['wait', 600], tok(of(0.45)), ['until', 'x.cur?.name === "守住了"'], ['until', '!x.cur'], ['wait', 1500]],
  'air-kill': [...AIR, tok(of(1)), ['until', 'x.cur?.name === "守住了"'], ['until', '!x.cur'], ['wait', 1500]],
  'air-steal': [...AIR, tok(of(1), -1), ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 800]],
  // the end coming while it hauls the chest off, while it is still getting up, while it casts its lightning
  'sprint-kill': [...LOOT, ...SPRINT, tok(of(1)), ['until', 'x.cur?.name === "守住了"'], ['until', '!x.cur'], ['wait', 1500]],
  'up-kill': [['do', 'x.gm.chest = 500; x.gm.pile = 6; x.visit()'], ['wait', 300], ['jump', 2500], ['wait', 800], ['jump', 9100], ['until', 'x.gm.visit?.phase === "tug"'], ['wait', 1200], tok(of(1)), ['until', 'x.cur?.name === "守住了"'], ['until', '!x.cur'], ['wait', 1500]],
  'zap-steal': [...LOOT, tok(of(0.55)), ['until', 'x.gm.visit.cut != null'], ['wait', 900], tok(of(1.55), -1), ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 800]],
  'zap-win': [...LOOT, ...ZAP, ...WIN],
  'empty-knocked': [...EMPTY, ['until', 'x.gm.visit?.phase === "sit"'], ['wait', 500], tok(1), ['until', 'x.cur?.name === "打跑了"'], ['until', '!x.cur'], ['wait', 800]],
  // one 小花花 over the empty chest: it laughs its thanks and is off with the chest, she cries (from sitting, and from
  // peering into it: the 小花花 waits till it is up, then it laughs from where it is)
  'empty-snatch': [...EMPTY, ['until', 'x.gm.visit?.phase === "sit"'], ['wait', 500], tok(1, -1), ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 800]],
  'empty-snatch-peer': [['do', 'x.visit()'], ['wait', 300], ['jump', 2500], ['jump', 9100], ['until', 'x.gm.visit?.phase === "peer"'], ['wait', 1500], tok(1, -1), ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 800]],
  'empty-toss': [...EMPTY, ['until', 'x.gm.visit?.phase === "sit"'], ['jump', 45100], ['until', '!x.gm.visit'], ['wait', 1200]],
  'scare-tug': [...LOOT, ...ZAP, ['do', 'x.fire("舰长", { price: 1980 })'], ['until', 'x.cur?.name === "舰长"'], ['until', '!x.cur'], ['wait', 800]],
  'scare-zap-admiral': [...LOOT, ...ZAP, ['do', 'x.fire("提督", { price: 19980 })'], ['until', 'x.cur?.name === "提督"'], ['until', '!x.cur'], ['wait', 800]],
  'scare-zap-governor': [...LOOT, ...ZAP, ['do', 'x.fire("总督", { price: 199980 })'], ['until', 'x.cur?.name === "总督"'], ['until', '!x.cur'], ['wait', 800]],
  'scare-zap-lottery': [...LOOT, ...ZAP, ['do', 'x.fire("报名开场")'], ['until', 'x.cur?.name === "报名开场"'], ['until', '!x.cur'], ['wait', 800]],
  'scare-big': [...LOOT, ['do', 'x.give([{ gift: "小电视飞船", price: 12450, n: 1 }])'], ['until', 'x.cur?.name === "大礼物"'], ['until', '!x.cur'], ['wait', 800]],
  'scare-big-zap': [...LOOT, ...ZAP, ['do', 'x.give([{ gift: "小电视飞船", price: 12450, n: 1 }])'], ['until', 'x.cur?.name === "大礼物"'], ['until', '!x.cur'], ['wait', 800]],
  // (2026-11-18 review) a 小花花 late from its spam as she is robbed; one just after a big gift; an empty chest decided in
  // the air; a big gift while it flies in with a full one; a lottery in the tug; a round of blind boxes to a 中礼物
  'late-gift': [...LOOT, tok(of(1), -1), ['wait', 500], tok(1, -1), ['until', 'x.cur?.name === "被偷了" && x.now() >= x.cur.start + x.cur.pre'], ['until', '!x.cur'], ['wait', 800]],
  'burst-race': [...LOOT, ['do', 'x.give([{ gift: "小电视飞船", price: 12450, n: 1 }])'], ['wait', 500], tok(of(1), -1), ['until', 'x.cur?.name === "大礼物"'], ['until', '!x.cur'], ['wait', 800]],
  'empty-air-kill': [['do', 'x.visit()'], ['wait', 300], ['jump', 2500], ['wait', 1500], tok(1), ['until', 'x.cur?.name === "打跑了"'], ['until', '!x.cur'], ['wait', 800]],
  'empty-air-steal': [['do', 'x.visit()'], ['wait', 300], ['jump', 2500], ['wait', 1500], tok(1, -1), ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 800]],
  'scare-air-big': [...AIR, ['do', 'x.give([{ gift: "小电视飞船", price: 12450, n: 1 }])'], ['until', 'x.cur?.name === "大礼物"'], ['until', '!x.cur'], ['wait', 800]],
  'scare-lottery': [...LOOT, ['do', 'x.fire("报名开场")'], ['until', 'x.cur?.name === "报名开场"'], ['until', '!x.cur'], ['wait', 800]],
  'box-mid': [...LOOT, ['do', "x.give([{ gift: '爱心抱枕', price: 160, n: 10, box: '心动盲盒', boxPrice: 150 }])"], ['until', 'x.cur?.name === "盲盒·中"'], ['until', '!x.cur'], ['wait', 800]],
  'scare-castle': [...LOOT, ['do', 'x.give([{ gift: "浪漫城堡", price: 22330, n: 1, box: "心动盲盒", boxPrice: 10, castle: true }])'], ['until', 'x.cur?.name === "城堡"'], ['until', '!x.cur'], ['wait', 800]],
  // (2026-11-18 second review) a big gift just after it wins, just after it steals; a gift after a PK scared it off a
  // frizzed tug; a 总督 just after a 舰长 that scared it off; a 小花花 and a 秒杀 while it only peeks in
  'win-big': [...LOOT, tok(of(1)), ['until', 'x.cur?.name === "守住了"'], ['wait', 1000], ['do', 'x.give([{ gift: "小电视飞船", price: 12450, n: 1 }])'], ['until', 'x.cur?.name === "大礼物"'], ['until', '!x.cur'], ['wait', 800]],
  'lose-big': [...LOOT, tok(of(1), -1), ['wait', 800], ['do', 'x.give([{ gift: "小电视飞船", price: 12450, n: 1 }])'], ['until', 'x.cur?.name === "大礼物"'], ['until', '!x.cur'], ['wait', 800]],
  'pk-zap-gift': [...LOOT, ...ZAP, ['do', 'x.pkOn(true)'], ['wait', 300], ['do', 'x.give([{ gift: "爱心抱枕", price: 1000, n: 1 }])'], ['until', 'x.cur?.name === "中礼物"'], ['until', '!x.cur'], ['wait', 800]],
  'guard-gov': [...LOOT, ...ZAP, ['do', 'x.fire("舰长", { price: 1980 })'], ['until', 'x.cur?.name === "舰长"'], ['wait', 500], ['do', 'x.fire("总督", { price: 199980 })'], ['until', 'x.cur?.name === "总督"'], ['until', '!x.cur'], ['wait', 800]],
  'peek-steal': [['do', 'x.visit()'], ['wait', 400], tok(1, -1), ['until', 'x.cur?.name === "被偷了"'], ['until', '!x.cur'], ['wait', 800]],
  'peek-kill': [['do', 'x.gm.chest = 500; x.gm.pile = 6; x.visit()'], ['wait', 400], ['until', 'x.gm.visit?.phase === "peek"'], tok(of(1)), ['until', 'x.cur?.name === "守住了"'], ['until', '!x.cur'], ['wait', 1500]],
  'pk-tug': [...LOOT, ['do', 'x.pkOn(true)'], ['until', '!x.gm.visit'], ['wait', 1500]],
  'pk-zap': [...LOOT, ...ZAP, ['do', 'x.pkOn(true)'], ['until', '!x.gm.visit'], ['wait', 1500]],
  'scare-air': [['do', 'x.gm.chest = 500; x.gm.pile = 6; x.visit()'], ['wait', 300], ['jump', 2500], ['wait', 3000], ['do', 'x.fire("舰长", { price: 1980 })'],
    ['until', 'x.cur?.name === "舰长"'], ['until', '!x.cur'], ['wait', 800]],
};
const props = { scene: '手动', controls: '隐藏', caption: '隐藏', room: '深色房间', game: '开' };
const url = `http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html&props=${encodeURIComponent(JSON.stringify(props))}`;
const play = steps => `(async () => {
  const x = dc, sleep = ms => new Promise(r => setTimeout(r, ms)), seen = {}, pkSeen = {}, errors = [];
  for (let i = 0; i < 150 && !x.gm; i++) await sleep(100);   // all routes load at once: the board may take a while to boot
  await sleep(600);
  const dh = x.drawHer.bind(x);
  x.drawHer = (g, st, now) => {
    const a = x.hv?.pose, r = dh(g, st, now), b = x.hv?.pose;
    if (a && b && a !== b && !((a + '>' + b) in seen)) { const w = x.route(a, b); seen[a + '>' + b] = w == null ? 'none' : w.length ? 'tween' : 'cut'; }
    return r;
  };
  // the prankster's changes too: a turn round (its in-betweens face one way, so a change that turns it is a plain switch
  // as written), in-betweens (pkRoute: drawn either way round, or by way of pulling its rope), cut as written (PKCUT), or none
  const dp = x.drawPk.bind(x);
  x.drawPk = (g, p) => {
    const a = x.pv?.pose, af = x.pv?.flip, r = dp(g, p);
    const cut = x.pkCut, k = a + '>' + p.pose, back = p.pose + '>' + a;
    if (a && a !== p.pose && !(k in pkSeen)) pkSeen[k] = !!af !== !!p.flip ? 'turn' : x.pkRoute(a, p.pose) ? 'tween' : cut.has(k) || cut.has(back) || p.pose === 'knocked' ? 'cut' : 'none';
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
  return { seen, pkSeen, errors };
})()`;
// run as a script: compare.mjs imports the routes
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const names = only.length ? only : Object.keys(ROUTES);
  const runs = await Promise.all(names.map(name => new Promise(done => {
    const p = spawn('node', [path.join(tools, 'cdp-eval.mjs'), url, '5000', play(ROUTES[name])], { stdio: ['ignore', 'pipe', 'ignore'] });
    const stall = setTimeout(() => p.kill(), 6 * 60000);   // a Chrome that stalls under the load of all of them at once
    let out = ''; p.stdout.on('data', d => { out += d; }); p.on('close', () => { clearTimeout(stall); try { done([name, JSON.parse(out)]); } catch { done([name, { seen: {}, errors: ['no result (stalled: run it alone)'] }]); } });
  })));
  const all = {}, pk = {};
  for (const [name, r] of runs) {
    if (r.errors?.length) console.log(`${name}: ERRORS ${r.errors.join(' | ')}`);
    for (const [pair, how] of Object.entries(r.seen || {})) (all[pair] ??= { how, routes: [] }).routes.push(name);
    for (const [pair, how] of Object.entries(r.pkSeen || {})) (pk[pair] ??= { how, routes: [] }).routes.push(name);
  }
  const pairs = Object.entries(all).sort((a, b) => a[1].how.localeCompare(b[1].how) || a[0].localeCompare(b[0]));
  const count = h => pairs.filter(([, v]) => v.how === h).length;
  console.log(`${names.length} routes: ${pairs.length} changes of her pose, ${count('tween')} with in-betweens, ${count('cut')} cut as written, ${count('none')} with no way across`);
  for (const [pair, v] of pairs) if (v.how !== 'tween') console.log(`  ${v.how === 'none' ? 'no way' : 'cut'}: ${pair}（${v.routes.join('、')}）`);
  const pks = Object.entries(pk).sort((a, b) => a[1].how.localeCompare(b[1].how) || a[0].localeCompare(b[0])), n = h => pks.filter(([, v]) => v.how === h).length;
  console.log(`the prankster: ${pks.length} changes of its pose, ${n('tween')} with in-betweens, ${n('turn')} turning round, ${n('cut')} cut as written, ${n('none')} with none`);
  for (const [pair, v] of pks) if (v.how === 'none') console.log(`  none: ${pair}（${v.routes.join('、')}）`);
}
