"""Take her out of the cabinet scenes with codex's erased picture as the mask (cut/her.png: the cabinets painted out, she
redrawn a little smaller or lower and with browner hair, so only its outline is used). Each cell's outline is scaled about
her feet and slid to fit her parts off the cabinet (left of it) in scene.png, grown 2 px, and cuts scene.png's own pixels.

python cut-mask.py   (in wip-gacha; writes scene/her.png, a 3 x 2 sheet with cell 1 the anchor, and scene/her-check.png)
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "tools"))

CELL, GROUND, CX = 512, 471, 265
sc = np.asarray(Image.open("scene/scene.png").convert("RGBA"))
er = np.asarray(Image.open(sys.argv[1] if len(sys.argv) > 1 else "cut/her-keyed.png").convert("RGBA"))
cab = Image.open("bay-fit-big/gacha-marked.png")
out = np.zeros_like(sc)
out[:CELL, :CELL] = sc[:CELL, :CELL]
check = Image.new("RGBA", (3 * CELL, 2 * CELL), (96, 100, 112, 255))


def place(E, s, dx, dy):
    w = round(CELL * s)
    Es = np.asarray(Image.fromarray((E * 255).astype(np.uint8)).resize((w, w), Image.BILINEAR)) > 127
    can = np.zeros((CELL, CELL), bool)
    ox, oy = CELL // 2 - w // 2 + dx, GROUND - round(GROUND * s) + dy
    sx0, sy0, tx0, ty0 = max(0, -ox), max(0, -oy), max(0, ox), max(0, oy)
    ww, hh = min(w - sx0, CELL - tx0), min(w - sy0, CELL - ty0)
    can[ty0:ty0 + hh, tx0:tx0 + ww] = Es[sy0:sy0 + hh, sx0:sx0 + ww]
    return can


for j, (x, y) in enumerate([(512, 0), (1024, 0), (0, 512)]):
    S, E = sc[y:y + CELL, x:x + CELL], er[y:y + CELL, x:x + CELL, 3] > 120
    off = np.zeros((CELL, CELL), bool)
    off[:, :CX - 2] = True
    best = None
    for s in np.arange(0.92, 1.13, 0.01):
        for dy in range(-30, 31, 2):
            for dx in range(-24, 25, 2):
                m = place(E, s, dx, dy)
                a, b = (S[..., 3] > 120) & off, m & off
                iou = (a & b).sum() / max(1, (a | b).sum())
                if best is None or iou > best[0]:
                    best = (iou, s, dx, dy)
    m = place(E, *best[1:])
    m = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))) > 0
    # the mask's outer 6 px may hold bits of the cabinet just outside her outline: from outside the mask, everything
    # reachable there without crossing a dark line (her outline) is not hers
    inner = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(13))) > 0
    band = m & ~inner
    passable = band & (S[..., :3].max(-1) > 110)
    reach = np.zeros_like(m)
    seed = np.asarray(Image.fromarray((~m * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3))) > 0
    reach = seed & passable
    for _ in range(12):
        grown = np.asarray(Image.fromarray((reach * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3))) > 0
        grown &= passable
        if (grown == reach).all():
            break
        reach = grown
    m &= ~reach
    cut = S.copy()
    cut[..., 3] = np.where(m, S[..., 3], 0)
    out[y:y + CELL, x:x + CELL] = cut
    print(["press", "turn", "take"][j], "fit %.3f scale %.2f dx %d dy %d" % best)
    top = Image.new("RGBA", (CELL, CELL), (96, 100, 112, 255))
    top.alpha_composite(Image.fromarray(cut))
    check.alpha_composite(top, (j * CELL, 0))
    both = Image.new("RGBA", (CELL, CELL), (96, 100, 112, 255))
    both.alpha_composite(cab, (CX, GROUND - cab.height))
    both.alpha_composite(Image.fromarray(cut))
    check.alpha_composite(both, (j * CELL, CELL))
Image.fromarray(out).save("scene/her.png")
check.convert("RGB").save("scene/her-check.png")
