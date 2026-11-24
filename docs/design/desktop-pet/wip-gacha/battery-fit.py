"""Draw the battery beside the cabinet, sized by its bay: codex draws a small prop at about 0.14 of her height whatever it is
asked (three tries 0.14-0.16 for 0.10-0.125), so the bay it must fit in is put in the picture as the ruler.

python battery-fit.py <tries>  (in wip-gacha; reads gacha-a2/patched-raw.png, writes battery-fit/)
"""
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "tools"))
from petlib import PKG, bbox, key, pieces

HERE = Path(__file__).resolve().parent
OUT = HERE / "battery-fit"
OUT.mkdir(exist_ok=True)
CANVAS = HERE / "gacha-a2/patched-raw.png"
BAY_H = 44  # the bay's gold frame, nub included, at her 360 px
WHAT = (PKG / "tools/props/battery.txt").read_text().strip()
PROMPT = ("Use your built-in image generation tool (not code) to edit the attached image into ONE new image, then copy the "
          "generated PNG file to {out}. Do not write any code. The attached image shows a chibi girl and a capsule-toy "
          "cabinet on flat #00FF00 green: keep both exactly as they are, the same size, in the same place, unchanged, and "
          "keep everything you do not draw the same flat green, with no green on anything you draw. On the cabinet's panel, "
          "right of the big gold knob, there is an empty battery-shaped bay with a thin gold rim. Draw, to the RIGHT of the "
          "cabinet, floating level with that bay and not touching the cabinet, ONE battery that fits that bay exactly: the "
          "SAME height and width as the bay's gold rim, nub included, so that it could be pressed straight into it. "
          + WHAT.split(":", 1)[1].split("Only the battery")[0].strip().replace("a plump", "A plump", 1)
          + " Only the battery: no motion lines, no sparkles, no glow, no shadow. RENDERING: the rendering of the cabinet "
          "in the picture. DO NOT DRAW: text, letters, numbers, a second battery, anything else.")

tries = int(sys.argv[1]) if len(sys.argv) > 1 else 3
best = None
for t in range(tries):
    out = OUT / f"try{t + 1}.png"
    out.unlink(missing_ok=True)
    subprocess.run([str(PKG / "tools/codex.sh"), "-C", str(OUT), "-i", str(CANVAS), "-o", str(out.with_suffix(".log.txt"))],
                   input=PROMPT.format(out=out), text=True, capture_output=True, timeout=900)
    if not out.exists():
        print(f"try {t + 1}: codex gave no image")
        continue
    pic = Image.open(out).convert("RGB").resize(Image.open(CANVAS).size, Image.LANCZOS)
    a = key(pic)
    keep = np.zeros(a.shape[:2], bool)
    for p in pieces(a[..., 3] > 40):  # the battery: whatever lies right of the cabinet (it ends at x 533)
        ys, xs = np.nonzero(p)
        if p.sum() > 100 and xs.min() > 540:
            keep |= p
    if not keep.any():
        print(f"try {t + 1}: no battery right of the cabinet")
        continue
    ys = np.nonzero(keep.any(1))[0]
    h = ys.max() - ys.min() + 1
    print(f"try {t + 1}: the battery is {h} px tall, the bay {BAY_H}")
    miss = abs(h - BAY_H)
    if best is None or miss < best[0]:
        prop = a.copy()
        prop[..., 3] = np.where(keep, prop[..., 3], 0)
        best = (miss, prop)
    if miss <= 3:
        break
if best is None:
    sys.exit("no battery")
x0, y0, x1, y1 = bbox(best[1])
Image.fromarray(best[1][y0:y1, x0:x1].round().astype(np.uint8)).save(OUT / "battery.png")
print(f"battery: {x1 - x0} x {y1 - y0} px")
