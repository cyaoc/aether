"""Her poses at the gacha cabinet, drawn with the cabinet in the picture (as sheet 16 drew her at the chest).

python scene.py <out dir> [--tries 3]   (run from docs/design/desktop-pet)

The canvas is 3 x 2 cells of 512 on flat green: cell 1 holds her everyday standing drawing (the anchor), cells 2-4 each
hold the cabinet where it stands beside her on the page (its left edge GAP right of her side, on her ground line), at
her 360 px scale. Codex draws her at each cabinet: pressing the battery into the bay, turning the knob, squatting to take
the capsule out. Each scene is measured like a pose sheet's cell: her face against the anchor's (the eyes found
together), and the cabinet must still be where it was pasted. A scene that is off is drawn again, keeping the ones that
passed; each keeps its best try.

Out: scene.png (the canvas codex drew on, keyed), scene-check.png (each scene on grey with its gate).
"""
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, str(Path("tools").resolve()))
from petlib import FACE_MATCH, PKG, bbox, eye_ref, find_face, key, standing

CELL, GROUND = 512, 471
HER_X = 196  # her centre in a scene cell, so that she and the cabinet both fit
# where she stands to use the cabinet, by scene: her centre this far left of its left edge (negative: right of it), so her
# arms reach at their own length with the elbow bent ("手臂太长了"). She stands in front of it, hiding part of it
# ("你可以让她遮住扭蛋机啊"); the cabinet is taller than her ("把扭蛋机画更大点"), so its sign stays above her head:
# before the bay (at its right, her chest), before the knob (at its left), squatting before the outlet (its middle).
HER_OVER = {1: -113, 2: -22, 3: -77}
CABINET = Image.open(PKG / "wip-gacha/bay-fit-big/gacha-marked.png")  # taller than her (2026-11-24)
GATE = {"face": 0.06, "cabinet": 6, "feet": 6, "shoes": 16}  # shoes: the cabinet's outline leaves ~10 px of noise  # face size off by at most 6%; the cabinet moved, her feet off the
# ground line (codex drew her nearer the viewer, lower, where the page has one ground line) by at most 6 px
SCENES = {1: "press", 2: "turn", 3: "take"}  # cell index: name
NAMES = ["top-left", "top-middle", "top-right", "bottom-left", "bottom-middle", "bottom-right"]
BASE = (PKG / "wip-gacha/scene-base.txt").read_text().strip()
CELLS = (PKG / "wip-gacha/scene-cells.txt").read_text().strip()
NEG = (PKG / "wip-gacha/scene-negative.txt").read_text().strip()
REFS = [PKG.parent / "mascot/textures/chibi-sheet.webp", PKG.parent / "mascot/textures/chibi-turnaround.webp",
        PKG / "wip-gacha/battery-fit/battery.png"]
NEW = ("Use your built-in image generation tool (not code) to edit the LAST attached image into ONE new image, then copy "
       "the generated PNG file to {out}. Do not write any code. {body}")
REDO = ("Use your built-in image generation tool (not code) to edit the LAST attached image into ONE new image, then copy "
        "the generated PNG file to {out}. Do not write any code. Draw her ONLY at the cabinet(s) in the {cells} cell(s), "
        "and keep every other cell exactly as it is. {body}")


