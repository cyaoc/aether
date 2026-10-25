// Serves the design boards for local viewing: node docs/design/online-picker/tools/server.mjs [port]
// Reuses the board renderer of docs/design/lottery-textures; these boards upload nothing, so there is no blobs.json.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const renderer = path.resolve(root, '../lottery-textures/tools/render.html');
const port = Number(process.argv[2] ?? 8796);

http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  let file;
  if (url.pathname === '/render.html') file = renderer;
  else if (url.pathname.startsWith('/board/')) file = path.join(root, 'boards', path.basename(decodeURIComponent(url.pathname)));
  if (!file || !fs.existsSync(file)) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
  fs.createReadStream(file).pipe(res);
}).listen(port, () => console.log(`http://localhost:${port}/render.html?board=OR1-OnlinePicker.dc.html`));
