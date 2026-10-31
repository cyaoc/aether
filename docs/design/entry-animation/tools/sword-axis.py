# Measures how far a hilt bends away from its blade, by fitting both axes instead of reading points off the image.
#   python sword-axis.py <image> <guardX> <guardY> <tipX> <tipY> <pommelX> <pommelY>
# The three points only need to be rough. Fits the blade, finds the silver pommel cap, and prints the kink in degrees, the
# hilt / blade ratio and refined points, which go straight into paste-blade.py. Needs Pillow, NumPy, SciPy.
import sys

import numpy as np
from PIL import Image
from scipy import ndimage


def bands(mask, a, b, lo, hi, reach, wmin, wmax):
    """Middle of the solid band across the line a -> b, sampled from lo to hi along it, in pixels."""
    u = (b - a) / np.linalg.norm(b - a); n = np.array([-u[1], u[0]])
    H, W = mask.shape
    out = []
    for s in np.linspace(lo, hi, 48):
        c = a + u * s
        ws = np.arange(-reach, reach + 1)
        xs = np.clip(np.round(c[0] + ws * n[0]).astype(int), 0, W - 1)
        ys = np.clip(np.round(c[1] + ws * n[1]).astype(int), 0, H - 1)
        on = mask[ys, xs]
        if not on.any():
            continue
        w0, w1 = ws[on][0], ws[on][-1]
        if wmin <= w1 - w0 <= wmax:
            out.append((s, (w0 + w1) / 2))
    return np.array(out)


def fit(a, b, pts):
    """Refined line through the band middles: a point on it, its unit direction, and how many samples held."""
    u = (b - a) / np.linalg.norm(b - a); n = np.array([-u[1], u[0]])
    if len(pts) < 8:
        return None
    s, w = pts[:, 0], pts[:, 1]
    keep = np.ones(len(s), bool)
    for _ in range(3):
        k, c = np.polyfit(s[keep], w[keep], 1)
        r = np.abs(w - (k * s + c))
        keep = r <= max(2.0, 2 * np.median(r[keep]))
    if keep.sum() < 8:
        return None
    d = u + n * k
    return a + n * c, d / np.linalg.norm(d), int(keep.sum())


def pommel_cap(a, p, reach=64):
    """Centre of the largest bright, colourless patch near p: the silver cap. The gloved hands spoil any fit along the hilt."""
    H, W = a.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W]
    near = (xx - p[0]) ** 2 + (yy - p[1]) ** 2 <= reach ** 2
    rgb = a[..., :3]
    # skin and the white jacket are warm or huge; the cap is neutral grey-white
    silver = near & (a[..., 3] > 128) & (rgb.max(2) > 120) & (rgb.max(2) - rgb.min(2) < 34)
    lab, n = ndimage.label(silver)
    if not n:
        return None
    sizes = ndimage.sum(np.ones(lab.shape), lab, range(1, n + 1))
    i = int(np.argmax(sizes)) + 1
    if sizes[i - 1] < 60:
        return None
    y, x = ndimage.center_of_mass(lab == i)
    return np.array([x, y])


def measure(path, g, t, p):
    a = np.asarray(Image.open(path).convert('RGBA')).astype(int)
    alpha = a[..., 3] > 128
    pale = a[..., :3].min(2) > 150                # the white heart of a motion streak
    L = np.linalg.norm(t - g)
    blade = fit(g, t, bands(alpha & ~pale, g, t, .18 * L, .92 * L, 34, 8, 54))
    return blade, pommel_cap(a, p), L


if __name__ == '__main__':
    path, *nums = sys.argv[1:]
    v = list(map(float, nums))
    g, t, p = np.array(v[:2]), np.array(v[2:4]), np.array(v[4:6])
    blade, cap, L = measure(path, g, t, p)
    if blade is None or cap is None:
        sys.exit(f'no fit: blade {blade is not None} cap {cap is not None}; read the points by hand')
    b0, bu = blade[:2]
    guard = b0 + bu * ((g - b0) @ bu)             # along the axis a rough guard is good enough: the angle barely moves
    hu = (guard - cap) / np.linalg.norm(guard - cap)
    kink = np.degrees(np.arccos(np.clip(bu @ hu, -1, 1)))
    print(f'kink {kink:.1f} ratio {np.linalg.norm(guard - cap) / L:.2f} samples {blade[2]}')
    print(f'guard {guard.round(1).tolist()} pommel {cap.round(1).tolist()} tip {(guard + bu * L).round(1).tolist()}')
