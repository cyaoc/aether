"""Paste only the redrawn eyes from an edited sprite back onto the idle sprite, so blinking never shifts a pixel elsewhere.

python sprite-patch.py <idle.png> <edited.png> <out.png> <cx,cy,rx,ry> [<cx,cy,rx,ry> ...]

Each ellipse is one eye area in the idle sprite's pixels, taken whole from the edit with a soft 8 px edge. Make it
reach well past the eye's outline: a blend that still keeps a few percent of the old dark outline shows as a faint ring.
The alpha stays the idle sprite's, since the eyes sit inside its body.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

idle, edited, out = sys.argv[1], sys.argv[2], sys.argv[3]
a = Image.open(idle).convert("RGBA")
b = Image.open(edited).convert("RGBA").resize(a.size, Image.LANCZOS)
A, B = np.asarray(a, dtype=np.float32), np.asarray(b, dtype=np.float32)
mask = Image.new("L", a.size, 0)
draw = ImageDraw.Draw(mask)
for spec in sys.argv[4:]:
    cx, cy, rx, ry = map(int, spec.split(","))
    draw.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=255)
k = np.asarray(mask.filter(ImageFilter.GaussianBlur(8)), dtype=np.float32)[..., None] / 255
res = A.copy()
res[..., :3] = A[..., :3] * (1 - k) + B[..., :3] * k
Image.fromarray(res.round().astype(np.uint8)).save(out)
print(f"took {int((k[..., 0] > 0.5).sum())} pixels from the edit")
