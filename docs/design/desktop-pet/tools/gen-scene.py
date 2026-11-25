"""Her poses drawn with a prop in the picture: where her hands land on it, or how it hides her, only comes out right when
codex draws her there (sheet 21 of the 扭蛋 daily, drawn without the cabinet, had her hands at the wrong height).

python tools/gen-scene.py <scene> <out dir> [--tries 3] [--redo hBlow,...]   (run from docs/design/desktop-pet)

The canvas is 3 x 2 cells of 512 on flat green: cell 1 holds her everyday standing drawing (the anchor); each scene cell
holds its prop where it stands by her on the page, at her 360 px scale, and her standing drawing where she is to be
drawn: in front of the prop (the 扭蛋 cabinet, the sofa) or behind it (the hot-pot table hides her legs). Codex turns her
there into the cell's pose. Each scene is measured: her face against the anchor's (the eyes found together); the prop
still where it was pasted (the ends of its lowest rows that she never reaches); nothing below the prop's ground line;
and, where she stays on her feet in front of it, her shoes where they were. A scene that is off is drawn again, keeping
the ones that passed; each keeps its best try. A scene.png already in the out dir is picked up again: its passing
scenes stay, and --redo draws the poses listed again too (wrong by eye: a bored face where she should blow on the food).

Out: scene.png (the canvas codex drew on, keyed), scene-check.png (each scene on grey with its gate).
"""
import subprocess
import sys
from pathlib import Path
from typing import NamedTuple, Optional
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, str(Path(__file__).resolve().parent))  # petlib sits beside the tools, also under python -I
from petlib import FACE_MATCH, PKG, bbox, eye_ref, find_face, key, standing

CELL, GROUND = 512, 471  # the prop's bottom rests on GROUND in every cell
GATE = {"face": 0.06, "prop": 6, "feet": 6, "shoes": 16}  # shoes: the cabinet's outline leaves ~10 px of noise
NAMES = ["top-left", "top-middle", "top-right", "bottom-left", "bottom-middle", "bottom-right"]
MASCOT = PKG.parent / "mascot/textures"


class Cell(NamedTuple):
    pose: str
    prop: Image.Image
    left: int  # the prop's left edge in the cell
    her: int  # her centre x in the cell, where she is to be drawn
    drop: int  # how far below her ground line the prop's bottom stands (the table stands nearer the viewer than she kneels)
    front: bool  # she is pasted in front of the prop; else behind it, and it hides her legs
    shoes: bool  # she stays on her feet there: her shoes must not move (codex moved her off the cabinet, arms stretched)
    ends: tuple  # the ends of the prop's lowest rows she never reaches: "left", "right"
    lip: Optional[int] = None  # lying on it: the row of the prop (its px) whose front lip hides her below it (cut-scene.py)


# 扭蛋 (#403): the cabinet, taller than her (2026-11-24), as far right as it fits; she stands in front of it, hiding part of
# it ("你可以让她遮住扭蛋机啊"), her centre this far right of its left edge so her arms reach at their own length with the
# elbow bent ("手臂太长了"): before the bay (at its right, her chest), before the knob (at its left), squatting before the
# outlet (its middle). The sign stays above her head ("把扭蛋机画更大点").
CABINET = Image.open(PKG / "wip-gacha/bay-fit-big/gacha-marked.png")
_CAB = CELL - CABINET.width - 10
# 火锅、手游 (#411): the table centred, she kneels behind its pot (left of the table's middle as codex drew it, approved
# 2026-11-25), the table's bottom 39 px nearer the viewer than her ground as in its picture with her; the sofa centred,
# she stands before its middle to lie down on it. Lying on the sofa, the front of its seat (its gold rim and the cream frame
# under it, SOFA_LIP down) is in front of her: nothing of her shows below it
TABLE = Image.open(PKG / "wip-hotpot/props/hotpot.png")
SOFA = Image.open(PKG / "wip-mobile/props/sofa.png")
_TAB, _SOFA = (CELL - TABLE.width) // 2, (CELL - SOFA.width) // 2
_POT = _TAB + 202  # the pot's middle, 202 px from the table's left edge


def _cab(pose, over):
    return Cell(pose, CABINET, _CAB, _CAB + over, 0, True, True, ("right",))


def _table(pose):
    return Cell(pose, TABLE, _TAB, _POT, 39, False, False, ("left", "right"))


SOFA_LIP = 144  # the row of the sofa's picture where its seat's front (gold rim, row 146) begins


