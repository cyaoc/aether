# Replaces the blade in a pose with the one from the sword sheet, so every image has the same straight blade, the same
# length and the cutting edge on the right side. The model cannot be trusted with any of these.
#   python paste-blade.py <in.webp> <out.webp> <guardX> <guardY> <tipX> <tipY> <length> <edgeX> <edgeY> [keep] [clip=<px>]
# guard, tip: points in the pose, for the axis. length: guard to tip, in image pixels.
# edgeX, edgeY: which way the cutting edge should face (a rough screen direction: the side of the blade nearest it wins).
# keep: paste over what is there instead of erasing the old blade first (it lies on a smear).
# clip=<px>: only draw this far from the guard (a blade going into the scabbard).
# start=<px>: start this far from the guard, to close the gap to the collar.
# pommel=<x>,<y>: lay the blade on the line of the hilt instead of the model's blade, which it often bends at the guard;
# tip still says where the old blade is, to erase it. Needs Pillow, NumPy, SciPy.
import os
import sys
import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.ndimage import map_coordinates

args = sys.argv[1:]
keep = 'keep' in args
clip = next((float(a[5:]) for a in args if a.startswith('clip=')), None)
start = next((float(a[6:]) for a in args if a.startswith('start=')), None)
pommel = next((np.array(a[7:].split(','), float) for a in args if a.startswith('pommel=')), None)
args = [a for a in args if a != 'keep' and not a.startswith(('clip=', 'start=', 'pommel='))]
src, dst, gx, gy, tx, ty, length, ex, ey = args
gx, gy, tx, ty, length, ex, ey = map(float, (gx, gy, tx, ty, length, ex, ey))

here = os.path.dirname(os.path.abspath(__file__))
blade = np.asarray(Image.open(os.path.join(here, '..', 'textures', 'blade.webp')).convert('RGBA')).astype(float)
BH, BW = blade.shape[:2]
GUARD_GAP = 54.0   # blade.webp starts this far (in its own pixels) from the middle of the guard
AXIS = BH / 2      # its back and edge are about equally far from this row

im = np.asarray(Image.open(src).convert('RGBA')).astype(float)
g, t = np.array([gx, gy]), np.array([tx, ty])
old_u = (t - g) / np.linalg.norm(t - g)
u = old_u if pommel is None else (g - pommel) / np.linalg.norm(g - pommel)
n = np.array([-u[1], u[0]])
scale = length / (BW + GUARD_GAP)
assert abs(n @ np.array([ex, ey])) > .2, 'the edge direction lies along the blade: give one across it'
if n @ np.array([ex, ey]) < 0:       # blade.webp has its edge on +n; flip it across the axis if it must face the other way
    blade = blade[::-1]
half = BH / 2 * scale

end = g + u * (length + 6)
pad_l, pad_t = int(max(0, 12 - min(end[0] - half, end[0] + half))), int(max(0, 12 - min(end[1] - half, end[1] + half)))
pad_r, pad_b = int(max(0, end[0] + half + 12 - im.shape[1])), int(max(0, end[1] + half + 12 - im.shape[0]))
out = np.pad(im, ((pad_t, pad_b), (pad_l, pad_r), (0, 0)))
H, W = out.shape[:2]
yy, xx = np.mgrid[0:H, 0:W]
rx, ry = xx - pad_l - g[0], yy - pad_t - g[1]
tt, ww = rx * u[0] + ry * u[1], rx * n[0] + ry * n[1]
t0 = start if start else GUARD_GAP * scale + 6
stop = clip if clip else length + 2

if not keep:
    old = float((t - g) @ old_u)
    ot, ow = rx * old_u[0] + ry * old_u[1], ry * old_u[0] - rx * old_u[1]
    # the old blade goes all the way to its tip, also when the new one is shorter; only a clipped blade stops early
    out[(ot > t0) & (ot < (stop + 4 if clip else max(old, length) + 12)) & (np.abs(ow) <= half * 1.25)] = 0
    # slivers of the old blade's edge fall just outside that band: first cut the thin ones, which may still hang off the guard,
    # and then whatever no longer touches the figure goes by size
    solid = out[..., 3] > 8
    out[solid & ~ndimage.binary_opening(solid, iterations=3) & (ot > t0) & (ot < max(old, length) + 24) & (np.abs(ow) <= half * 2.2)] = 0
    lab, cnt = ndimage.label(out[..., 3] > 8)
    if cnt > 1:
        sizes = np.bincount(lab.ravel())[1:]
        near = np.unique(lab[(ot > t0) & (ot < max(old, length) + 24) & (np.abs(ow) <= half * 3)])
        out[np.isin(lab, [i for i in near if i and sizes[i - 1] < sizes.max() * .015])] = 0
mask = (tt > t0) & (tt <= stop) & (np.abs(ww) <= half + 1)
sx = np.maximum((tt[mask] / scale) - GUARD_GAP, 1)  # short of the blade's base: repeat its first column
sy = ww[mask] / scale + AXIS
col = np.stack([map_coordinates(blade[..., c], [sy, sx], order=1, mode='constant', cval=0) for c in range(4)], 1)
under = out[mask]
al = col[:, 3:] / 255
res = np.concatenate([col[:, :3] * al + under[:, :3] * (1 - al), al * 255 + under[:, 3:] * (1 - al)], 1)
out[mask] = res
Image.fromarray(out.clip(0, 255).astype('uint8')).save(dst, quality=92, method=6)
print(os.path.basename(dst), 'size', W, H, 'shift', pad_l, pad_t, 'tip', np.round(g + u * length + [pad_l, pad_t]).tolist())
