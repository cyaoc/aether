"""Check a 3x2 pose sheet: per cell, the figure's top, bottom and height, against cell 1 (the idle ruler), and write a
labeled preview on a mid-gray backdrop.

python sheet-check.py <sheet.png> <preview.png> <name1,name2,...,name6>

Bottoms in one row should match (feet on one line); idle-like standing cells should match cell 1's height.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

src, out, names = sys.argv[1], sys.argv[2], sys.argv[3].split(",")
im = Image.open(src).convert("RGBA")
a = np.asarray(im)
if a[..., 3].min() == 255:  # no alpha: key out a flat #00FF00 backdrop
    rgb = a[..., :3].astype(int)
    alpha = np.where((rgb[..., 1] - np.maximum(rgb[..., 0], rgb[..., 2])) > 80, 0, 255).astype(np.uint8)
    im.putalpha(Image.fromarray(alpha))
    a = np.asarray(im)
H, W = a.shape[:2]
cw, ch = W // 3, H // 2
ruler = None
font = ImageFont.truetype("/System/Library/Fonts/Hiragino Sans GB.ttc", 22)
sheet = Image.new("RGBA", (W, H), (96, 100, 112, 255))
sheet.alpha_composite(im)
d = ImageDraw.Draw(sheet)
for i in range(6):
    x0, y0 = (i % 3) * cw, (i // 3) * ch
    cell = a[y0:y0 + ch, x0:x0 + cw, 3] > 40
    ys, xs = np.nonzero(cell)
    label = names[i] if i < len(names) else ""
    if len(ys) == 0:
        print(f"cell {i + 1} {label}: empty")
        d.text((x0 + 8, y0 + 6), f"{i + 1} {label}（空）", fill="white", font=font)
        continue
    top, bottom, h = ys.min(), ys.max(), ys.max() - ys.min()
    if i == 0:
        ruler = h
    edge = xs.min() < 3 or xs.max() > cw - 4 or top < 3 or bottom > ch - 4
    print(f"cell {i + 1} {label}: top {top}, bottom {bottom}, height {h} ({h / ruler * 100:.0f}% of idle){'  TOUCHES EDGE' if edge else ''}")
    d.rectangle([x0, y0, x0 + cw - 1, y0 + ch - 1], outline=(255, 255, 255, 90))
    d.line([(x0, y0 + bottom), (x0 + cw, y0 + bottom)], fill=(255, 120, 120, 200), width=1)
    d.text((x0 + 8, y0 + 6), f"{i + 1} {label}", fill="white", font=font)
sheet.convert("RGB").save(out)
