"""Make one pose sheet with her size fixed while it is drawn.

python gen-sheet.py <sheet number> <out dir> [--tries 3] [--redo 5,6]

Codex gets the sheet's canvas: 1536 x 1024 flat green, 3 x 2 cells of 512, and cell 1 already holding her everyday
standing drawing (the anchor), ANCHOR_H px tall with her feet on GROUND: the same drawing at the same size on every
sheet, so every sheet comes out at one scale and the board needs no scale of its own per sheet. Codex draws cells 2-6
beside it, copying each pose from the approved earlier sheet (handed in just before the canvas) and every size from the
anchor. Codex's whole image is only brought back to 1536 x 1024; nothing in it is scaled on its own.

Each cell is measured against the anchor (GATE): the size of her face (the anchor's two eyes found together, as one
template); where she stands upright, how high her eyes are above her feet. Every drawing is then slid down or up so its lowest point
rests on the ground line (codex puts a row's feet a little off); that is a move, never a resize.
A cell that is off is drawn again: the cells that passed stay, the failing ones get the anchor pasted in and codex turns
it into the pose there (drawn from an empty cell, a head under a hat came out 8% small), up to --tries times; each cell
keeps its best try. Cells whose eyes cannot be found (closed, turned away, behind
her hands) are listed to be looked at by eye. A sheet already in the out dir is picked up again: its passing cells stay,
and --redo draws the cells listed again too (wrong by eye: a plaster on the wrong knee).

Out: sheet-<n>.png (transparent, cell 1 the anchor exactly) and sheet-<n>-check.png (each cell on grey, its gate).
"""
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, str(Path(__file__).resolve().parent))  # petlib sits beside the tools, also under python -I
from petlib import FACE_MATCH, PKG, POSE, SAME_SPOT, SHEET, bbox, carry_props, drop_strays, eye_ref, find_face, key, standing

T, MASCOT = PKG / "tools", PKG.parent / "mascot/textures"
CELL, GROUND = 512, 471  # cell side; the anchor's ground line inside its cell
GATE = {"face": 0.06, "eyeline": 0.05}
# sheet number: board sheet, base prompt, outfit design sheet
SHEETS = {1: ("common", "pet-base", None), 2: ("lottery", "pet-base", None), 3: ("daily1", "pet-base", None),
          5: ("moods", "pet-base", None), 6: ("gifts", "pet-base", None),
          7: ("castleSheet", "princess-pose-base", "princess-sheet"), 8: ("voyage", "fleet-pose-base", "fleet-sheet"),
          9: ("ski9", "ski-pose-base", "ski-sheet"), 10: ("gym10", "outfit-pose-base", "gym-sheet"),
          11: ("hike11", "outfit-pose-base", "hike-sheet"), 12: ("cycle12", "outfit-pose-base", "cycle-sheet")}
CASTLE = [None, "pIdle", "spin", "curtsy", "walkA", "lookBack"]  # the castle's princess idle moves to cell 2
# upright on the ground line: her eyes are as high above her feet as the anchor's
UPRIGHT = {"wave", "cheer", "surprised", "announce", "point", "hope", "clap", "crack", "pout", "shy", "hum", "giftBox",
           "shakeBox", "crownV", "pIdle", "capSalute", "spyglass", "admSalute", "govPoint", "govWave", "gymReady", "curl",
           "gymWipe", "hikeReady", "hikeUp", "bikeReady", "bikeUp", "skiShake"}
NAMES = ["top-left", "top-middle", "top-right", "bottom-left", "bottom-middle", "bottom-right"]
NEW = ("Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to {out}. "
       "Do not write any code. The LAST attached image is the canvas to fill; the image just before it is the approved "
       "earlier version of this pose sheet: copy each pose, outfit, prop and expression from it, by name, but take every "
       "size only from the canvas. Image: {body}")
REDO = ("Use your built-in image generation tool (not code) to edit the LAST attached image into ONE new image, then copy "
        "the generated PNG file to {out}. Do not write any code. It is a 3 by 2 pose sheet on flat #00FF00 green. In the "
        "{cells} cell(s) she stands in her everyday standing pose, exactly as in the top-left cell: turn her there into the "
        "pose, outfit and expression described below for that cell, keeping her head and face exactly as big as they are "
        "now (a hat sits on top of her head; the head never gets smaller for it), her body and legs as long, her shoe soles "
        "on the same line. Change ONLY those cells and keep every other cell exactly as it is. The image just before it is "
        "the approved earlier version of this sheet: copy the poses from it, never its sizes. Image: {body}")


