// node scripts/desktop-pet/play.mjs <show[:variant]> <ms,ms,...> <out.png> [--line] [--port 8830]
// Starts the show on the 桌宠页 (a 试演 variant: a blind box's Gain, Even, Loss or Jackpot, a lottery step, a 日常小动作's
// Mishap) and plays it in real time, grabbing the page, 480 × 640 on a transparent ground, at those ms into it, side by side;
// --line draws a red line at her home x. The design package's tools/play.mjs, changed to play the page. server.mjs here must
// be running.
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const here = new URL('.', import.meta.url).pathname;
const args = process.argv.slice(2), opt = (k, d) => { const i = args.indexOf(k); return i < 0 ? d : args.splice(i, 2)[1]; };
const line = args.includes('--line') && args.splice(args.indexOf('--line'), 1);
const port = opt('--port', 8830);
const [what, times, out] = args;
const [show, variant = null] = what.split(':');
const js = `(async () => {
  const { pet } = await import('/desktop-pet/page.mjs');
  await Promise.all(Object.keys(pet.art.sheets).map(k => pet.loadSheet(k)));
  pet.resize(480, 640);
  const ts = [${times}], cv = pet.canvas, out = document.createElement('canvas');
  out.width = 480 * ts.length; out.height = 640; const g = out.getContext('2d');
  await new Promise(r => setTimeout(r, 500)); pet.testShow({ show: ${JSON.stringify(show)}, variant: ${JSON.stringify(variant)}, nickname: '星河' }); const c = pet.cur;
  for (let i = 0; i < ts.length; i++) {
    while (pet.now() - c.start < ts[i]) await new Promise(r => requestAnimationFrame(r));
    await new Promise(r => requestAnimationFrame(r));
    g.drawImage(cv, 0, 0, 480, 640, i * 480, 0, 480, 640);
    if (${!!line}) { g.fillStyle = 'red'; g.fillRect(i * 480 + 240, 0, 1, 640); }
  }
  return out.toDataURL('image/png');
})()`;
const url = `http://localhost:${port}/desktop-pet`;
const data = JSON.parse(execFileSync('node', [here + '../../docs/design/desktop-pet/tools/cdp-eval.mjs', url, '5000', js], { encoding: 'utf8', maxBuffer: 1 << 28 }));
fs.writeFileSync(out, Buffer.from(data.split(',', 2)[1], 'base64'));
