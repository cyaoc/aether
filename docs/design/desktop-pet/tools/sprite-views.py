"""Cut the sprite's four turnaround views out of the character's sprite design sheet, onto transparency.

python sprite-views.py <mascot/textures/sprite-sheet.webp> <out.png>

The sheet is 4 x 2 on a light warm-grey ground; the top row is front, three-quarter and side (facing the viewer's left)
and back. Each cell's ground is flood-filled from its edges and corners, and only the piece holding the cell's centre is
kept, which drops the loose hearts drawn around the sprite. Out: one row of four transparent cells, same cell size.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

src = Image.open(sys.argv[1]).convert("RGB")
cw, ch = src.width // 4, src.height // 2
out = Image.new("RGBA", (cw * 4, ch), (0, 0, 0, 0))
for i in range(4):
    cell = src.crop((i * cw, 0, (i + 1) * cw, ch))
    key = cell.copy()
    for seed in [(0, 0), (cw - 1, 0), (0, ch - 1), (cw - 1, ch - 1), (cw // 2, 0), (0, ch // 2), (cw - 1, ch // 2), (cw // 2, ch - 1)]:
        ImageDraw.floodfill(key, seed, (255, 0, 255), thresh=14)
    a = np.asarray(key)
    fg = ~((a[..., 0] == 255) & (a[..., 1] == 0) & (a[..., 2] == 255))
    # shrink first so thin gaps split the hearts off, then keep the piece at the centre
    m = Image.fromarray((fg * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(5)).convert("RGB")
    ImageDraw.floodfill(m, (cw // 2, ch // 2), (255, 0, 0), thresh=10)
    mm = np.asarray(m)
    main = (mm[..., 0] == 255) & (mm[..., 1] == 0)
    alpha = Image.fromarray((main * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(0.8))
    c = cell.convert("RGBA")
    c.putalpha(alpha)
    out.paste(c, (i * cw, 0))
out.save(sys.argv[2])
