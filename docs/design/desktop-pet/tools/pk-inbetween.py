"""Make the in-between frames of a change from one pose of the prankster to another (the chest game's PKTRANS).

python pk-inbetween.py <name> <from pose> <to pose> <out dir> [--tries 2]

As inbetween.py does for her: one 2 x 2 grid on flat green, frame 1 the first pose and frame 4 the second, each as the
board shows it (scaled by its sheet's ruler to the first pose's sheet, and lined up on its centre of mass, where the
board puts the prankster's place), and codex draws frames 2 and 3 into the empty quarters; tools/frames/<name>.txt says
what moves. Every frame is keyed, and slid (never scaled) back onto the grid as it was laid out: codex can draw a
quarter's figure somewhere else in it (it did with her pulling loop), so frame 1 is slid onto the first pose, frame 4
onto the second, and frames 2 and 3 a third and two thirds of the way between. The gate: frames 1 and 4 must lie on the
poses (their outlines overlapping at least OVERLAP), and frames 2 and 3 must be about as big as the poses (area within
SIZE of a third and two thirds of the way); otherwise it draws again, up to --tries times, and keeps the best.

Out: <name>-strip.png, the four frames in a row, each in the first pose's cell (512 x 512) as the board draws it, and
<name>-check.png. The last line printed is the board's PKTRANS entry.
"""
import json
import re
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image

T = Path(__file__).resolve().parent
PKG = T.parent
sys.path.insert(0, str(T))
from petlib import codex_frames  # noqa: E402

CELL, OVERLAP, SIZE = 512, 0.85, 0.15
BOARD = (PKG / "boards/DP-DesktopPet.dc.html").read_text(encoding="utf-8")
PKPOSE = {p: (k, int(i)) for p, k, i in re.findall(r"(\w+): \['(pk\d)', (\d)\]", re.search(r"const PKPOSE = \{(.*?)\};", BOARD, re.S).group(1))}
BLOB = dict(re.findall(r"(\w+): '([0-9a-f]{32})'", re.search(r"const BLOB = \{(.*?)\n\};", BOARD, re.S).group(1)))
FILES = json.loads((PKG / "tools/blobs.json").read_text())
PROMPT = ("Use your built-in image generation tool (not code) to edit the attached image into ONE new image, then copy "
          "the generated PNG file to {out}. Do not write any code. The attached image is a 2 by 2 grid of FOUR ANIMATION "
          "FRAMES on flat #00FF00 green, in this order: top-left, top-right, bottom-left, bottom-right. Frame 1 (top-left) "
          "and frame 4 (bottom-right) are already drawn: keep them exactly as they are, at the same size and in the same "
          "place. Draw frame 2 (top-right) and frame 3 (bottom-left), the in-between moments of one continuous movement "
          "from frame 1 to frame 4: the same little purple cloud creature with the black eye mask, its proportions, line "
          "art, colors and rendering exactly as in frames 1 and 4, exactly as big as in frames 1 and 4, in the middle of "
          "its quarter where frames 1 and 4 are in theirs; its expression goes over gradually from frame 1's to frame "
          "4's. Each frame stays inside its own quarter on the same perfectly flat solid #00FF00 green, with no green "
          "anywhere on it. {what} DO NOT DRAW: {negative}")


