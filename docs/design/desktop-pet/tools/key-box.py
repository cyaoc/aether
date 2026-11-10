import sys
import numpy as np
from PIL import Image, ImageFilter
"""Replace one box of a transparent image with the same box keyed out of a green-screen edit.

python key-box.py <original> <edited-on-green> <out> <x0,y0,x1,y1>

For a change that reaches past the figure's outline: put the original on flat #00FF00, let codex edit it there with
no glow (an edit on transparency comes back with a semi-opaque haze), then key the box back in; outside the box
nothing moves.
"""
orig, edited, out, box = sys.argv[1], sys.argv[2], sys.argv[3], tuple(map(int, sys.argv[4].split(",")))
o = Image.open(orig).convert("RGBA"); O = np.asarray(o, dtype=np.float32)
E = np.asarray(Image.open(edited).convert("RGB").resize(o.size, Image.LANCZOS), dtype=np.float32)
spill = E[..., 1] - np.maximum(E[..., 0], E[..., 2])
a = 1 - np.clip((spill - 30) / (170 - 30), 0, 1)
key = np.array([0, 255, 0], np.float32)
F = np.clip((E - (1 - a[..., None]) * key) / np.maximum(a[..., None], 1e-3), 0, 255)
F[..., 1] = np.minimum(F[..., 1], np.maximum(F[..., 0], F[..., 2]) + 20)  # despill
x0, y0, x1, y1 = box
m = np.zeros(a.shape, np.float32); m[y0:y1, x0:x1] = 1
soft = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(6)), dtype=np.float32) / 255
res = O.copy()
res[..., :3] = O[..., :3] * (1 - soft[..., None]) + F * soft[..., None]
res[..., 3] = O[..., 3] * (1 - soft) + a * 255 * soft
Image.fromarray(res.round().clip(0, 255).astype(np.uint8)).save(out)
