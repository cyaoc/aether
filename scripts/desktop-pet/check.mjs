// node scripts/desktop-pet/check.mjs [port]: every change of pose in every show of the 桌宠页 (each 试演 variant: blind box
// endings, lottery steps, mishaps) and 日常小动作 (in from standing, out back to it), and whether the page has a way across
// it: in-between frames, a cut written in the manifest's cuts, or a route through them. The design package's
// tools/check-board.mjs, changed to read the shows from the 演出表 and play them on the page. server.mjs here must be serving
// the page on the port (8830).
import { execFileSync } from 'node:child_process';

const here = new URL('.', import.meta.url).pathname;
const port = process.argv[2] || 8830;

const js = `(async () => {
  const { pet } = await import('/desktop-pet/page.mjs');
  await Promise.all(Object.keys(pet.art.sheets).map(k => pet.loadSheet(k)));
  const pairs = {}, rows = (await (await fetch('/desktop-pet/shows.json')).json()).shows.filter(row => row.group !== 'mood');
  const walk = (fn, dur, where) => {
    let prev = 'idle';
    for (let t = 0; t <= dur + 20; t += 20) { const p = t > dur ? 'idle' : fn(t).pose; if (p !== prev) { pairs[prev + '>' + p] ??= where; prev = p; } }
  };
  for (const row of rows) for (const variant of row.tests) {
    const opts = { result: variant, mishap: variant === 'Mishap' };
    pet.start(row.id, opts); walk(t => pet.eventState(row.id, t, { ...opts, seed: 0 }, 'x'), pet.cur.dur, row.id + (variant ? '·' + variant : ''));
  }
  pet.cur = null;
  const res = { shows: rows.length, changes: 0, tween: 0, cut: 0, none: [] };
  for (const [k, where] of Object.entries(pairs)) {
    const [a, b] = k.split('>'), r = pet.route(a, b); res.changes++;
    if (r == null) res.none.push(k + '（' + where + '）'); else if (r.length) res.tween++; else res.cut++;
  }
  return res;
})()`;
const url = `http://localhost:${port}/desktop-pet`;
const res = JSON.parse(execFileSync('node', [here + '../../docs/design/desktop-pet/tools/cdp-eval.mjs', url, '6000', js], { encoding: 'utf8' }));
console.log(`${res.shows} shows and 日常小动作: ${res.changes} changes of pose, ${res.tween} with in-betweens, ` +
  `${res.cut} cut as written, ${res.none.length} with no way across`);
for (const n of res.none) console.log('  no way: ' + n);
process.exitCode = res.none.length ? 1 : 0;
