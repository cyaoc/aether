// Serves the 桌宠页 for the checks next to it, as Aether's web service does: node scripts/desktop-pet/server.mjs [port]
// The design package's tools/server.mjs, changed to serve src/Aether.Core/Pages, the current character's folder as
// /characters/current/, and a /desktop-pet/events that tells the page it is on, in a connected room, with the default
// settings and her lines; the checks then drive the page's `pet` themselves.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const pages = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../src/Aether.Core/Pages');
const port = Number(process.argv[2] ?? 8830);
const types = { '.html': 'text/html; charset=utf-8', '.mjs': 'text/javascript', '.js': 'text/javascript', '.webp': 'image/webp', '.json': 'application/json', '.css': 'text/css', '.woff2': 'font/woff2' };
const { lines } = JSON.parse(fs.readFileSync(path.join(pages, 'characters/1/character.json'), 'utf8'));
const settings = { off: [], midGift: 100000, bigGift: 1000000, boredMinutes: 15, gloomyMinutes: 40, sleepyMinutes: 60, dailyMinSeconds: 20, dailyMaxSeconds: 40, mishapPercent: 25 };
const state = JSON.stringify({ enabled: true, roomConnected: true, lottery: null, giftBoard: null, settings, lines });

http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  if (url.pathname === '/desktop-pet/events') {
    res.writeHead(200, { 'content-type': 'text/event-stream' });
    res.write(`event: state\ndata: ${state}\n\n`);
    return;
  }
  const file = path.join(pages, url.pathname === '/desktop-pet' ? 'desktop-pet/index.html' : decodeURIComponent(url.pathname).replace('/characters/current/', '/characters/1/'));
  if (!file.startsWith(pages) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': types[path.extname(file)] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(res);
}).listen(port, () => console.log(`http://localhost:${port}/desktop-pet`));
