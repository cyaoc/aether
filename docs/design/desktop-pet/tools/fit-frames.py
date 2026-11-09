"""Fit a pose change's four frames to the two poses on the board: her feet on the ground, her height going from the first
pose's to the second's.

python fit-frames.py [--switch] [--flip] <strip.png> <first ref> <second ref> <out.png>

frame-patch.py --whole keeps codex's in-betweens whole, and codex draws her a little taller than the drawings it starts
from: her head lines up with the first pose but her feet sit 8-11 board px under the ground, so on the board she would
sink for two frames and her head would drop as the second pose comes in; and where the second pose is on another
sheet, codex draws it at that sheet's pixel size, up to 16% off on the board. Each frame is scaled whole about her feet
(the lowest row of her, under the middle of her) and moved so her feet stand on the first pose's ground line, at the
height the board gives it: frame 1 the first pose's, frame 4 the second pose's, frames 2 and 3 a third and two thirds of
the way, or with --switch (a change of clothes between frames 2 and 3) frame 2 the first pose's and frame 3 the
second's. Heights are measured on the board, each sheet scaled to her standing height of 250 (RULER in the board where
a sheet's first cell is not her plain height).

--flip: the second pose is too tall for the first pose's cell (on skis with a pompom hat, under the governor's hat), so
the frames are moved into the second pose's cell, where the board draws them at the same spot, in reverse order: the
board then plays them for the change from the second pose to the first, and backwards for the first to the second.
<ref> is as in frame-patch.py (`sheet.webp:3`).
"""
import json
import re
import sys
from pathlib import Path
import numpy as np
from PIL import Image

pkg = Path(__file__).resolve().parent.parent
switch, flip = "--switch" in sys.argv, "--flip" in sys.argv
src, first, second, out = [a for a in sys.argv[1:] if a not in ("--switch", "--flip")]
board = (pkg / "boards/DP-DesktopPet.dc.html").read_text(encoding="utf-8")
RULER = dict(re.findall(r"(\w+): (\d+)", re.search(r"const RULER = \{(.*?)\};", board).group(1))) if "const RULER" in board else {}
BLOB = dict(re.findall(r"(\w+): '([0-9a-f]{32})'", re.search(r"const BLOB = \{(.*?)\};", board, re.S).group(1)))
blobs = json.loads((pkg / "tools/blobs.json").read_text())
KEY = {Path(blobs[md5]).stem: k for k, md5 in BLOB.items() if md5 in blobs}


def sheet(spec):
    """The cell, the board px per pixel of its sheet, its ground line and its anchor (centre of mass), as the board has them."""
    path, _, n = spec.partition(":")
    im = np.asarray(Image.open(pkg / path).convert("RGBA"))
    h, w = im.shape[0] // 2, im.shape[1] // 3
    cells = [im[(i // 3) * h:(i // 3 + 1) * h, (i % 3) * w:(i % 3 + 1) * w] for i in range(6)]
    i = int(n) - 1
    rows = lambda a: np.nonzero(a[..., 3] > 40)[0]
    ruler = int(RULER.get(KEY[Path(path).stem], np.ptp(rows(cells[0])) + 1))
    ground = max(rows(cells[j]).max() for j in range((i // 3) * 3, (i // 3) * 3 + 3))
    al = cells[i][..., 3].astype(float)
    wt = np.where(al > 40, al, 0)
    return cells[i], 250 / ruler, ground, (wt * np.arange(w)).sum() / wt.sum()


def warp(c, sx, sy):
    """The picture c sampled at (sx, sy) for every output pixel, bilinear, premultiplied so edges blend right."""
    pm = c.astype(np.float32)
    pm[..., :3] *= pm[..., 3:] / 255
    H, W = c.shape[:2]
    x0, y0 = np.floor(sx).astype(int), np.floor(sy).astype(int)
    fx, fy = (sx - x0)[..., None], (sy - y0)[..., None]
    get = lambda yv, xv: np.where(((xv >= 0) & (xv < W) & (yv >= 0) & (yv < H))[..., None], pm[np.clip(yv, 0, H - 1), np.clip(xv, 0, W - 1)], 0)
    v = (get(y0, x0) * (1 - fx) + get(y0, x0 + 1) * fx) * (1 - fy) + (get(y0 + 1, x0) * (1 - fx) + get(y0 + 1, x0 + 1) * fx) * fy
    rgb = np.where(v[..., 3:] > 0, v[..., :3] * 255 / np.maximum(v[..., 3:], 1e-3), 0)
    return np.dstack([rgb, v[..., 3]])


a, ka, ga, cxa = sheet(first)
b, kb, gb, cxb = sheet(second)
im = np.asarray(Image.open(src).convert("RGBA")).astype(np.float32)
fw = im.shape[1] // 4
frames = [im[:, j * fw:(j + 1) * fw] for j in range(4)]
if flip:  # a pixel of the second pose's cell shows what this pixel of the first pose's cell showed, on the board
    H, W = b.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    r = ka / kb
    frames = [warp(f, cxa + (xx - cxb) / r, ga + (yy - gb) / r) for f in frames[::-1]]
    a, ka, ga, b, kb, gb = b, kb, gb, a, ka, ga
height = lambda c, g: g - np.nonzero(c[..., 3] > 40)[0].min()  # head (or hat) top to the ground line
ha, hb = height(a, ga) * ka, height(b, gb) * kb  # board px
steps = [0, 0, 1, 1] if switch else [0, 1 / 3, 2 / 3, 1]
res = []
for j, (c, t) in enumerate(zip(frames, steps)):
    ys, xs = np.nonzero(c[..., 3] > 40)
    feet = ys.max()
    cx = xs[ys > feet - 20].mean()
    f = (ha + (hb - ha) * t) / ka / (feet - ys.min())
    yy, xx = np.mgrid[0:c.shape[0], 0:c.shape[1]].astype(np.float32)
    res.append(warp(c, cx + (xx - cx) / f, feet + (yy - ga) / f))  # scaled about her feet, which land on the ground
    print(f"frame {j + 1}: x{f:.3f}, feet {feet - ga:+d} px to the ground")
Image.fromarray(np.clip(np.hstack(res), 0, 255).round().astype(np.uint8)).save(out)
