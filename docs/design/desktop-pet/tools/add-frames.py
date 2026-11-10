"""Put frame strips, pose sheets and props into the design package and the board.

python add-frames.py <strip.png> [<strip.png> ...]
python add-frames.py --texture <board key> <picture.png> <textures/name.webp>

Each <name>-strip.png (from frame-patch.py) becomes textures/frames/<name>.webp; blobs.json maps its md5 to it (dropping
the entry of the file it replaces) and the board's FRAMES line gets <name>: '<md5>'. --texture does the same for one
picture under the board's BLOB (a pose sheet from gen-sheet.py, a prop from gen-prop.py), adding the key if it is new.
Restart server.mjs afterwards: it reads blobs.json once.
"""
import hashlib
import json
import re
import sys
from pathlib import Path
from PIL import Image

pkg = Path(__file__).resolve().parent.parent
blobs_path, board_path = pkg / "tools/blobs.json", pkg / "boards/DP-DesktopPet.dc.html"
blobs = json.loads(blobs_path.read_text())
board = board_path.read_text()
if sys.argv[1] == "--texture":
    key, src, rel = sys.argv[2:5]
    Image.open(src).save(pkg / rel, quality=92, method=6, exact=True)
    md5 = hashlib.md5((pkg / rel).read_bytes()).hexdigest()
    blobs = {k: v for k, v in blobs.items() if v != rel}
    blobs[md5] = rel
    blobs_path.write_text(json.dumps(blobs, indent=1, ensure_ascii=False))
    block = re.search(r"const BLOB = \{(.*?)\n\};", board, re.S)
    body = block.group(1)
    body = re.sub(rf"\b{key}: '[0-9a-f]{{32}}'", f"{key}: '{md5}'", body) if re.search(rf"\b{key}: '", body) else body.rstrip() + f",\n  {key}: '{md5}'"
    board_path.write_text(board[:block.start(1)] + body + board[block.end(1):])
    print(f"{key}: {md5}")
    sys.exit()
frames = dict(re.findall(r"(\w+): '([0-9a-f]{32})'", re.search(r"const FRAMES = \{(.*?)\};", board).group(1)))
for src in sys.argv[1:]:
    name = Path(src).name.removesuffix("-strip.png")
    rel = f"textures/frames/{name}.webp"
    Image.open(src).save(pkg / rel, quality=92, method=6, exact=True)
    md5 = hashlib.md5((pkg / rel).read_bytes()).hexdigest()
    blobs = {k: v for k, v in blobs.items() if v != rel}
    blobs[md5] = rel
    frames[name] = md5
    print(f"{name}: {md5}")
blobs_path.write_text(json.dumps(blobs, indent=1, ensure_ascii=False))
line = "const FRAMES = {" + ", ".join(f"{k}: '{v}'" for k, v in sorted(frames.items())) + "};"
board_path.write_text(re.sub(r"const FRAMES = \{.*?\};", lambda _: line, board, count=1))