def _sofa(pose, lying=True):
    return Cell(pose, SOFA, _SOFA, _SOFA + SOFA.width // 2, 1, True, False, ("left", "right"), SOFA_LIP if lying else None)


# a scene: its prompt files in tools/scenes, the pictures after who she is, and its cells by index
SCENES = {
    "gacha": ("gacha", [PKG / "wip-gacha/battery-fit/battery.png"],
              {1: _cab("press", 113), 2: _cab("turn", 22), 3: _cab("take", 77)}),
    "hotpot": ("home", [PKG / "wip-hotpot/props/bowl.png"],
               {i + 1: _table(p) for i, p in enumerate(["hPray", "hSit", "hPickClear", "hPickRed", "hBlow"])}),
    "hotpot2": ("home", [PKG / "wip-hotpot/props/bowl.png", PKG / "wip-hotpot/props/soymilk.png"],
                {**{i + 1: _table(p) for i, p in enumerate(["hYum", "hFeed", "hSpicy", "hDrink"])}, 5: _sofa("mSit", lying=False)}),
    "sofa": ("home", [], {i + 1: _sofa(p) for i, p in enumerate(["mPlay", "mWin", "mStunned", "mSmack", "mOuch"])}),
}
NEW = ("Use your built-in image generation tool (not code) to edit the LAST attached image into ONE new image, then copy "
       "the generated PNG file to {out}. Do not write any code. {body}")
REDO = ("Use your built-in image generation tool (not code) to edit the LAST attached image into ONE new image, then copy "
        "the generated PNG file to {out}. Do not write any code. Draw her ONLY in the {cells} cell(s), and keep every "
        "other cell exactly as it is. {body}")


def cell_xy(i):
    return (i % 3) * CELL, (i // 3) * CELL


def her_x(A, c):
    """Where her standing drawing is pasted in a scene cell (its left edge)."""
    return c.her - A.shape[1] // 2


def canvas(A, cells, keep):
    """The anchor in cell 1, each scene cell's prop and her, and the scenes that passed (keep: {i: cell}) as they are."""
    im = Image.new("RGBA", (3 * CELL, 2 * CELL), (0, 255, 0, 255))
    im.alpha_composite(Image.fromarray(A), ((CELL - A.shape[1]) // 2, GROUND - A.shape[0]))
    for i, c in cells.items():
        x, y = cell_xy(i)
        if i in keep:
            im.alpha_composite(Image.fromarray(keep[i]), (x, y))
            continue
        her = (Image.fromarray(A), (x + her_x(A, c), y + GROUND - c.drop - A.shape[0]))
        prop = (c.prop, (x + c.left, y + GROUND - c.prop.height))
        for layer in ([prop, her] if c.front else [her, prop]):
            im.alpha_composite(*layer)
    return im.convert("RGB")


def shoes_x(a, c=None):
    """The middle of her brown loafers along x, or None. c: the cell, whose prop's own brown outline (and whatever lies
    within 3 px of it) is left out."""
    a = a.astype(int)
    r, g, b, al = (a[..., k] for k in range(4))
    m = (al > 200) & (r > 60) & (r < 160) & (g > 25) & (g < 95) & (b > 10) & (b < 75) & (r - b > 40)
    m[:a.shape[0] - 60] = False  # only near the ground: her hair is dark too
    if c is not None:
        k = np.asarray(c.prop).astype(int)
        kr, kg, ka = k[..., 0], k[..., 1], k[..., 3]
        ys, xs = np.nonzero((ka > 120) & (kr < 170) & (kg < 110))  # the prop's dark outline, where it was pasted
        ys, xs = ys + GROUND - c.prop.height, xs + c.left
        grow = np.zeros(m.shape, bool)
        for dy in range(-3, 4):
            for dx in range(-3, 4):
                ok = (ys + dy >= 0) & (ys + dy < m.shape[0]) & (xs + dx >= 0) & (xs + dx < m.shape[1])
                grow[ys[ok] + dy, xs[ok] + dx] = True
        m &= ~grow
    xs = np.nonzero(m)[1]
    return float(xs.mean()) if len(xs) > 30 else None


def measure(a, A, ref, c):
    """{'scale', 'shift', 'fails'}: her face against the anchor's; how far the prop's lowest rows moved."""
    g = {"fails": []}
    m, _, _, s = find_face(a, ref)
    g["scale"] = s if m >= FACE_MATCH else None
    if g["scale"] is not None and abs(s - 1) > GATE["face"]:  # eyes not found (closed, turned away): looked at by eye
        g["fails"].append("face size")
    if c.shoes:
        sx = shoes_x(a, c)
        g["shoes"] = None if sx is None else sx - (her_x(A, c) + shoes_x(A))
        if g["shoes"] is None or abs(g["shoes"]) > GATE["shoes"]:
            g["fails"].append("moved off her spot")
    if int(bbox(a)[3]) - GROUND > GATE["feet"]:  # the lowest thing in the cell: the prop's bottom, or her if lower
        g["fails"].append("feet below the ground")
    cols = np.nonzero((a[GROUND - 10:GROUND, :, 3] > 120).any(0))[0]
    want = np.nonzero((np.asarray(c.prop)[-10:, :, 3] > 120).any(0))[0] + c.left
    if not len(cols):
        g["shift"] = None
        g["fails"].append("prop gone")
        return g
    g["shift"] = float(max(abs(cols.min() - want.min()) if e == "left" else abs(cols.max() - want.max()) for e in c.ends))
    if g["shift"] > GATE["prop"]:
        g["fails"].append("prop moved")
    return g


def run(prompt, refs, src, out, cells):
    out.unlink(missing_ok=True)
    cmd = [str(PKG / "tools/codex.sh"), "-C", str(out.parent)]
    for r in refs + [src]:
        cmd += ["-i", str(r)]
    subprocess.run(cmd + ["-o", str(out.with_suffix(".log.txt"))], input=prompt, text=True, capture_output=True, timeout=900)
    if not out.exists():
        return None
    im = Image.open(out).convert("RGB").resize((3 * CELL, 2 * CELL), Image.LANCZOS)
    a = key(im)
    return {i: a[cell_xy(i)[1]:cell_xy(i)[1] + CELL, cell_xy(i)[0]:cell_xy(i)[0] + CELL].round().astype(np.uint8) for i in cells}


def main():
    argv = sys.argv[1:]
    tries = 3
    if "--tries" in argv:
        k = argv.index("--tries")
        tries = int(argv[k + 1])
        del argv[k:k + 2]
    redo = []
    if "--redo" in argv:
        k = argv.index("--redo")
        redo = argv[k + 1].split(",")
        del argv[k:k + 2]
    name, out = argv[0], Path(argv[1]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    stem, props, cells = SCENES[name]
    S = PKG / "tools/scenes"
    body = " ".join((S / f).read_text().strip() for f in (f"{stem}-base.txt", f"{name}-cells.txt", f"{stem}-negative.txt"))
    refs = [MASCOT / "chibi-sheet.webp", MASCOT / "chibi-turnaround.webp"] + props
    A = standing()
    ref = eye_ref(A)
    best = {}
    todo = list(cells)
    done = out / "scene.png"
    if done.exists():  # an earlier run: its scenes are measured again, and only the failing and --redo ones drawn again
        old = np.asarray(Image.open(done).convert("RGBA"))
        for i, c in cells.items():
            x, y = cell_xy(i)
            a = old[y:y + CELL, x:x + CELL].copy()
            best[i] = (measure(a, A, ref, c), a)
            if c.pose in redo:
                best[i][0]["fails"].append("redo")
        todo = [i for i in cells if best[i][0]["fails"]]
        print("kept: " + "; ".join(f"{cells[i].pose} {' '.join(best[i][0]['fails']) or 'ok'}" for i in cells))
    first = not best
    for t in range(tries):
        if not todo:
            break
        n = len(list(out.glob("try*.png"))) + 1  # a later run keeps the earlier runs' tries
        src = out / f"canvas-{n}.png"
        canvas(A, cells, {i: c for i, (_, c) in best.items() if i not in todo}).save(src)
        png = out / f"try{n}.png"
        prompt = NEW.format(out=png, body=body) if t == 0 and first else REDO.format(out=png, cells=", ".join(NAMES[i] for i in todo), body=body)
        got = run(prompt, refs, src, png, cells)
        if got is None:
            print(f"try {t + 1}: codex gave no image")
            continue
        for i in todo:
            g = measure(got[i], A, ref, cells[i])
            if i not in best or (len(g["fails"]), abs((g["scale"] or 0) - 1)) < (len(best[i][0]["fails"]), abs((best[i][0]["scale"] or 0) - 1)):
                best[i] = (g, got[i])
        todo = [i for i in cells if best[i][0]["fails"]]
        print(f"try {t + 1}: " + "; ".join(f"{cells[i].pose} {' '.join(best[i][0]['fails']) or 'ok'}" for i in cells))
    if not best:
        sys.exit("no scene")
    sheet = np.asarray(canvas(A, cells, {i: c for i, (_, c) in best.items()}).convert("RGBA")).copy()
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
        sh = f"prop {g['shift']:.0f} px" if g.get("shift") is not None else ""
        d.text((x + 8, y + 6), f"{i + 1} {cells[i].pose}  {sc}  {sh}", font=font, fill="white")
    check.convert("RGB").save(out / "scene-check.png")
    failed = [cells[i].pose for i in cells if best[i][0]["fails"]]
    print("scenes: " + ("all pass" if not failed else "still off: " + ", ".join(failed)))


if __name__ == "__main__":
    main()
