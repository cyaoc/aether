"""Widen the cabinet's battery bay to the battery codex draws (it draws a small prop at about 0.14 of her height whatever it
is asked): the battery stands right of the cabinet in the picture as the ruler, codex redraws the panel, and only the panel
is pasted back onto the cabinet she chose (A), so nothing else of it moves.

python bay-fit.py <tries> [big]  (in wip-gacha; reads battery-fit/try3.png and gacha-a2/base.png, writes bay-fit/;
with big: the taller cabinet in gacha-big/, the battery pasted beside it, writes bay-fit-big/)
"""
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "tools"))
from petlib import PKG, key, pieces

HERE = Path(__file__).resolve().parent
BIG = "big" in sys.argv[2:]
OUT = HERE / ("bay-fit-big" if BIG else "bay-fit")
OUT.mkdir(exist_ok=True)
BATTERY = (33, 50)  # the battery's width and height at her 360 px
if BIG:  # the taller cabinet (its bay came out 65 x 82): the battery pasted right of it, level with the bay
    BASE = HERE / "gacha-big/gacha-raw.png"
    bx0, by0, bx1, by1 = [int(v) for v in (HERE / "gacha-big/gacha.box.txt").read_text().split()]
    PANEL = (bx0 + 22, by0 + 289, bx0 + 212, by0 + 384)  # under the gold trim above it, where the bay is looked for
    PASTE = (bx0 + 22, by0 + 268, bx0 + 212, by0 + 386)  # pasted back with the trim, so no trace of the old bay is left
    SRC = OUT / "src.png"
    _im = Image.open(BASE).convert("RGBA")
    _im.alpha_composite(Image.open(HERE / "battery-fit/battery.png"), (bx1 + 40, by0 + 305))
    _im.convert("RGB").save(SRC)
else:
    SRC = HERE / "battery-fit/try3.png"  # her, the cabinet with the small bay, the battery right of it
    BASE = HERE / "gacha-a/gacha-raw.png"  # the cabinet she chose, before any edit
    PANEL = (410, 786, 528, 858)  # the cream panel between the window and the outlet, on the 1024 canvas
    PASTE = PANEL
PROMPT = ("Use your built-in image generation tool (not code) to edit the attached image into ONE new image, then copy the "
          "generated PNG file to {out}. Do not write any code. The attached image shows a chibi girl, a capsule-toy cabinet "
          "and, right of the cabinet, one battery, on flat #00FF00 green. Change ONLY the cream-white panel under the "
          "cabinet's glass window; keep the girl, the battery, the green, the sign, the window, the capsules, the pink "
          "stripes, the gold trim, the capsule outlet with its pink flap and the gold plinth exactly as they are, pixel for "
          "pixel. On that panel, resize the empty battery-shaped bay with the thin gold rim so that the battery at "
          "the right would fit snugly inside its gold rim: the hollow inside the rim exactly as tall and as wide as that "
          "battery, nub included. "
          "The bay stays EMPTY, an upright battery-shaped hollow, to the right of the knob at the same height. The parts on "
          "the front must NOT look like a face. Same rendering as the rest of the picture.")


def bay(a):
    """The bay's gold rim (outer width, height) on the canvas: the gold in the panel's right half (the knob, split into
    pieces by its own lines, is in the left half; on the taller cabinet the rim is the biggest gold piece of all)."""
    r, g, b, al = (a[..., i] for i in range(4))
    gold = (al > 200) & (r > 200) & (g > 130) & (g < 200) & (b < 110)
    box = np.zeros(gold.shape, bool)
    x0, y0, x1, y1 = PANEL
    box[y0:y1, x0:x1 - 6] = True  # the panel, short of the pink stripe
    m = np.zeros(gold.shape, bool)
    for p in pieces(gold & box):
        if p.sum() > 20 and np.nonzero(p)[1].min() > (x0 + x1) // 2:
            m |= p
    if not m.any():
        return None
    ys, xs = np.nonzero(m)
    return xs.max() - xs.min() + 1, ys.max() - ys.min() + 1


tries = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 2
best = None
for t in range(tries):
    out = OUT / f"try{t + 1}.png"
    out.unlink(missing_ok=True)
    subprocess.run([str(PKG / "tools/codex.sh"), "-C", str(OUT), "-i", str(SRC), "-o", str(out.with_suffix(".log.txt"))],
                   input=PROMPT.format(out=out), text=True, capture_output=True, timeout=900)
    if not out.exists():
        print(f"try {t + 1}: codex gave no image")
        continue
    pic = Image.open(out).convert("RGB").resize(Image.open(BASE).size, Image.LANCZOS)
    size = bay(key(pic))
    if size is None:
        print(f"try {t + 1}: no bay found")
        continue
    # the rim is about 3 px each side: its outside should be the battery plus 4-10 px
    dw, dh = size[0] - BATTERY[0], size[1] - BATTERY[1]
    ok = 4 <= dw <= 11 and 4 <= dh <= 11
    print(f"try {t + 1}: the bay's rim is {size[0]} x {size[1]} px, the battery {BATTERY[0]} x {BATTERY[1]}: {'fits' if ok else 'off'}")
    miss = abs(dw - 6) + abs(dh - 6)
    if best is None or miss < best[0]:
        best = (miss, out)
    if ok:
        break
if best is None:
    sys.exit("no bay")
x0, y0, x1, y1 = PASTE
subprocess.run([sys.executable, "-I", str(PKG / "tools/patch-box.py"), str(BASE), str(best[1]), str(OUT / "patched-raw.png"),
                f"{x0},{y0},{x1},{y1}", "3"], check=True)
print(f"pasted the panel of {best[1].name}")
