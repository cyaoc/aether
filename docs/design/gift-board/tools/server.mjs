// Serves the design board for local viewing: node docs/design/gift-board/tools/server.mjs [port]
// The board references uploads as /_blob/<id>; blobs.json maps each id to a file in the design packages, or to the B站
// address of a gift icon, guard frame or guard badge, which is fetched on demand and never committed.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const port = Number(process.argv[2] ?? 8799);
const blobs = JSON.parse(fs.readFileSync(path.join(here, 'blobs.json'), 'utf8'));
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.webp': 'image/webp', '.png': 'image/png', '.woff2': 'font/woff2', '.json': 'application/json', '.mp3': 'audio/mpeg' };

http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const blob = url.pathname.startsWith('/_blob/') ? blobs[path.basename(url.pathname)] : undefined;
  if (blob?.startsWith('https://')) {
    // B站's image hosts refuse foreign Referers; Node's fetch sends none.
    const remote = await fetch(blob).catch(() => null);
    if (!remote?.ok) { res.writeHead(502); res.end(); return; }
    res.writeHead(200, { 'content-type': remote.headers.get('content-type') ?? 'image/png' });
    res.end(Buffer.from(await remote.arrayBuffer()));
    return;
  }
  let file;
  if (url.pathname === '/render.html') file = path.join(here, 'render.html');
  else if (url.pathname.startsWith('/board/')) file = path.join(root, 'boards', path.basename(decodeURIComponent(url.pathname)));
  else if (url.pathname === '/audio/events.json') file = path.join(root, 'audio', 'events.json');
  else if (url.pathname.startsWith('/audio/sources/')) file = path.join(root, 'audio', 'sources', path.basename(decodeURIComponent(url.pathname)));
  else if (blob) file = path.resolve(root, blob);
  if (!file || !fs.existsSync(file)) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': types[path.extname(file)] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(res);
}).listen(port, () => console.log(`http://localhost:${port}/render.html?board=GB1-GiftBoard.dc.html`));
