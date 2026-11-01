// Serves the design boards for local viewing: node docs/design/splash/tools/server.mjs [port]
// Reuses the board renderer of docs/design/lottery-textures; blobs.json maps each /_blob/<id> to a file in this package.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const renderer = path.resolve(root, '../lottery-textures/tools/render.html');
const port = Number(process.argv[2] ?? 8797);
const blobs = JSON.parse(fs.readFileSync(path.join(here, 'blobs.json'), 'utf8'));
const types = { '.html': 'text/html; charset=utf-8', '.webp': 'image/webp' };

http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  let file;
  if (url.pathname === '/render.html') file = renderer;
  else if (url.pathname.startsWith('/board/')) file = path.join(root, 'boards', path.basename(decodeURIComponent(url.pathname)));
  else if (url.pathname.startsWith('/_blob/') && blobs[path.basename(url.pathname)]) file = path.resolve(root, blobs[path.basename(url.pathname)]);
  if (!file || !fs.existsSync(file)) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': types[path.extname(file)] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(res);
}).listen(port, () => console.log(`http://localhost:${port}/render.html?board=SP1-Splash.dc.html`));
