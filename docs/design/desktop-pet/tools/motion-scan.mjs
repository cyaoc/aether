// node tools/motion-scan.mjs [port]: plays every event (each ending of a blind box too) and daily action on the board,
// once from calm and once from happy (where the sprite circles her), and tracks the sprite frame by frame: any frame it
// moves more than 20 px (at 60 fps) is printed. A sprite that dashes that fast reads as a jump: its circle used to take
// its angle from a clock, so a show handing over to the happy idle sent it across her at 29 px a frame, and stills never
// showed it. Speeds are taken on the clock the sprite's speed cap uses (performance.now, as follow() does): on the board's
// own clock a frame that came late looked twice as fast. server.mjs must be running on the port (8820); it plays in
// real time, about ten minutes for the whole board.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const here = new URL('.', import.meta.url).pathname;
const port = process.argv[2] || 8820, LIMIT = 20;
const board = fs.readFileSync(here + '../boards/DP-DesktopPet.dc.html', 'utf8');
const block = name => board.match(new RegExp(`const ${name} = \\{([\\s\\S]*?)\\};`))[1];
const events = [...block('EVENTS').matchAll(/'?([^\s',{}]+?)'?: \{ tier: '\w+', dur:/g)].map(m => m[1]);
const kinds = [...block('DAILY').matchAll(/(\S+): \[/g), ...block('SPORTS').matchAll(/^\s+(\S+): \{ ready:/gm)].map(m => m[1]);

const js = `(async () => {
  await new Promise(r => setTimeout(r, 500));
  const frame = () => new Promise(r => requestAnimationFrame(r)), out = [];
  const runs = [];
  for (const name of ${JSON.stringify(events)}) for (const opts of dc.eventVariants(name)) runs.push([name + (opts.result ? '·' + opts.result : ''), name, opts]);
  for (const kind of ${JSON.stringify(kinds)}) for (const mishap of [false, true]) runs.push([kind + (mishap ? '·出事' : ''), '日常', { kind, mishap }]);
  for (const happy of [false, true]) for (const [label, name, opts] of runs) {
    dc.reset(); dc.nextDaily = 1e12;
    const keepHappy = () => { if (happy) { const n = dc.now(); dc.recent = [n, n, n]; } };
    keepHappy(); for (let i = 0; i < 40; i++) { await frame(); keepHappy(); }
    dc.start(name, opts);
    const t0 = dc.now(), end = t0 + dc.cur.pre + dc.cur.dur + 2500;
    let prev = null, worst = 0, at = 0;
    while (dc.now() < end) {
      await frame(); keepHappy();
      const sv = dc.sv; if (!sv) continue;
      const p = [performance.now(), sv.x, sv.y];
      if (prev && p[0] > prev[0]) { const step = Math.hypot(p[1] - prev[1], p[2] - prev[2]) / (p[0] - prev[0]) * 16.7; if (step > worst) { worst = step; at = dc.now() - t0; } }
      prev = p;
    }
    if (worst > ${LIMIT}) out.push((happy ? '开心·' : '平静·') + label + '：一帧 ' + worst.toFixed(0) + ' px @' + Math.round(at) + 'ms');
  }
  return { runs: runs.length * 2, fast: out };
})()`;
const url = `http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html&props=${encodeURIComponent(JSON.stringify({ scene: '手动', controls: '隐藏', caption: '隐藏' }))}`;
const res = JSON.parse(execFileSync('node', [here + 'cdp-eval.mjs', url, '5000', js], { encoding: 'utf8', timeout: 30 * 60000 }));
console.log(`${res.runs} plays, ${res.fast.length} with the sprite moving more than ${LIMIT} px in a frame`);
for (const f of res.fast) console.log('  ' + f);
process.exitCode = res.fast.length ? 1 : 0;