def canvas_cell(i):
    return (i % 3) * CELL, (i // 3) * CELL


def place_anchor(sheet, A):
    """The anchor into cell 1, centred, feet on GROUND (sheet: RGBA uint8 array)."""
    sheet[:CELL, :CELL] = 0
    x, y = (CELL - A.shape[1]) // 2, GROUND - A.shape[0]
    sheet[y:y + A.shape[0], x:x + A.shape[1]] = A
    return sheet


def on_green(sheet):
    im = Image.new("RGBA", (3 * CELL, 2 * CELL), (0, 255, 0, 255))
    im.alpha_composite(Image.fromarray(sheet))
    return im.convert("RGB")


def run_codex(prompt, refs, out, log):
    out.unlink(missing_ok=True)
    cmd = ["codex", "exec", "--skip-git-repo-check", "-s", "workspace-write", "-C", str(out.parent)]
    for r in refs:
        cmd += ["-i", str(r)]
    subprocess.run(cmd + ["-o", str(log)], input=prompt, text=True, capture_output=True, timeout=900)
    if not out.exists():
        return None
    im = Image.open(out).convert("RGB")
    if im.size != (3 * CELL, 2 * CELL):  # codex's own canvas size, brought back as a whole
        im = im.resize((3 * CELL, 2 * CELL), Image.LANCZOS)
    return [to_ground(drop_strays(key(im.crop((x, y, x + CELL, y + CELL)))).round().astype(np.uint8))
            for x, y in map(canvas_cell, range(6))]


def to_ground(c):
    """The drawing slid up or down so its lowest point rests on GROUND (codex puts a row's feet a little off)."""
    if not (c[..., 3] > 40).any():
        return c
    dy = GROUND - bbox(c)[3]
    out = np.zeros_like(c)
    if dy >= 0:
        out[dy:] = c[:CELL - dy]
    else:
        out[:dy] = c[-dy:]
    return out


def measure(c, ref, name):
    """A cell's gate against the anchor: {'eyes': found?, 'scale', 'eyeline', 'fails': [...]}. Eyes closed, turned away or
    behind her hands match badly and are left to be looked at by eye."""
    m, _, y, s = find_face(c, ref)
    g = {"match": m, "fails": [], "eyes": m >= FACE_MATCH}
    if not g["eyes"]:
        return g
    g["scale"] = s
    if abs(s - 1) > GATE["face"]:
        g["fails"].append("face size")
    if name in UPRIGHT:
        g["eyeline"] = (bbox(c)[3] - y) / ref["eyeline"]
        if abs(g["eyeline"] - 1) > GATE["eyeline"]:
            g["fails"].append("eye height")
    return g


def score(g):
    """Lower is better: failed checks first, then how far off."""
    return (len(g["fails"]), sum(abs(g.get(k, 1) - 1) for k in ("scale", "eyeline")))


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
        redo = [int(c) - 1 for c in argv[k + 1].split(",")]
        del argv[k:k + 2]
    n, out = int(argv[0]), Path(argv[1]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    key_, base, outfit = SHEETS[n]
    names = CASTLE if key_ == "castleSheet" else [next((p for p, (k, i) in POSE.items() if k == key_ and i == j), None) for j in range(6)]
    A = standing()
    ref = eye_ref(A)

    body = " ".join((T / f).read_text().strip() for f in (f"{base}.txt", f"sheet-{n}.txt", "pet-negative.txt"))
    refs = [MASCOT / "chibi-sheet.webp", MASCOT / "chibi-turnaround.webp"]
    if outfit:
        refs.append(PKG / f"textures/{outfit}.webp")
    refs.append(PKG / f"textures/{SHEET[key_]}.webp")
    canvas = place_anchor(np.zeros((2 * CELL, 3 * CELL, 4), np.uint8), A)
    on_green(canvas).save(out / f"sheet-{n}-canvas.png")

    best = {}  # cell index: (gate, drawing)
    todo = [i for i in range(1, 6)]
    done = out / f"sheet-{n}.png"
    if done.exists():  # an earlier run: its cells are measured again, and only the failing ones drawn again
        old = np.asarray(Image.open(done).convert("RGBA"))
        for i in todo:
            x, y = canvas_cell(i)
            c = old[y:y + CELL, x:x + CELL].copy()
            best[i] = (measure(c, ref, names[i]) if names[i] else {"eyes": False, "fails": [], "match": 0}, c)
        todo = [i for i in range(1, 6) if names[i] and best[i][0]["fails"] or i in redo]
        for i in redo:
            best[i] = ({"eyes": False, "fails": ["redo"], "match": 0}, best[i][1])
        print("kept: " + "; ".join(f"{i + 1} {names[i]} {' '.join(best[i][0]['fails']) or 'ok'}" for i in range(1, 6) if names[i]))
    for t in range(tries):
        if not todo:
            break
        png, log = out / f"sheet-{n}-try{t + 1}.png", out / f"sheet-{n}-try{t + 1}.log.txt"
        if t == 0 and not best:
            got = run_codex(NEW.format(out=png, body=body), refs + [out / f"sheet-{n}-canvas.png"], png, log)
        else:
            sheet = place_anchor(np.zeros((2 * CELL, 3 * CELL, 4), np.uint8), A)
            for i, (_, c) in best.items():
                x, y = canvas_cell(i)
                # a failing cell starts again from the anchor itself: codex keeps a head it edits at the size it is,
                # where it drew a head under a hat 8% small from an empty cell
                sheet[y:y + CELL, x:x + CELL] = place_anchor(np.zeros((CELL, CELL, 4), np.uint8), A) if i in todo else c
            on_green(sheet).save(out / f"sheet-{n}-redo{t + 1}.png")
            cells_ = ", ".join(NAMES[i] for i in todo)
            got = run_codex(REDO.format(out=png, cells=cells_, body=body), refs + [out / f"sheet-{n}-redo{t + 1}.png"], png, log)
        if got is None:
            print(f"try {t + 1}: codex gave no image")
            continue
        for i in todo:
            gi = measure(got[i], ref, names[i]) if names[i] else {"eyes": False, "fails": [], "match": 0}
            if i not in best or score(gi) < score(best[i][0]):
                best[i] = (gi, got[i])
        todo = [i for i in range(1, 6) if names[i] and best[i][0]["fails"]]
        print(f"try {t + 1}: " + "; ".join(f"{i + 1} {names[i]} {' '.join(best[i][0]['fails']) or 'ok'}" for i in range(1, 6) if names[i]))

    # what she drops stays where it lands: a pose in the board's SAME_SPOT gets the things on the ground of its earlier pose,
    # pixel for pixel (petlib.carry_props), so a dumbbell, the skis, the poles or the fallen bike do not move between poses
    for p, q in SAME_SPOT.items():
        if POSE[p][0] == key_:
            i, j = POSE[p][1], POSE[q][1]
            best[i] = (best[i][0], carry_props(best[j][1], best[i][1]).round().astype(np.uint8))
    sheet = place_anchor(np.zeros((2 * CELL, 3 * CELL, 4), np.uint8), A)
    for i, (_, c) in best.items():
        x, y = canvas_cell(i)
        sheet[y:y + CELL, x:x + CELL] = c
    Image.fromarray(sheet).save(out / f"sheet-{n}.png")

    check = Image.new("RGBA", sheet.shape[1::-1], (96, 100, 112, 255))
    check.alpha_composite(Image.fromarray(sheet))
    d = ImageDraw.Draw(check)
    font = ImageFont.truetype("/System/Library/Fonts/Hiragino Sans GB.ttc", 20)
    for i in range(6):
        x, y = canvas_cell(i)
        d.line([(x, y + GROUND), (x + CELL, y + GROUND)], fill=(255, 255, 255, 90))  # every drawing rests on it
        if i == 0:
            d.text((x + 8, y + 6), "1 anchor", font=font, fill="white")
            continue
        gi = best.get(i, ({"eyes": False, "fails": []},))[0]
        ok = not gi["fails"]
        d.rectangle([x + 2, y + 2, x + CELL - 3, y + CELL - 3], outline=(80, 220, 120) if ok and gi["eyes"] else (255, 200, 60) if ok else (255, 80, 80), width=3)
        text = f"{i + 1} {names[i] or '(unused)'}"
        if gi["eyes"]:
            text += f"  face x{gi['scale']:.2f}" + (f"  eye line x{gi['eyeline']:.2f}" if "eyeline" in gi else "")
        elif names[i]:
            text += "  eyes not found: look by eye"
        d.text((x + 8, y + 6), text, font=font, fill="white")
    check.convert("RGB").save(out / f"sheet-{n}-check.png")
    unmeasured = [names[i] for i in range(1, 6) if names[i] and not best[i][0]["eyes"]]
    failed = [names[i] for i in range(1, 6) if names[i] and best[i][0]["fails"]]
    print(f"sheet {n}: {'all measured cells pass' if not failed else 'still off: ' + ', '.join(failed)}"
          + (f"; look by eye: {', '.join(unmeasured)}" if unmeasured else ""))


if __name__ == "__main__":
    main()
