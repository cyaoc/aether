// node drive.mjs <tools dir> <port> <out.png> '<steps json>' [room]
// Drives the board's chest game in headless Chrome and grabs the page (480 x 640) at each shot, side by side with its
// label. Steps: ["wait", ms] real time; ["jump", ms] moves the board's clock on (then waits 300 ms: pose changes run on
// real time); ["do", "js on x, the board"]; ["shot", "label"].
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';

const [tools, port, out, steps, room = '深色房间'] = process.argv.slice(2);
const props = { scene: '手动', controls: '隐藏', caption: '隐藏', room, game: '开' };
const url = `http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html&props=${encodeURIComponent(JSON.stringify(props))}`;
const js = `(async () => {
  const x = dc, cv = document.querySelector('canvas'), corner = [1400, 420], steps = ${steps};
  const shots = steps.filter(s => s[0] === 'shot').length, o = document.createElement('canvas');
  o.width = 480 * shots; o.height = 640 + 28; const g = o.getContext('2d');
  const sleep = ms => new Promise(r => setTimeout(r, ms)), frame = () => new Promise(r => requestAnimationFrame(r));
  await sleep(600);
  let i = 0; const errors = [];
  for (const [kind, arg] of steps) {
    try {
      if (kind === 'wait') await sleep(arg);
      if (kind === 'jump') { x.master.currentTime += arg / x.cfg.speed; await sleep(300); }
      if (kind === 'do') new Function('x', arg)(x);
      if (kind === 'until') { const ok = new Function('x', 'return ' + arg), t0 = performance.now(); while (!ok(x)) { if (performance.now() - t0 > 20000) throw new Error('never'); await sleep(16); } }
      if (kind === 'shot') {
        await frame(); await frame();
        g.drawImage(cv, ...corner, 480, 640, i * 480, 28, 480, 640);
        g.fillStyle = '#111'; g.fillRect(i * 480, 0, 480, 28); g.fillStyle = '#fff'; g.font = '16px sans-serif'; g.fillText(arg, i * 480 + 8, 20);
        i++;
      }
    } catch (e) { errors.push(kind + ' ' + arg + ': ' + e.message); }
  }
  return { png: o.toDataURL('image/png'), errors, gm: JSON.stringify({ chest: x.gm.chest, pile: x.gm.pile, streak: x.gm.streak, steals: x.gm.steals, phase: x.gm.visit?.phase ?? null }) };
})()`;
const res = JSON.parse(execFileSync('node', [tools + '/cdp-eval.mjs', url, '5000', js], { encoding: 'utf8', maxBuffer: 1 << 28 }));
fs.writeFileSync(out, Buffer.from(res.png.split(',', 2)[1], 'base64'));
console.log(res.gm, res.errors.length ? 'ERRORS ' + res.errors.join(' | ') : 'no errors');
