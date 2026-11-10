// node tools/play.mjs <event[:ending] | 日常:<kind>[:出事]> <ms,ms,...> <out.png> [--line] [--room 明亮画面|深色房间|透明] [--port 8820]
// Starts the event (a blind box with its ending: 赚, 平, 亏 or 大奖; or the daily action, with its mishap) on the board
// and plays it in real time, grabbing the page at those ms into it, side by side; --line draws a red line at her home x. Time-jumped stills (freeze) are not enough to
// judge motion: the board's pose changes run on real time, and stills taken by jumping the clock showed poses that never
// play together. With --page it plays a 试演 on Aether's 桌宠页 instead, named as its 演出表 names it: <show[:variant]>, such
// as blindBoxLarge:Jackpot or bike:Mishap, on a transparent ground. server.mjs must be running.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const here = new URL('.', import.meta.url).pathname;
const args = process.argv.slice(2), opt = (k, d) => { const i = args.indexOf(k); return i < 0 ? d : args.splice(i, 2)[1]; };
const line = args.includes('--line') && args.splice(args.indexOf('--line'), 1);
const page = args.includes('--page') && args.splice(args.indexOf('--page'), 1);
const room = opt('--room', '明亮画面'), port = opt('--port', 8820);
const [what, times, out] = args;
const [name, kind, mishap] = what.split(':');
// What it plays on, as x with its canvas cv and the page's corner on it, and how the show starts.
const target = page ? {
  url: `http://localhost:${port}/desktop-pet`,
  setup: `const { pet: x } = await import('/desktop-pet/page.mjs'); await Promise.all(Object.keys(x.art.sheets).map(k => x.loadSheet(k)));
    x.resize(480, 640); const cv = x.canvas, corner = [0, 0];`,
  start: `x.testShow({ show: ${JSON.stringify(name)}, variant: ${JSON.stringify(kind ?? null)}, nickname: '星河' })`
} : {
  url: `http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html&props=${encodeURIComponent(JSON.stringify({ scene: '手动', controls: '隐藏', caption: '隐藏', room }))}`,
  // the page in its default bottom-right corner
  setup: `const x = dc, cv = document.querySelector('canvas'), corner = [1400, 420];`,
  start: `x.start(${JSON.stringify(name)}, ${JSON.stringify(name === '日常' ? { kind, mishap: mishap === '出事' } : kind ? { result: kind } : {})})`
};
const js = `(async () => {
  ${target.setup}
  const ts = [${times}], out = document.createElement('canvas');
  out.width = 480 * ts.length; out.height = 640; const g = out.getContext('2d');
  await new Promise(r => setTimeout(r, 500)); ${target.start}; const c = x.cur;
  for (let i = 0; i < ts.length; i++) {
    while (x.now() - c.start < ts[i]) await new Promise(r => requestAnimationFrame(r));
    await new Promise(r => requestAnimationFrame(r));
    g.drawImage(cv, ...corner, 480, 640, i * 480, 0, 480, 640);
    if (${!!line}) { g.fillStyle = 'red'; g.fillRect(i * 480 + 240, 0, 1, 640); }
  }
  return out.toDataURL('image/png');
})()`;
const data = JSON.parse(execFileSync('node', [here + 'cdp-eval.mjs', target.url, '5000', js], { encoding: 'utf8', maxBuffer: 1 << 28 }));
fs.writeFileSync(out, Buffer.from(data.split(',', 2)[1], 'base64'));
