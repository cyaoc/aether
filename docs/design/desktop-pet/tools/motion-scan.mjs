// node tools/motion-scan.mjs [port] [--page]: plays every event (each ending of a blind box too) and daily action on the board,
// once from calm and once from happy (where the sprite circles her), and tracks the sprite frame by frame: any frame it
// moves more than 20 px (at 60 fps) is printed. A sprite that dashes that fast reads as a jump: its circle used to take
// its angle from a clock, so a show handing over to the happy idle sent it across her at 29 px a frame, and stills never
// showed it. With --page it plays Aether's 桌宠页 instead, every row of its 演出表 with each 试演 variant. Speeds are taken on
// the clock the sprite's speed cap uses: the time each frame was drawn at, which follow() takes its step from (a
// performance.now read after the frame came a few ms late now and then made a 13 px step look like 22). server.mjs must be
// running on the port (8820); it plays in real time, about ten minutes for the whole board.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const here = new URL('.', import.meta.url).pathname;
const args = process.argv.slice(2), page = args.includes('--page') && args.splice(args.indexOf('--page'), 1);
const port = args[0] || 8820, LIMIT = 20;
const board = fs.readFileSync(here + '../boards/DP-DesktopPet.dc.html', 'utf8');
const block = name => board.match(new RegExp(`const ${name} = \\{([\\s\\S]*?)\\};`))[1];
const events = [...block('EVENTS').matchAll(/'?([^\s',{}]+?)'?: \{ tier: '\w+', dur:/g)].map(m => m[1]);
const kinds = [...block('DAILY').matchAll(/(\S+): \[/g), ...block('SPORTS').matchAll(/^\s+(\S+): \{ ready:/gm)].map(m => m[1]);

// What it plays on, as x, how x starts over, and its runs, each [label, show, options].
const target = page ? `
  const { pet: x } = await import('/desktop-pet/page.mjs'), { MOOD, seeded } = await import('/desktop-pet/engine.mjs');
  await Promise.all(Object.keys(x.art.sheets).map(k => x.loadSheet(k)));
  const reset = () => x.reset(seeded(7)), runs = [];
  for (const row of (await (await fetch('/desktop-pet/shows.json')).json()).shows.filter(row => row.group !== MOOD))
    for (const variant of row.tests) runs.push([row.id + (variant ? '·' + variant : ''), row.id, { result: variant, mishap: variant === 'Mishap' }]);` : `
  await new Promise(r => setTimeout(r, 500));
  const x = dc, reset = () => x.reset(), runs = [];
  for (const name of ${JSON.stringify(events)}) for (const opts of dc.eventVariants(name)) runs.push([name + (opts.result ? '·' + opts.result : ''), name, opts]);
  for (const kind of ${JSON.stringify(kinds)}) for (const mishap of [false, true]) runs.push([kind + (mishap ? '·出事' : ''), '日常', { kind, mishap }]);`;
const js = `(async () => {
  ${target}
  const frame = () => new Promise(r => requestAnimationFrame(r)), out = [];
  for (const happy of [false, true]) for (const [label, name, opts] of runs) {
    reset(); x.nextDaily = 1e12;
    const keepHappy = () => { if (happy) { const n = x.now(); x.recent = [n, n, n]; } };
    keepHappy(); for (let i = 0; i < 40; i++) { await frame(); keepHappy(); }
    x.start(name, opts);
    const t0 = x.now(), end = t0 + x.cur.pre + x.cur.dur + 2500;
    let prev = null, worst = 0, at = 0;
    while (x.now() < end) {
      await frame(); keepHappy();
      const sv = x.sv; if (!sv) continue;
      const p = [x.rt, sv.x, sv.y];
      if (prev && p[0] > prev[0]) { const step = Math.hypot(p[1] - prev[1], p[2] - prev[2]) / (p[0] - prev[0]) * 16.7; if (step > worst) { worst = step; at = x.now() - t0; } }
      prev = p;
    }
    if (worst > ${LIMIT}) out.push((happy ? '开心·' : '平静·') + label + '：一帧 ' + worst.toFixed(0) + ' px @' + Math.round(at) + 'ms');
  }
  return { runs: runs.length * 2, fast: out };
})()`;
const url = page ? `http://localhost:${port}/desktop-pet`
  : `http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html&props=${encodeURIComponent(JSON.stringify({ scene: '手动', controls: '隐藏', caption: '隐藏' }))}`;
const res = JSON.parse(execFileSync('node', [here + 'cdp-eval.mjs', url, '5000', js], { encoding: 'utf8', timeout: 30 * 60000 }));
console.log(`${res.runs} plays, ${res.fast.length} with the sprite moving more than ${LIMIT} px in a frame`);
for (const f of res.fast) console.log('  ' + f);
process.exitCode = res.fast.length ? 1 : 0;
