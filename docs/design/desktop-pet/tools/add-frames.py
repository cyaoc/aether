"""Put frame strips into the design package and the board.

python add-frames.py <strip.png> [<strip.png> ...]

Each <name>-strip.png (from frame-patch.py) becomes textures/frames/<name>.webp; blobs.json maps its md5 to it (dropping
the entry of the file it replaces) and the board's FRAMES line gets <name>: '<md5>'. Restart server.mjs afterwards: it
reads blobs.json once.
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
