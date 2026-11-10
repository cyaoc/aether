// Serves the desktop-pet board for local viewing: node docs/design/desktop-pet/tools/server.mjs [port]
// The board references images as /_blob/<id>; blobs.json maps each id to a file in the design packages. The tools' --page
// checks play Aether's 桌宠页 instead, served here as Aether's web service serves it: src/Aether.Core/Pages with the current
// character's folder as /characters/current/, and a /desktop-pet/events that tells the page it is on, in a connected room,
// with the default settings and her lines; the checks then drive the page's `pet` themselves.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { SETTINGS } from '../../../../tests/Aether.Core.Tests/Support/DesktopPetSettings.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const pages = path.resolve(here, '../../../../src/Aether.Core/Pages');
const port = Number(process.argv[2] ?? 8820);
const blobs = JSON.parse(fs.readFileSync(path.join(here, 'blobs.json'), 'utf8'));
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.webp': 'image/webp',
  '.png': 'image/png', '.json': 'application/json', '.woff2': 'font/woff2' };
const { lines } = JSON.parse(fs.readFileSync(path.join(pages, 'characters/1/character.json'), 'utf8'));
const state = JSON.stringify({ enabled: true, roomConnected: true, lottery: null, giftBoard: null, settings: SETTINGS, lines });

// The 桌宠页's files, as the web service maps them; anything that resolves outside Pages is missing.
function pageFile(pathname) {
  const file = path.join(pages, pathname === '/desktop-pet' ? 'desktop-pet/index.html' : decodeURIComponent(pathname).replace('/characters/current/', '/characters/1/'));
  return file.startsWith(pages) && fs.existsSync(file) && !fs.statSync(file).isDirectory() ? file : null;
}

http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  if (url.pathname === '/desktop-pet/events') {
    res.writeHead(200, { 'content-type': 'text/event-stream' });
    res.write(`event: state\ndata: ${state}\n\n`);
    return;
  }
  let file;
  if (url.pathname === '/render.html') file = path.join(here, 'render.html');
  else if (url.pathname.startsWith('/board/')) file = path.join(root, 'boards', path.basename(decodeURIComponent(url.pathname)));
  else if (url.pathname.startsWith('/_blob/') && blobs[path.basename(url.pathname)]) file = path.resolve(root, blobs[path.basename(url.pathname)]);
  else file = pageFile(url.pathname);
  if (!file || !fs.existsSync(file)) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': types[path.extname(file)] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(res);
}).listen(port, () => console.log(`http://localhost:${port}/render.html?board=DP-DesktopPet.dc.html`));