def cell(pose):
    k, i = PKPOSE[pose]
    a = np.asarray(Image.open(PKG / FILES[BLOB[k]]).convert("RGBA"), np.float32)
    w, h = a.shape[1] // 3, a.shape[0] // 2
    ruler = a[:h, :w, 3] > 40
    ys = np.nonzero(ruler.any(1))[0]
    return a[(i // 3) * h:(i // 3 + 1) * h, (i % 3) * w:(i % 3 + 1) * w], ys.max() - ys.min() + 1


def centre(a):
    w = a[..., 3]
    ys, xs = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    return (xs * w).sum() / w.sum(), (ys * w).sum() / w.sum()


def placed(a, s, at):
    """Drawing a scaled by s with its centre of mass at `at`, on a transparent CELL x CELL square."""
    im = Image.fromarray(a.round().clip(0, 255).astype(np.uint8))
    if s != 1:
        im = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
    b = np.asarray(im, np.float32)
    cx, cy = centre(b)
    out = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    out.alpha_composite(im, (round(at[0] - cx), round(at[1] - cy)))
    return np.asarray(out, np.float32)


def slid(f, dx, dy):
    g = np.zeros_like(f)
    h, w = f.shape[:2]
    g[max(0, dy):h + min(0, dy), max(0, dx):w + min(0, dx)] = f[max(0, -dy):h - max(0, dy), max(0, -dx):w - max(0, dx)]
    return g


def onto(f, r):
    """The slide that lays f on r best, and how far off f then is."""
    def off(dx, dy):
        g = slid(f, dx, dy)
        return (np.abs(g[..., 3] - r[..., 3]) + np.abs(g[..., :3] - r[..., :3]).mean(-1) * np.minimum(g[..., 3], r[..., 3]) / 255).mean()
    d, dx, dy = min((off(x, y), x, y) for y in range(-48, 49, 4) for x in range(-48, 49, 4))
    return min((off(x, y), x, y) for y in range(dy - 3, dy + 4) for x in range(dx - 3, dx + 4))


def overlap(a, b):
    a, b = a[..., 3] > 40, b[..., 3] > 40
    return (a & b).sum() / max(1, (a | b).sum())


def main():
    argv = sys.argv[1:]
    tries = 2
    if "--tries" in argv:
        k = argv.index("--tries")
        tries = int(argv[k + 1])
        del argv[k:k + 2]
    name, a_pose, b_pose, out = argv[0], argv[1], argv[2], Path(argv[3]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    A, ra = cell(a_pose)
    B, rb = cell(b_pose)
    mid = (CELL / 2, CELL / 2)
    PA, PB = placed(A, 1, mid), placed(B, ra / rb, mid)   # the poses as laid into their quarters
    grid = Image.new("RGBA", (2 * CELL, 2 * CELL), (0, 255, 0, 255))
    grid.alpha_composite(Image.fromarray(PA.round().astype(np.uint8)), (0, 0))
    grid.alpha_composite(Image.fromarray(PB.round().astype(np.uint8)), (CELL, CELL))
    grid.convert("RGB").save(out / f"{name}-grid.png")
    prompt = PROMPT.format(out=out / f"{name}-frames.png", what=(T / f"frames/{name}.txt").read_text().strip(),
                           negative=(T / "pk-frames-negative.txt").read_text().strip())
    best = None
    for t in range(tries):
        (out / f"{name}-frames.png").unlink(missing_ok=True)
        subprocess.run([str(T / "codex.sh"), "-C", str(out), "-i", str(out / f"{name}-grid.png"), "-o", str(out / f"{name}.log.txt")],
                       input=prompt, text=True, capture_output=True, timeout=900)
        if not (out / f"{name}-frames.png").exists():
            print(f"try {t + 1}: codex gave no image")
            continue
        F = codex_frames(out / f"{name}-frames.png", CELL)
        d1, x1, y1 = onto(F[0], PA)
        d4, x4, y4 = onto(F[3], PB)
        F = [slid(f, round(x1 + (x4 - x1) * k / 3), round(y1 + (y4 - y1) * k / 3)) for k, f in enumerate(F)]
        o1, o4 = overlap(F[0], PA), overlap(F[3], PB)
        area = [(f[..., 3] > 40).sum() for f in F]
        a1, a4 = (PA[..., 3] > 40).sum(), (PB[..., 3] > 40).sum()
        sizes = [area[k] / (a1 + (a4 - a1) * k / 3) for k in (1, 2)]
        miss = max(0, OVERLAP - o1) + max(0, OVERLAP - o4) + sum(max(0, abs(s - 1) - SIZE) for s in sizes)
        print(f"try {t + 1}: frame 1 overlaps the first pose {o1:.2f}, frame 4 the second {o4:.2f}; frames 2 and 3 x{sizes[0]:.2f} x{sizes[1]:.2f} as big"
              + ("" if miss == 0 else "  GATE FAILED"))
        if best is None or miss < best[0]:
            best = (miss, F)
        if miss == 0:
            break
    if best is None:
        sys.exit("codex gave no image")
    F = best[1]
    # into the first pose's cell: the cell's own centre of mass where the quarter had it
    cx, cy = centre(A)
    dx, dy = round(cx - mid[0]), round(cy - mid[1])
    strip = np.hstack([slid(f, dx, dy) for f in F])
    Image.fromarray(strip.round().clip(0, 255).astype(np.uint8)).save(out / f"{name}-strip.png")
    check = Image.new("RGBA", (4 * CELL, CELL), (200, 200, 208, 255))
    check.alpha_composite(Image.fromarray(strip.round().clip(0, 255).astype(np.uint8)))
    check.convert("RGB").resize((4 * CELL // 2, CELL // 2)).save(out / f"{name}-check.png")
    print(f"'{a_pose}>{b_pose}': '{name}'")


if __name__ == "__main__":
    main()
