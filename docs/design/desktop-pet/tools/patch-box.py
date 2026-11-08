"""Take one box from an edited image and paste it onto the original with a soft edge, so nothing outside it moves.

python patch-box.py <original> <edited> <out> <x0,y0,x1,y1> [feather] [alpha]

The box is in the original's pixels. The alpha stays the original's (the box sits inside the figure), unless the last
argument is "alpha": then the box takes the edit's alpha too, for a change that reaches past the figure's outline.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

orig, edited, out, box = sys.argv[1], sys.argv[2], sys.argv[3], tuple(map(int, sys.argv[4].split(",")))
feather = int(sys.argv[5]) if len(sys.argv) > 5 else 8
with_alpha = len(sys.argv) > 6 and sys.argv[6] == "alpha"
a = Image.open(orig).convert("RGBA")
b = Image.open(edited).convert("RGBA").resize(a.size, Image.LANCZOS)
mask = Image.new("L", a.size, 0)
ImageDraw.Draw(mask).rectangle(box, fill=255)
k = np.asarray(mask.filter(ImageFilter.GaussianBlur(feather)), dtype=np.float32)[..., None] / 255
A, B = np.asarray(a, dtype=np.float32), np.asarray(b, dtype=np.float32)
res = A.copy()
channels = slice(0, 4) if with_alpha else slice(0, 3)
res[..., channels] = A[..., channels] * (1 - k) + B[..., channels] * k
Image.fromarray(res.round().astype(np.uint8)).save(out)
