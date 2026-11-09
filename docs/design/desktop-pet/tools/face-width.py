"""Measure how wide codex drew her face in every pose and every frame, and how much head-scale.py must scale each head.

python face-width.py [--show]

Codex draws every cell of a pose sheet with its own face: scaled to the board, her cheeks are 61 to 84 px wide where
the everyday standing pose (pose-1-common cell 1) has 73.4, so switching poses makes her face swell or slim (the user
saw it in the wave, then in several poses). Only a change of camera may change it, so every pose whose face looks at
the viewer gets its head scaled, about her chin, to the standing pose's cheek width; hands held against her face (both
hands on her cheeks, over her mouth, under her chin) are above her chin, so they scale with it and stay where they are.

Cheek width: her face is the biggest piece of skin in the upper half of the drawing (blush counted as skin, or a
blushing face falls apart), its holes filled column by column; her eyes are the two biggest holes side by side across
its middle; the width is the widest row from just above her eyes to just below them, where a hand seldom reaches.
Where her hands cover her cheeks (the measured width falls short of what her eye spacing gives on the standing face),
the width is judged from her eyes. Poses whose head is turned, bowed or seen from behind (EXCLUDED), and poses where no
eyes are found, take the middle factor of their sheet (the castle's first cell, the never-scaled ruler, not counted). Frames take their own measure when it is close to what their pose expects (within 10%, 15% for
an in-between frame: codex drew some in-between heads 10-14% small), else what their pose expects; a frame within 2% of it takes exactly that, so measuring noise never makes a loop's face pulse.
A loop gets one factor for all four frames (the middle of what its frames measure). An in-between frame of a pose
change expects a blend of the two poses' factors, and takes just that blend where either pose is turned or bowed. A factor within 1.5% of 1 is left as 1.

Prints JSON {"sheets": {file: [six factors]}, "frames": {name: [four factors]}}; --show also prints a table.
"""
import json
import re
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

pkg = Path(__file__).resolve().parent.parent
FILE = {"common": "pose-1-common", "lottery": "pose-2-lottery", "daily1": "pose-3-daily-1", "moods": "pose-5-moods",
        "gifts": "pose-6-gifts", "castleSheet": "pose-7-castle", "voyage": "pose-8-voyage", "ski9": "pose-9-ski",
        "gym10": "pose-10-gym", "hike11": "pose-11-hike", "cycle12": "pose-12-cycle"}
# head turned (hiking, cycling, the spyglass), bowed (bow, dozing, drawing circles, nodding, curtsy), seen from behind
# (walking into the castle, looking back) or upside down (stuck in the snow)
EXCLUDED = {"hike", "bike", "spyglass", "bow", "doze", "gloomy", "nod", "curtsy", "walkA", "walkB", "lookBack", "skiDown"}
# the wave already has the standing head laid over it (head-swap.py)
KEEP = {"wave"}

board = (pkg / "boards/DP-DesktopPet.dc.html").read_text(encoding="utf-8")
POSE = {p: (k, int(i)) for p, k, i in re.findall(r"(\w+): \['(\w+)', (\d)\]", re.search(r"const POSE = \{(.*?)\};", board, re.S).group(1))}
STRIP = dict(re.findall(r"(\w+): \{ f: '(\w+)'", re.search(r"const STRIP = \{(.*?)\n\};", board, re.S).group(1)))
TRANS = dict(re.findall(r"'(\w+>\w+)': '(\w+)'", re.search(r"const TRANS = \{(.*?)\n\};", board, re.S).group(1)))


def pieces(mask):
    img = Image.fromarray((mask * 255).astype(np.uint8)).copy()
    found = []
    for label in range(1, 250):
        ys, xs = np.nonzero(np.asarray(img) == 255)
        if not len(xs):
            break
        ImageDraw.floodfill(img, (int(xs[0]), int(ys[0])), label)
        found.append(np.asarray(img) == label)
    return sorted(found, key=lambda m: -m.sum())


def skin(a):
    r, g, b, al = (a[..., i].astype(int) for i in range(4))
    plain = (r > 195) & (g > 145) & (b > 115) & (r > g) & (g > b) & (r - b > 18)
    blush = (r > 180) & (g > 90) & (b > 80) & (r - g > 35) & (r - b > 25)
    return (al > 200) & (plain | blush)


def cheek_width(a):
    """(cheek width, eye spacing) in the picture's pixels, or None where her eyes are not found."""
    ys = np.nonzero(a[..., 3] > 40)[0]
    if not len(ys):
        return None
    y0, y1 = ys.min(), ys.max()
    s = skin(a)
    s[int(y0 + 0.55 * (y1 - y0)):] = False
    ps = pieces(s)
    if not ps or ps[0].sum() < 1500:
        return None
    face = ps[0]
    filled = np.zeros_like(face)
    for x in np.nonzero(face.any(0))[0]:
        c = np.nonzero(face[:, x])[0]
        filled[c.min():c.max() + 1, x] = True
    fy = np.nonzero(filled.any(1))[0]
    top, bot = fy.min(), fy.max()
    band = np.zeros_like(face)
    band[int(top + 0.3 * (bot - top)):int(top + 0.75 * (bot - top))] = True
    holes = [m for m in pieces(filled & ~face & band) if m.sum() > 150][:4]
    best = None
    for i in range(len(holes)):
        for j in range(i + 1, len(holes)):
            (xa, ya), (xb, yb) = [(np.nonzero(m)[1].mean(), np.nonzero(m)[0].mean()) for m in (holes[i], holes[j])]
            if abs(ya - yb) < 0.25 * abs(xa - xb) and abs(xa - xb) > 40 and (best is None or holes[i].sum() + holes[j].sum() > best[0]):
                best = (holes[i].sum() + holes[j].sum(), (ya + yb) / 2, abs(xa - xb))
    if not best:
        return None
    ey = int(best[1])
    return max(np.ptp(np.nonzero(filled[y])[0]) + 1 for y in range(ey - 15, ey + 11) if filled[y].any()), best[2]


