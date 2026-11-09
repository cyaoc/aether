// node tools/check-board.mjs [port]: every change of pose in every event and daily action (in from standing, out back to
// it), and whether the board has a way across it: in-between frames, a cut written in CUT, or a route through them.
// Prints how many go each way and every change with no way across. server.mjs must be running on the port (8820).
// Event and daily names come from the board file and durations from the board itself (dc.start), so the check follows
// the board; an earlier copy with the durations written in kept checking the admiral at 7 s after it grew to 8.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const here = new URL('.', import.meta.url).pathname;
const port = process.argv[2] || 8820;
const board = fs.readFileSync(here + '../boards/DP-DesktopPet.dc.html', 'utf8');
const block = name => board.match(new RegExp(`const ${name} = \\{([\\s\\S]*?)\\};`))[1];
const events = [...block('EVENTS').matchAll(/'?([^\s',{}]+?)'?: \{ tier: '\w+', dur:/g)].map(m => m[1]);
const kinds = [...block('DAILY').matchAll(/(\S+): \[/g), ...block('SPORTS').matchAll(/^\s+(\S+): \{ ready:/gm)].map(m => m[1]);

const js = `(() => {
  const pairs = {}, EVENTS = ${JSON.stringify(events)}, KINDS = ${JSON.stringify(kinds)};
  const walk = (fn, dur, where) => {
    let prev = 'idle';
    for (let t = 0; t <= dur + 20; t += 20) { const p = t > dur ? 'idle' : fn(t).pose; if (p !== prev) { pairs[prev + '>' + p] ??= where; prev = p; } }
  };
  for (const name of EVENTS) { dc.start(name, {}); walk(t => dc.eventState(name, t, { seed: 0 }, 'x'), dc.cur.dur, name); }
  for (const kind of KINDS) for (const mishap of [false, true]) {
    dc.start('日常', { kind, mishap }); walk(t => dc.daily(t, dc.base(), { kind, mishap, seed: 0 }), dc.cur.dur, kind + (mishap ? '·出事' : ''));
  }
  dc.cur = null;
  const res = { events: EVENTS.length, daily: KINDS.length, changes: 0, tween: 0, cut: 0, none: [] };
  for (const [k, where] of Object.entries(pairs)) {
    const [a, b] = k.split('>'), r = dc.route(a, b); res.changes++;
    if (r == null) res.none.push(k + '（' + where + '）'); else if (r.length) res.tween++; else res.cut++;
  }
  return res;
})()`;
const url = `http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html`;
const res = JSON.parse(execFileSync('node', [here + 'cdp-eval.mjs', url, '6000', js], { encoding: 'utf8' }));
console.log(`${res.events} events, ${res.daily} daily actions: ${res.changes} changes of pose, ${res.tween} with in-betweens, ` +
  `${res.cut} cut as written, ${res.none.length} with no way across`);
for (const n of res.none) console.log('  no way: ' + n);
process.exitCode = res.none.length ? 1 : 0;
