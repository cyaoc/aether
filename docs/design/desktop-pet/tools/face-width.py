"""Measure how wide codex drew her face in every pose, and how much head-scale.py must scale each head.

python face-width.py [--show]

Codex draws every cell of a pose sheet with its own face: scaled to the board, her cheeks were 61 to 84 px wide where
the everyday standing pose (pose-1-common cell 1) has 74, so switching poses made her face swell or slim (the user saw
it in the wave, then in several poses). Only a change of camera may change it, so every pose whose face looks at the
viewer gets its head scaled, about her chin, to the standing pose's cheek width; hands held against her face (both
hands on her cheeks, over her mouth, under her chin) are above her chin, so they scale with it and stay where they are.
The sheets in the package are already scaled: run it on a new sheet, or to check that every pose reads 1.

Cheek width: her face is the biggest piece of skin in the upper half of the drawing, its holes filled column by column;
her eyes are the two biggest holes side by side across its middle; the width is the widest row from just above her eyes
to just below them, where a hand seldom reaches. Where both her hands cover her cheeks (COVERED: hiding her face when
shy), the cheeks cannot be measured and the gaps between her fingers pass for eyes, so the size is judged from her eyes
instead: the standing pose's two eyes are found in the drawing by template matching, each on its own and at its own
scale, and the distance between them (tilt does not change it) times the standing pose's cheeks-to-eyes ratio stands for
her cheek width. Poses whose head is turned, bowed or seen from behind (EXCLUDED), and poses where no eyes are found,
take the middle factor of their sheet (a sheet's first cell, its ruler, not counted unless it is the standing sheet's).
A factor within 1.5% of 1 is left as 1. Frames are not measured here: a loop takes its pose's factor, and in-betweens
are made from the poses as they are (inbetween.py).

Prints JSON {sheet file: [six factors]}; --show also prints a table.
"""
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))  # petlib sits beside the tools, also under python -I
from petlib import POSE, SHEET, cells, cheek_width, scale

# head turned (hiking, cycling, the spyglass), bowed (bow, dozing, drawing circles, nodding, curtsy), seen from behind
# (walking into the castle, looking back) or upside down (stuck in the snow)
EXCLUDED = {"hike", "bike", "spyglass", "bow", "doze", "gloomy", "nod", "curtsy", "walkA", "walkB", "lookBack", "skiDown"}
# the wave has the standing head laid over it
KEEP = {"wave"}
# both hands cover her cheeks: judged from her eyes
COVERED = {"shy"}


def board_gray(a, k):
    """The drawing on white, 2 px per board px."""
    im = Image.fromarray(a)
    im = im.resize((round(im.width * k * 2), round(im.height * k * 2)), Image.LANCZOS)
    bg = Image.new("RGBA", im.size, (255, 255, 255, 255))
    bg.alpha_composite(im)
    return np.asarray(bg.convert("L"), dtype=np.float32)


standing = cells(SHEET["common"])[0]
std_w, std_boxes = cheek_width(standing)
standard = std_w * scale("common")
_g, _k2 = board_gray(standing, scale("common")), 2 * scale("common")
EYES = [_g[int(y0 * _k2) - 10:int(y1 * _k2) + 4, int(x0 * _k2) - 6:int(x1 * _k2) + 6] for x0, y0, x1, y1 in std_boxes]


def eye_gap(a, k):
    """Board px between her two eyes, each found by matching the standing pose's eye at scales 0.84-1.16."""
    from numpy.lib.stride_tricks import sliding_window_view
    g = board_gray(a, k)
    centres = []
    for t in EYES:
        best = None
        for s in np.arange(0.84, 1.17, 0.03):
            ts = np.asarray(Image.fromarray(t).resize((round(t.shape[1] * s), round(t.shape[0] * s)), Image.BILINEAR), dtype=np.float32)
            th, tw = ts.shape
            win = sliding_window_view(g[:int(g.shape[0] * 0.45)], (th, tw))[::2, ::2]
            wz, tz = win - win.mean(axis=(2, 3), keepdims=True), ts - ts.mean()
            ncc = (wz * tz).sum(axis=(2, 3)) / (np.sqrt((wz ** 2).sum(axis=(2, 3)) * (tz ** 2).sum()) + 1e-6)
            iy, ix = np.unravel_index(ncc.argmax(), ncc.shape)
            if best is None or ncc[iy, ix] > best[0]:
                best = (ncc[iy, ix], ix * 2 + tw / 2, iy * 2 + th / 2)
        centres.append(best[1:])
    return np.hypot(centres[1][0] - centres[0][0], centres[1][1] - centres[0][1]) / 2


std_gap = eye_gap(standing, scale("common"))


def width(a, k, covered=False):
    """Her cheek width in board px; for a face behind her hands, what her eye gap says it is."""
    if covered:
        return eye_gap(a, k) * standard / std_gap
    m = cheek_width(a)
    return m and m[0] * k


measured, factor = {}, {}
for p, (k, i) in POSE.items():
    measured[p] = None if p in EXCLUDED or p in KEEP else width(cells(SHEET[k])[i], scale(k), p in COVERED)
    factor[p] = 1.0 if p in KEEP else (standard / measured[p] if measured[p] else None)
for p, (k, i) in POSE.items():
    if factor[p] is None:  # the middle of what codex did on this sheet
        factor[p] = float(np.median([factor[q] for q, (kk, ii) in POSE.items()
                                     if kk == k and q not in EXCLUDED | KEEP and measured[q] and (ii or kk == "common")] or [1.0]))
snap = lambda f: 1.0 if abs(f - 1) < 0.015 else round(float(f), 4)
sheets = {f: [1.0] * 6 for f in SHEET.values()}
for p, (k, i) in POSE.items():
    sheets[SHEET[k]][i] = snap(factor[p])

if "--show" in sys.argv:
    print(f"standing cheek width {standard:.1f} board px, eye gap {std_gap:.1f}")
    for p in POSE:
        m = measured[p]
        note = ("  (turned/bowed/behind: sheet middle)" if p in EXCLUDED else "  (head laid over)" if p in KEEP
                else "  (behind her hands: from her eyes)" if p in COVERED else "" if m else "  (no eyes found: sheet middle)")
        print(f"  {p:12s} {'%5.1f' % m if m else '    -'}  x{snap(factor[p])}{note}")
print(json.dumps(sheets))
