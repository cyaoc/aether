"""Measure how big codex drew her face on each pose sheet, for head-scale.py.

python face-scale.py

Every sheet's first cell is the same everyday standing pose, drawn to the same height on the board, yet her eyes sit
37 to 46 px apart depending on the sheet: codex gave each sheet its own head-to-body ratio, and the other poses of a
sheet follow its first cell. The face is measured by the distance between her eyes (inside her face, the skin with
its holes filled column by column, the two biggest holes side by side across its middle); a hand at her cheek or a
hat over her forehead does not change it. Prints, for each sheet, how much head-scale.py must enlarge its heads to
match the everyday sheet (pose-1-common).
"""
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

pkg = Path(__file__).resolve().parent.parent
SHEETS = {"common": "pose-1-common", "lottery": "pose-2-lottery", "daily1": "pose-3-daily-1", "moods": "pose-5-moods",
          "gifts": "pose-6-gifts", "castleSheet": "pose-7-castle", "voyage": "pose-8-voyage", "ski9": "pose-9-ski",
          "gym10": "pose-10-gym", "hike11": "pose-11-hike", "cycle12": "pose-12-cycle"}


def pieces(mask):
    """Connected pieces of a boolean mask, biggest first, as boolean masks."""
    img = Image.fromarray((mask * 255).astype(np.uint8)).copy()  # its own pixels, so the fills below stick
    out = []
    for label in range(1, 250):
        ys, xs = np.nonzero(np.asarray(img) == 255)
        if not len(xs):
            break
        ImageDraw.floodfill(img, (int(xs[0]), int(ys[0])), label)
        out.append(np.asarray(img) == label)
    return sorted(out, key=lambda m: -m.sum())


def eye_distance(cell):
    a = np.asarray(cell).astype(int)
    r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    ys, xs = np.nonzero(al > 40)
    if not len(ys):
        return None
    y0, y1 = ys.min(), ys.max()
    skin = (al > 200) & (r > 195) & (g > 145) & (b > 115) & (r > g) & (g > b) & (r - b > 18)
    skin[int(y0 + 0.6 * (y1 - y0)):] = False
    ps = pieces(skin)
    if not ps or ps[0].sum() < 1500:
        return None
    face = ps[0]
    # holes: in every column, what lies between the face's top and bottom skin (an eye has forehead above it and cheek
    # below it, even where it reaches the edge of her face)
    filled = np.zeros_like(face)
    for x in np.nonzero(face.any(0))[0]:
        col = np.nonzero(face[:, x])[0]
        filled[col.min():col.max() + 1, x] = True
    holes = filled & ~face
    fy, fx = np.nonzero(filled)
    top, bot = fy.min(), fy.max()
    band = np.zeros_like(holes)
    band[int(top + 0.3 * (bot - top)):int(top + 0.7 * (bot - top))] = True
    eyes = [m for m in pieces(holes & band) if m.sum() > 60][:4]
    if len(eyes) < 2:
        return None
    cs = [(np.nonzero(m)[1].mean(), np.nonzero(m)[0].mean(), m.sum(), np.ptp(np.nonzero(m)[0]) + 1, np.ptp(np.nonzero(m)[1]) + 1) for m in eyes]
    # the two biggest holes side by side at about the same height
    best = None
    for i in range(len(cs)):
        for j in range(i + 1, len(cs)):
            (xa, ya, na, ha, wa), (xb, yb, nb, hb, wb) = cs[i], cs[j]
            if abs(ya - yb) < 0.25 * abs(xa - xb) and abs(xa - xb) > 30 and min(na, nb) > 0.35 * max(na, nb):
                if best is None or na + nb > best[2]:
                    # an open eye is roughly round; a closed one is a thin arc, whose height says nothing about size
                    open_ = min(ha / wa, hb / wb) > 0.6
                    best = (abs(xa - xb), (ha + hb) / 2 if open_ else None, na + nb)
    return best[:2] if best else None


ref = None
factors = {}
for key, f in SHEETS.items():
    im = Image.open(pkg / f"textures/{f}.webp").convert("RGBA")
    w, h = im.width // 3, im.height // 2
    cell = im.crop((0, 0, w, h))
    ys = np.nonzero(np.asarray(cell)[..., 3] > 40)[0]
    d = eye_distance(cell)[0] * 250 / (ys.max() - ys.min() + 1)  # board pixels at the standing height of 250
    ref = ref or d
    factors[f] = round(ref / d, 3)
    print(f"{f:16s} eyes {d:5.1f} px apart, heads x{factors[f]}")
print(json.dumps(factors))
