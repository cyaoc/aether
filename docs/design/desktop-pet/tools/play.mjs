// node tools/play.mjs <event | 日常:<kind>[:出事]> <ms,ms,...> <out.png> [--line] [--room 明亮画面|深色房间|透明] [--port 8820]
// Starts the event (or the daily action, with its mishap) on the board and plays it in real time, grabbing the page at
// those ms into it, side by side; --line draws a red line at her home x. Time-jumped stills (freeze) are not enough to
// judge motion: the board's pose changes run on real time, and stills taken by jumping the clock showed poses that never
// play together. server.mjs must be running.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const here = new URL('.', import.meta.url).pathname;
const args = process.argv.slice(2), opt = (k, d) => { const i = args.indexOf(k); return i < 0 ? d : args.splice(i, 2)[1]; };
const line = args.includes('--line') && args.splice(args.indexOf('--line'), 1);
const room = opt('--room', '明亮画面'), port = opt('--port', 8820);
const [what, times, out] = args;
const [name, kind, mishap] = what.split(':');
const opts = name === '日常' ? { kind, mishap: mishap === '出事' } : {};
const props = encodeURIComponent(JSON.stringify({ scene: '手动', controls: '隐藏', caption: '隐藏', room }));
const js = `(async () => {
  const ts = [${times}], cv = document.querySelector('canvas'), out = document.createElement('canvas');
  out.width = 480 * ts.length; out.height = 640; const g = out.getContext('2d');
  await new Promise(r => setTimeout(r, 500)); dc.start(${JSON.stringify(name)}, ${JSON.stringify(opts)}); const c = dc.cur;
  for (let i = 0; i < ts.length; i++) {
    while (dc.now() - c.start < ts[i]) await new Promise(r => requestAnimationFrame(r));
    await new Promise(r => requestAnimationFrame(r));
    g.drawImage(cv, 1400, 420, 480, 640, i * 480, 0, 480, 640);  // the page in its default bottom-right corner
    if (${!!line}) { g.fillStyle = 'red'; g.fillRect(i * 480 + 240, 0, 1, 640); }
  }
  return out.toDataURL('image/png');
})()`;
const url = `http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html&props=${props}`;
const data = JSON.parse(execFileSync('node', [here + 'cdp-eval.mjs', url, '5000', js], { encoding: 'utf8', maxBuffer: 1 << 28 }));
fs.writeFileSync(out, Buffer.from(data.split(',', 2)[1], 'base64'));
