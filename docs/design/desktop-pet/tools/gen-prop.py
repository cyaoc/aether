"""Draw a prop for the shows with her in the same image at her fixed size, so it comes out at her scale and line weight.

python gen-prop.py <prop> <out dir> [--tries 2]

The canvas is flat green with her everyday standing drawing (petlib.standing, ANCHOR_H px tall) pasted where PROPS
puts her; codex draws the prop around or beside her (props/<prop>.txt), with the castle as the rendering to match and the
earlier version, if any, as reference. Her eyes are then found where she was pasted: if codex redrew her more than GATE
bigger or smaller, the whole picture is off scale and is drawn again; nothing is scaled afterwards. The prop is then taken
out: every piece apart from her. A prop is never drawn around her (codex then shrank her to make the flagship look
small); she stands beside it, and a prop with a size of its own (the flagship, about 2.2 of her heights) has its height
measured against hers and is drawn again when off. The opened gift box is an edit of the closed one's picture, and only the top of
the box is taken from it, so the closed and open boxes line up.

Out: <prop>.png (transparent, the prop alone, at her scale) and <prop>-with-her.png (codex's picture, keyed).
"""
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))  # petlib sits beside the tools, also under python -I
from petlib import FACE_MATCH, PKG, bbox, eye_ref, find_face, key, pieces, standing

CASTLE = PKG / "textures/castle-closed.webp"
GATE = 0.04  # how far codex may redraw her bigger or smaller
# canvas size; her centre x and shoe soles y; references after the castle; the prop's height in her heights (a gate)
PROPS = {"flagship": ((1536, 1024), (170, 990), [], (2.0, 2.45)),
         "emblem": ((1024, 1024), (230, 900), [], None),
         "giftbox": ((1024, 1024), (300, 900), [], None),
         "balloons": ((1024, 1024), (230, 900), [], None),
         "admship": ((1536, 1024), (170, 990), [PKG / "textures/flagship.webp"], (1.5, 1.9))}
DRAW = ("Use your built-in image generation tool (not code) to edit the LAST attached image into ONE new image, then copy "
        "the generated PNG file to {out}. Do not write any code. The last attached image is a canvas of perfectly flat "
        "solid #00FF00 green with a chibi girl already standing in it: keep her exactly as she is, the same size, in the "
        "same place, unchanged, and keep everything you do not draw the same flat green, with no green on anything you "
        "draw. {what} DO NOT DRAW: text, letters, numbers, logos, a watermark, a second girl, a creature, cannons, guns, "
        "weapons, a background, a frame or border, realistic metal or 3D rendering, muddy colors.")
EDIT = ("Use your built-in image generation tool (not code) to edit the attached image into ONE new image, then copy the "
        "generated PNG file to {out}. Do not write any code. {what}")


def codex(prompt, refs, out):
    out.unlink(missing_ok=True)
    cmd = [str(PKG / "tools/codex.sh"), "-C", str(out.parent)]
    for r in refs:
        cmd += ["-i", str(r)]
    subprocess.run(cmd + ["-o", str(out.with_suffix(".log.txt"))], input=prompt, text=True, capture_output=True, timeout=900)
    return Image.open(out).convert("RGB") if out.exists() else None


