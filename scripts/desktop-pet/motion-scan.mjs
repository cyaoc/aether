// node scripts/desktop-pet/motion-scan.mjs [port]: plays every show of the 桌宠页 (each 试演 variant: blind box endings,
// lottery steps, mishaps) and 日常小动作, once from calm and once from happy (where the sprite circles her), and tracks the
// sprite frame by frame: any frame it moves more than 20 px (at 60 fps) is printed. The design package's
// tools/motion-scan.mjs, changed to read the shows from the 演出表 and play them on the page. Speeds are taken on the clock the
// sprite's speed cap uses: the time each frame was drawn at, which follow() takes its step from (a performance.now read after
// the frame came a few ms late now and then and made a 13 px step look like 22). server.mjs here must be serving the page on
// the port (8830); it plays in real time, a few minutes for the whole page.
import { execFileSync } from 'node:child_process';

const here = new URL('.', import.meta.url).pathname;
const port = process.argv[2] || 8830, LIMIT = 20;

const js = `(async () => {
  const { pet } = await import('/desktop-pet/page.mjs'), { seeded } = await import('/desktop-pet/engine.mjs');
  await Promise.all(Object.keys(pet.art.sheets).map(k => pet.loadSheet(k)));
  const frame = () => new Promise(r => requestAnimationFrame(r)), out = [], runs = [];
  for (const row of (await (await fetch('/desktop-pet/shows.json')).json()).shows.filter(row => row.group !== 'mood'))
    for (const variant of row.tests) runs.push([row.id + (variant ? '·' + variant : ''), row.id, { result: variant, mishap: variant === 'Mishap' }]);
  for (const happy of [false, true]) for (const [label, name, opts] of runs) {
    pet.reset(seeded(7)); pet.nextDaily = 1e12;
    const keepHappy = () => { if (happy) { const n = pet.now(); pet.recent = [n, n, n]; } };
    keepHappy(); for (let i = 0; i < 40; i++) { await frame(); keepHappy(); }
    pet.start(name, opts);
    const t0 = pet.now(), end = t0 + pet.cur.pre + pet.cur.dur + 2500;
    let prev = null, worst = 0, at = 0;
    while (pet.now() < end) {
      await frame(); keepHappy();
      const sv = pet.sv; if (!sv) continue;
      const p = [pet.rt, sv.x, sv.y];
      if (prev && p[0] > prev[0]) { const step = Math.hypot(p[1] - prev[1], p[2] - prev[2]) / (p[0] - prev[0]) * 16.7; if (step > worst) { worst = step; at = pet.now() - t0; } }
      prev = p;
    }
    if (worst > ${LIMIT}) out.push((happy ? '开心·' : '平静·') + label + '：一帧 ' + worst.toFixed(0) + ' px @' + Math.round(at) + 'ms');
  }
  return { runs: runs.length * 2, fast: out };
})()`;
const url = `http://localhost:${port}/desktop-pet`;
const res = JSON.parse(execFileSync('node', [here + '../../docs/design/desktop-pet/tools/cdp-eval.mjs', url, '5000', js], { encoding: 'utf8', timeout: 30 * 60000 }));
console.log(`${res.runs} plays, ${res.fast.length} with the sprite moving more than ${LIMIT} px in a frame`);
for (const f of res.fast) console.log('  ' + f);
process.exitCode = res.fast.length ? 1 : 0;
