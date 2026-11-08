"""Take one box from an edited image and paste it onto the original with a soft edge, so nothing outside it moves.

python patch-box.py <original> <edited> <out> <x0,y0,x1,y1> [feather]

The box is in the original's pixels; alpha stays the original's (the box must sit inside the figure).
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

orig, edited, out, box = sys.argv[1], sys.argv[2], sys.argv[3], tuple(map(int, sys.argv[4].split(",")))
feather = int(sys.argv[5]) if len(sys.argv) > 5 else 8
a = Image.open(orig).convert("RGBA")
b = Image.open(edited).convert("RGBA").resize(a.size, Image.LANCZOS)
mask = Image.new("L", a.size, 0)
ImageDraw.Draw(mask).rectangle(box, fill=255)
k = np.asarray(mask.filter(ImageFilter.GaussianBlur(feather)), dtype=np.float32)[..., None] / 255
A, B = np.asarray(a, dtype=np.float32), np.asarray(b, dtype=np.float32)
res = A.copy()
res[..., :3] = A[..., :3] * (1 - k) + B[..., :3] * k
Image.fromarray(res.round().astype(np.uint8)).save(out)