def cell_xy(i):
    return (i % 3) * CELL, (i // 3) * CELL


def canvas(A, keep):
    """The anchor in cell 1, a cabinet in each scene cell, and the scenes that passed (keep: {i: cell}) as they are."""
    im = Image.new("RGBA", (3 * CELL, 2 * CELL), (0, 255, 0, 255))
    im.alpha_composite(Image.fromarray(A), ((CELL - A.shape[1]) // 2, GROUND - A.shape[0]))
    for i in SCENES:
        x, y = cell_xy(i)
        if i in keep:
            im.alpha_composite(Image.fromarray(keep[i]), (x, y))
        else:  # the cabinet, and her standing where she is to be drawn, over its left side (no stretched arms)
            im.alpha_composite(CABINET, (x + cabinet_x(A), y + GROUND - CABINET.height))
            im.alpha_composite(Image.fromarray(A), (x + cabinet_x(A) - HER_OVER[i] - A.shape[1] // 2, y + GROUND - A.shape[0]))
    return im.convert("RGB")


def cabinet_x(A):
    """Where the cabinet's left edge is in a scene cell: as far right as it fits, 10 px from the cell's edge (it is wider
    than she is now)."""
    return CELL - CABINET.width - 10


def shoes_x(c, cx=None):
    """The middle of her brown loafers along x, or None. cx: the cabinet's left edge in the cell, whose own brown outline
    (and whatever lies within 3 px of it) is left out."""
    a = c.astype(int)
    r, g, b, al = (a[..., k] for k in range(4))
    m = (al > 200) & (r > 60) & (r < 160) & (g > 25) & (g < 95) & (b > 10) & (b < 75) & (r - b > 40)
    m[:a.shape[0] - 60] = False  # only near the ground: her hair is dark too
    if cx is not None:
        k = np.asarray(CABINET).astype(int)
        kr, kg, kb, ka = (k[..., j] for j in range(4))
        line = (ka > 120) & (kr < 170) & (kg < 110)  # the cabinet's dark outline, where it was pasted
        grow = np.zeros(m.shape, bool)
        y0 = GROUND - CABINET.height
        for dy in range(-3, 4):
            for dx in range(-3, 4):
                ys, xs = np.nonzero(line)
                ys, xs = ys + y0 + dy, xs + cx + dx
                ok = (ys >= 0) & (ys < m.shape[0]) & (xs >= 0) & (xs < m.shape[1])
                grow[ys[ok], xs[ok]] = True
        m &= ~grow
    xs = np.nonzero(m)[1]
    return float(xs.mean()) if len(xs) > 30 else None


def measure(c, A, ref, i):
    """{'scale', 'shift', 'fails'}: her face against the anchor's; how far the cabinet's sign moved (it is above her)."""
    g = {"fails": []}
    m, _, _, s = find_face(c, ref)
    g["scale"] = s if m >= FACE_MATCH else None
    if g["scale"] is not None and abs(s - 1) > GATE["face"]:  # eyes not found (turned three-quarter): looked at by eye
        g["fails"].append("face size")
    # her shoes, where she was pasted: codex moved her off to the cabinet's side and stretched her arms twice
    sx = shoes_x(c, cabinet_x(A))
    g["shoes"] = None if sx is None else sx - (cabinet_x(A) - HER_OVER[i] - A.shape[1] // 2 + shoes_x(A))
    if g["shoes"] is None or abs(g["shoes"]) > GATE["shoes"]:
        g["fails"].append("moved off her spot")
    g["feet"] = int(bbox(c)[3]) - GROUND  # the lowest thing in the cell: the cabinet's plinth, or her shoes if lower
    if g["feet"] > GATE["feet"]:
        g["fails"].append("feet below the ground")
    # the cabinet: the right end of its gold plinth, which her feet never reach, still where it was pasted
    cx = cabinet_x(A)
    rows = c[GROUND - 10:GROUND]
    cols = np.nonzero((rows[..., 3] > 120).any(0))[0]
    want = np.nonzero((np.asarray(CABINET)[-10:, :, 3] > 120).any(0))[0]
    if not len(cols):
        g["shift"] = None
        g["fails"].append("cabinet gone")
        return g
    g["shift"] = float(abs(cols.max() - (cx + want.max())))
    if g["shift"] > GATE["cabinet"]:
        g["fails"].append("cabinet moved")
    return g


def run(prompt, src, out):
    out.unlink(missing_ok=True)
    cmd = [str(PKG / "tools/codex.sh"), "-C", str(out.parent)]
    for r in REFS + [src]:
        cmd += ["-i", str(r)]
    subprocess.run(cmd + ["-o", str(out.with_suffix(".log.txt"))], input=prompt, text=True, capture_output=True, timeout=900)
    if not out.exists():
        return None
    im = Image.open(out).convert("RGB").resize((3 * CELL, 2 * CELL), Image.LANCZOS)
    a = key(im)
    return {i: a[cell_xy(i)[1]:cell_xy(i)[1] + CELL, cell_xy(i)[0]:cell_xy(i)[0] + CELL].round().astype(np.uint8) for i in SCENES}


def main():
    argv = sys.argv[1:]
    tries = 3
    if "--tries" in argv:
        k = argv.index("--tries")
        tries = int(argv[k + 1])
        del argv[k:k + 2]
    out = Path(argv[0]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    A = standing()
    ref = eye_ref(A)
    body = f"{BASE} {CELLS} {NEG}"
    best = {}
    todo = list(SCENES)
    for t in range(tries):
        if not todo:
            break
        src = out / f"canvas-{t + 1}.png"
        canvas(A, {i: c for i, (_, c) in best.items() if i not in todo}).save(src)
        png = out / f"try{t + 1}.png"
        prompt = NEW.format(out=png, body=body) if t == 0 else REDO.format(out=png, cells=", ".join(NAMES[i] for i in todo), body=body)
        got = run(prompt, src, png)
        if got is None:
            print(f"try {t + 1}: codex gave no image")
            continue
        for i in todo:
            g = measure(got[i], A, ref, i)
            if i not in best or (len(g["fails"]), abs((g["scale"] or 0) - 1)) < (len(best[i][0]["fails"]), abs((best[i][0]["scale"] or 0) - 1)):
                best[i] = (g, got[i])
        todo = [i for i in SCENES if best[i][0]["fails"]]
        print(f"try {t + 1}: " + "; ".join(f"{SCENES[i]} {' '.join(best[i][0]['fails']) or 'ok'}" for i in SCENES))
    if not best:
        sys.exit("no scene")
    sheet = np.asarray(canvas(A, {i: c for i, (_, c) in best.items()}).convert("RGBA")).copy()
    sheet = key(Image.fromarray(sheet[..., :3])).round().astype(np.uint8)
    Image.fromarray(sheet).save(out / "scene.png")
    check = Image.new("RGBA", (3 * CELL, 2 * CELL), (96, 100, 112, 255))
    check.alpha_composite(Image.fromarray(sheet))
    d = ImageDraw.Draw(check)
    font = ImageFont.truetype("/System/Library/Fonts/Hiragino Sans GB.ttc", 20)
    for i, (g, _) in best.items():
        x, y = cell_xy(i)
        d.rectangle([x + 2, y + 2, x + CELL - 3, y + CELL - 3], outline=(255, 80, 80) if g["fails"] else (80, 220, 120), width=3)
        sc = f"face x{g['scale']:.2f}" if g["scale"] else "eyes not found: look by eye"
        sh = f"cabinet {g['shift']:.0f} px" if g.get("shift") is not None else ""
        d.text((x + 8, y + 6), f"{i + 1} {SCENES[i]}  {sc}  {sh}", font=font, fill="white")
    check.convert("RGB").save(out / "scene-check.png")
    failed = [SCENES[i] for i in SCENES if best[i][0]["fails"]]
    print("scenes: " + ("all pass" if not failed else "still off: " + ", ".join(failed)))


if __name__ == "__main__":
    main()
