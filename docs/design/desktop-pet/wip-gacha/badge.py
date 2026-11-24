"""badge.py <gacha.png> <out.png>: print B站's little-TV mark in white on the cabinet's blank sky-blue plate.

The mark is simple-icons' bilibili icon (CC0, bilibili-tv.svg here, filled #00AEEC), rendered to a 512 px PNG beside it
first: qlmanage -t -s 512 -o . bilibili-tv.svg. codex is never asked to draw the mark: it drew it wrong. The plate is the
biggest solid sky-blue piece about as wide as tall; the mark fills 78% of it."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "tools"))
import numpy as np
from PIL import Image
from petlib import pieces

ICON = Path(__file__).resolve().parent / "bilibili-tv.svg.png"
src, out = sys.argv[1], sys.argv[2]
im = Image.open(src).convert("RGBA")
a = np.asarray(im).astype(int)
r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
white = (al > 200) & (b > 170) & (b - r > 90) & (g > 120)  # the sky-blue enamel
rows = np.arange(a.shape[0])[:, None]
best = None
for p in pieces(white):  # the blank plate: the biggest solid sky-blue piece about as wide as tall
    ys, xs = np.nonzero(p)
    h, w = np.ptp(ys) + 1, np.ptp(xs) + 1
    if p.sum() < 150 or not 0.75 < w / h < 1.33:
        continue
    fill = p.sum() / (np.pi * w * h / 4)  # 1 for a full disc
    score = p.sum() * (1 - abs(xs.mean() - a.shape[1] / 2) / a.shape[1])
    if best is None or score > best[0]:
        best = (score, xs.min(), ys.min(), xs.max(), ys.max(), fill)
if best is None:
    sys.exit("no blank badge plate found")
_, x0, y0, x1, y1, fill = best
d = min(x1 - x0, y1 - y0)
print(f"plate {x1 - x0 + 1} x {y1 - y0 + 1} px at {x0},{y0}, disc fill {fill:.2f}")
ic = np.asarray(Image.open(ICON).convert("RGB")).astype(int)
mask = Image.fromarray(np.clip((255 - ic[..., 0]) * 1.2, 0, 255).astype(np.uint8))  # blue on white: red channel is the ink
s = round(d * 0.78)
mask = mask.resize((s, s), Image.LANCZOS)
ink = Image.new("RGBA", (s, s), (255, 255, 255, 255))
ink.putalpha(mask)
im.alpha_composite(ink, (round((x0 + x1) / 2 - s / 2), round((y0 + y1) / 2 - s / 2)))
im.save(out)