def main():
    argv = sys.argv[1:]
    tries = 2
    if "--tries" in argv:
        k = argv.index("--tries")
        tries = int(argv[k + 1])
        del argv[k:k + 2]
    name, out = argv[0], Path(argv[1]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    what = (PKG / f"tools/props/{name}.txt").read_text().strip()

    if name == "giftbox-open":  # an edit of the closed box's picture
        closed = Image.open(out / "giftbox.png")
        pic = codex(EDIT.format(out=out / "giftbox-open-raw.png", what=what), [out / "giftbox-raw.png"], out / "giftbox-open-raw.png")
        if pic is None:
            sys.exit("codex gave no image")
        opened = key(pic.resize(Image.open(out / "giftbox-raw.png").size, Image.LANCZOS))
        x0, y0, x1, y1 = [int(v) for v in (out / "giftbox.box.txt").read_text().split()]
        top = np.asarray(closed).astype(np.float32).copy()
        cut = int(0.45 * (y1 - y0))  # the lid and the top of the box come from the edit; below it the closed box stays
        top[:cut] = opened[y0:y0 + cut, x0:x1]
        Image.fromarray(top.round().astype(np.uint8)).save(out / "giftbox-open.png")
        print("giftbox-open: the top of the box from the edit")
        return

    (W, H), (hx, hy), refs, tall = PROPS[name]
    A = standing()
    canvas = Image.new("RGBA", (W, H), (0, 255, 0, 255))
    canvas.alpha_composite(Image.fromarray(A), (hx - A.shape[1] // 2, hy - A.shape[0]))
    canvas.convert("RGB").save(out / f"{name}-canvas.png")
    her_box = (hx - A.shape[1] // 2, hy - A.shape[0], hx + (A.shape[1] + 1) // 2, hy)
    sheet_ref = eye_ref(A)

    best = None
    for t in range(tries):
        design = [out / f"{name}-design.png"] if (out / f"{name}-design.png").exists() else []  # an earlier draw to keep
        pic = codex(DRAW.format(out=out / f"{name}-try{t + 1}.png", what=what + (" Keep the shape and design of the image just before the canvas, in the colors given here, at the size given here." if design else "")),
                    [CASTLE] + refs + design + [out / f"{name}-canvas.png"], out / f"{name}-try{t + 1}.png")
        if pic is None:
            print(f"try {t + 1}: codex gave no image")
            continue
        pic = pic.resize((W, H), Image.LANCZOS)  # codex's own canvas size, brought back as a whole
        a = key(pic)
        x0, y0, x1, y1 = her_box
        m = 40
        crop = a[max(0, y0 - m):y1 + m, max(0, x0 - m):x1 + m].round().astype(np.uint8)
        fm, _, _, scale = find_face(crop, sheet_ref, np.arange(0.8, 1.21, 0.01))
        size = scale if fm >= FACE_MATCH else None
        keep = np.zeros(a.shape[:2], bool)  # every piece that is not her
        for p in pieces(a[..., 3] > 40):
            ys, xs = np.nonzero(p)
            if p.sum() > 200 and not ((xs < x1 + 6) & (xs >= x0 - 6) & (ys >= y0 - 6) & (ys < y1 + 6)).mean() > 0.5:
                keep |= p
        prop = a.copy()
        prop[..., 3] = np.where(keep, prop[..., 3], 0)
        ratio = (np.ptp(np.nonzero(keep.any(1))[0]) + 1) / A.shape[0] if keep.any() else 0
        ok = size is not None and abs(size - 1) <= GATE and (tall is None or tall[0] <= ratio <= tall[1])
        print(f"try {t + 1}: her eyes {'not found' if size is None else f'x{size:.3f} of her pasted size'}; the prop is {ratio:.2f} of her height")
        miss = (abs(size - 1) if size is not None else 1) + (0 if tall is None else max(0, tall[0] - ratio, ratio - tall[1]))
        if best is None or miss < best[0]:
            best = (miss, size, pic, a, prop)
        if ok:
            break
    if best is None:
        sys.exit("codex gave no image")
    _, size, pic, a, prop = best
    pic.save(out / f"{name}-raw.png")
    Image.fromarray(a.round().astype(np.uint8)).save(out / f"{name}-with-her.png")
    px0, py0, px1, py1 = bbox(prop)
    Image.fromarray(prop[py0:py1, px0:px1].round().astype(np.uint8)).save(out / f"{name}.png")
    (out / f"{name}.box.txt").write_text(f"{px0} {py0} {px1} {py1}")
    print(f"{name}: her size x{size if size else 0:.3f}; the prop is {px1 - px0} x {py1 - py0} px, she is {A.shape[0]} px tall")


if __name__ == "__main__":
    main()
