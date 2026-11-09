"""Stand a drawing as tall as the ruler, without touching her face: lift the head and stretch the body under it.

python lift.py <px> <top> <bottom> <in.png> <out.png> [--cells 3x2] [--only 2]

Codex drew some standing poses a little shorter than the everyday one (the wave: her body squashed between collar and
skirt hem, so her face sat 9 px lower and dropped all at once when she began to wave). Everything above row <top> moves
up <px> pixels as it is; from <top> down to <bottom> the lift fades out evenly, so that stretch of body grows by <px>;
below <bottom> nothing moves, so her legs and feet stay where the other drawings have them. Rows are counted inside a
cell. --cells: the grid (3x2 for a pose sheet, 4x1 for a frame strip); --only: the cells to lift, counted from 1 (all
when left out).
"""
import sys
import numpy as np
from PIL import Image


def opt(name, default):
    if name in sys.argv:
        k = sys.argv.index(name)
        v = sys.argv[k + 1]
        del sys.argv[k:k + 2]
        return v
    return default


cols, rows = map(int, opt("--cells", "3x2").split("x"))
only = {int(v) for v in opt("--only", "").split(",") if v}
px, top, bottom = float(sys.argv[1]), float(sys.argv[2]), float(sys.argv[3])
src, out = sys.argv[4], sys.argv[5]

im = np.asarray(Image.open(src).convert("RGBA")).astype(np.float32)
H, W = im.shape[:2]
cw, ch = W // cols, H // rows
# a row y of the result shows row y + lift(y) of the drawing (lift solved for the result's rows, so the stretch is even)
y = np.arange(ch, dtype=np.float32)
lift = np.clip((bottom - y) / (bottom - top + px), 0, 1) * px
sy = y + lift
y0 = np.clip(np.floor(sy).astype(int), 0, ch - 1)
y1 = np.clip(y0 + 1, 0, ch - 1)
f = (sy - np.floor(sy))[:, None, None]
res = im.copy()
for i in range(cols * rows):
    if only and i + 1 not in only:
        continue
    r, c = divmod(i, cols)
    cell = im[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw]
    pm = cell.copy()
    pm[..., :3] *= pm[..., 3:] / 255  # premultiplied, so edges blend right
    v = pm[y0] * (1 - f) + pm[y1] * f
    v[sy > ch - 1] = 0
    rgb = np.where(v[..., 3:] > 0, v[..., :3] * 255 / np.maximum(v[..., 3:], 1e-3), 0)
    res[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw] = np.dstack([rgb, v[..., 3]])
Image.fromarray(np.clip(res, 0, 255).round().astype(np.uint8)).save(out)
