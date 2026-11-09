// node cdp-eval.mjs "<url>" <waitMs> "<js>": opens the page in headless Chrome, runs js (may return a promise) and prints
// its JSON result; console errors and exceptions go to stderr.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const [url, wait, js] = process.argv.slice(2);
const port = 9400 + Math.floor(Math.random() * 500);
const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'cdp-'));
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', [
  '--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${dir}`, '--window-size=1920,1080', '--hide-scrollbars',
  '--use-angle=metal', '--no-first-run', 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
try {
  let page;
  for (let i = 0; i < 50 && !page; i++) {
    await sleep(200);
    try { page = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === 'page'); } catch {}
  }
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise(r => ws.addEventListener('open', r, { once: true }));
  let id = 0;
  const pending = new Map();
  ws.addEventListener('message', e => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
    if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') console.error('error: ' + m.params.args.map(a => a.value ?? a.description).join(' '));
    if (m.method === 'Runtime.exceptionThrown') console.error('exception: ' + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
  });
  const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  await send('Runtime.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url });
  await sleep(+wait);
  const r = await send('Runtime.evaluate', { expression: js, awaitPromise: true, returnByValue: true });
  console.log(JSON.stringify(r.result?.result?.value ?? r.result));
  ws.close();
} finally {
  chrome.kill();
  setTimeout(() => fs.rmSync(dir, { recursive: true, force: true }), 500);
}