def cells(file):
    im = np.asarray(Image.open(pkg / f"textures/{file}.webp").convert("RGBA"))
    h, w = im.shape[0] // 2, im.shape[1] // 3
    return [im[(i // 3) * h:(i // 3 + 1) * h, (i % 3) * w:(i % 3 + 1) * w] for i in range(6)]


def ruler(cs):
    ys = np.nonzero(cs[0][..., 3] > 40)[0]
    return ys.max() - ys.min() + 1


RULER = dict(re.findall(r"(\w+): (\d+)", re.search(r"const RULER = \{(.*?)\};", board).group(1))) if "const RULER" in board else {}
sheet_cells = {k: cells(f) for k, f in FILE.items()}
scale = {k: 250 / int(RULER.get(k, ruler(cs))) for k, cs in sheet_cells.items()}  # board px per picture px
std_w, std_eyes = cheek_width(sheet_cells["common"][0])
standard, ratio = std_w * scale["common"], std_w / std_eyes


def width(a):
    """Cheek width; where her hands cover her cheeks (narrower than her eyes allow: both hands held to her face), judged
    from her eyes instead."""
    m = cheek_width(a)
    if not m:
        return None
    w, eyes = m
    return eyes * ratio if w < 0.88 * eyes * ratio else w


def snap(f):
    return 1.0 if abs(f - 1) < 0.015 else round(float(f), 4)


measured, pose_f = {}, {}
for p, (k, i) in POSE.items():
    w = None if p in EXCLUDED or p in KEEP else width(sheet_cells[k][i])
    measured[p] = w and w * scale[k]
    pose_f[p] = 1.0 if p in KEEP else (standard / measured[p] if measured[p] else None)
for p, (k, i) in POSE.items():
    if pose_f[p] is None:  # the middle of what codex did on this sheet
        pose_f[p] = float(np.median([pose_f[q] for q, (kk, ii) in POSE.items()
                                     if kk == k and q not in EXCLUDED | KEEP and measured[q] and (ii or kk == "common")] or [1.0]))
sheets = {f: [1.0] * 6 for f in FILE.values()}
for p, (k, i) in POSE.items():
    sheets[FILE[k]][i] = snap(pose_f[p])


def frame_factors(name, expect, k, loop=False):
    trust = 0.10 if loop else 0.15  # codex drew some in-between heads 10-14% small (standing to crown V)
    im = np.asarray(Image.open(pkg / f"textures/frames/{name}.webp").convert("RGBA"))
    fw = im.shape[1] // 4
    out, notes = [], []
    for j in range(4):
        e = expect[j]
        w = width(im[:, j * fw:(j + 1) * fw]) if e is not None else None
        f = standard / (w * scale[k]) if w else None
        if f is None or abs(f / e - 1) > trust:
            out.append(e); notes.append("expected")
        elif abs(f / e - 1) <= 0.02:
            out.append(e); notes.append("measured ~")
        else:
            out.append(f); notes.append("measured")
    if loop:  # one size for the whole loop, so its face never pulses
        got = [v for v, n in zip(out, notes) if n != "expected"]
        out = [float(np.median(got)) if got else expect[0]] * 4
    return [snap(v) for v in out], notes


# a sheet's first cell is its ruler and was left out when the sheets were first scaled, but frames drawn from it were
# scaled with the rest of their sheet: for frames, it is already the size of its sheet
pose_f_frames = {p: 1.0 if i == 0 and k != "common" else f for p, ((k, i), f) in ((p, (POSE[p], pose_f[p])) for p in POSE)}
frames, why = {}, {}
for pose, f in STRIP.items():
    if pose in POSE:
        e = None if pose in EXCLUDED or pose in KEEP else pose_f_frames[pose]
        frames[f], why[f] = frame_factors(f, [e] * 4, POSE[pose][0], loop=True) if e else ([snap(pose_f_frames[pose])] * 4, ["pose"] * 4)
for pair, f in TRANS.items():
    a, b = pair.split(">")
    if a not in POSE or b not in POSE:
        continue
    fa, fb = pose_f_frames[a], pose_f_frames[b]
    e = [fa + (fb - fa) * t / 3 for t in range(4)]
    if a in EXCLUDED or b in EXCLUDED or (a in KEEP and b in KEEP):
        frames[f], why[f] = [snap(v) for v in e], ["blend"] * 4
    else:
        frames[f], why[f] = frame_factors(f, e, POSE[a][0])

if "--show" in sys.argv:
    print(f"standing cheek width {standard:.1f} board px")
    for p in POSE:
        m = measured[p]
        print(f"  {p:12s} {'%5.1f' % m if m else '    -'}  x{snap(pose_f[p])}" + ("  (turned/bowed/behind: sheet middle)" if p in EXCLUDED else "  (head laid over)" if p in KEEP else "" if m else "  (no eyes found: sheet middle)"))
    for f in frames:
        print(f"  frames {f:16s} {frames[f]}  {why[f]}")
print(json.dumps({"sheets": sheets, "frames": frames}))
