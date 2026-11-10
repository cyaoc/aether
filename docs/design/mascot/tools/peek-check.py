"""Measure gift-board poses: the straight cut at the bottom (as a fraction of the height, which scene.mjs uses as the
sprite anchor), the shoulder line, and an overlay against the current poses.

python peek-check.py <idle.png> <cheer.png> <old-idle.webp> <old-cheer.webp> <out.png>
"""
import sys
import numpy as np
from PIL import Image, ImageDraw


def cut(path):
    a = np.asarray(Image.open(path).convert("RGBA").resize((768, 1152)))[..., 3] > 128
    rows = a.sum(axis=1)
    bottom = int(np.nonzero(rows)[0].max())
    width = rows[bottom - 40:bottom - 5].max()  # the body is widest just above the cut
    return bottom / 1152, width / 768, bool((np.asarray(Image.open(path).convert("RGBA"))[..., 3] < 255).any())


paths = sys.argv[1:5]
for p in paths:
    c, w, alpha = cut(p)
    print(f"{p.split('/')[-1]}: cut at {c:.3f} of the height, body {w:.2f} of the width at the cut, transparent: {alpha}")
tiles = []
for new, old in [(paths[0], paths[2]), (paths[1], paths[3])]:
    o = Image.open(old).convert("RGBA").resize((768, 1152))
    n = Image.open(new).convert("RGBA").resize((768, 1152))
    base = Image.new("RGBA", (768, 1152), (40, 44, 60, 255))
    ghost = o.copy()
    ghost.putalpha(Image.fromarray((np.asarray(o)[..., 3] * 0.35).astype(np.uint8)))
    base = Image.alpha_composite(base, ghost)
    base = Image.alpha_composite(base, n)
    d = ImageDraw.Draw(base)
    d.line([(0, int(0.84 * 1152)), (768, int(0.84 * 1152))], fill=(255, 80, 80, 255), width=2)
    tiles.append(base.convert("RGB"))
sheet = Image.new("RGB", (768 * 2 + 20, 1152), "white")
for i, t in enumerate(tiles):
    sheet.paste(t, (i * 788, 0))
sheet.save(sys.argv[5])
