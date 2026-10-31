# Shortens a hilt, or a blade lying on the body, whose end sticks out over the empty background: the far part slides in
# along the axis, so what lies on the body stays untouched. For a blade give the tip instead of the pommel.
#   python shorten-hilt.py <in> <out> <guardX> <guardY> <pommelX> <pommelY> <target> <from> [half]
# target: guard to pommel afterwards, in image pixels. from: how far from the guard the free part starts, past the fist.
# half: half the width of the band that moves (default 40); narrow it when something else runs close beside the sword.
# The cut is nudged so the diamonds of the wrap still line up. Needs Pillow, NumPy, SciPy.
import sys

import numpy as np
from PIL import Image
from scipy.ndimage import map_coordinates

src, dst, *nums = sys.argv[1:]
gx, gy, px, py, target, start, *rest = map(float, nums)
g, p = np.array([gx, gy]), np.array([px, py])
L = float(np.linalg.norm(p - g))
assert start < target < L, f'need from {start} < target {target} < hilt {L:.0f}'
u = (p - g) / L; n = np.array([-u[1], u[0]])
a = np.asarray(Image.open(src).convert('RGBA')).astype(float)
H, W = a.shape[:2]
ts = np.arange(start, L + 1)
xs, ys = g[0] + ts * u[0], g[1] + ts * u[1]
red = map_coordinates(a[..., 0] - a[..., 1], [ys, xs], order=1)   # the wrap's diamonds along the axis

want = int(round(L - target))
best, score = want, -2.0
free = target - start                 # what stays of the free part: on a short one the cut and the seam must stay small too
reach = min(24, int(free // 4))
for d in range(max(1, want - reach), want + reach + 1):
    if d > len(red) - 8:       # too little of the free part left to compare
        continue
    x, y = red[:len(red) - d], red[d:]
    if x.std() == 0 or y.std() == 0:
        continue
    c = float(np.corrcoef(x, y)[0, 1])
    if c > score:
        best, score = d, c

yy, xx = np.mgrid[0:H, 0:W]
t, w = (xx - g[0]) * u[0] + (yy - g[1]) * u[1], (xx - g[0]) * n[0] + (yy - g[1]) * n[1]
HALF = rest[0] if rest else 40
out = a.copy()
out[(t >= start) & (t <= L + 30) & (np.abs(w) <= HALF)] = 0
out[(t > L - best + 2) & (t <= L + 30) & (np.abs(w) <= HALF * 1.5)] = 0   # the old end's outline, just outside the band
m = (t >= start) & (t <= L + 30 - best) & (np.abs(w) <= HALF)
sx, sy = xx[m] + best * u[0], yy[m] + best * u[1]
moved = np.stack([map_coordinates(a[..., c], [sy, sx], order=1) for c in range(4)], 1)
fade = np.clip((t[m] - start) / min(28, free / 4), 0, 1)[:, None]                        # the two ends never quite match in width
out[m] = a[m] * (1 - fade) + moved * fade
Image.fromarray(out.clip(0, 255).astype('uint8')).save(dst)
print(f'{dst.rsplit("/", 1)[-1]} hilt {L:.0f} -> {L - best:.0f} match {score:.2f}')
