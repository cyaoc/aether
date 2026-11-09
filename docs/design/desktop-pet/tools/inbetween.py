"""Make the in-between frames of a change from one pose of the board to another.

python inbetween.py <name> <from pose> <to pose> <out dir> [--tries 2]

Codex gets one 2 x 2 grid on flat green with frame 1 (the first pose) and frame 4 (the second pose) already in it, both
drawn just as the board shows them (each scaled from its sheet to the board, standing on the same ground line, lined up
on its centre of mass), and draws frames 2 and 3 into the empty quarters; what moves between them is frames/<name>.txt.
In-betweening from both keyframes is how cartoon in-betweening is done (ToonCrafter and the like); drawn in the same
image as the keyframes, the middle frames take their sizes from them. Handed the two poses as separate pictures with
"same size, same place", codex drew all four frames as tall as the first and the second pose came out up to 16% off.
Nothing is laid over codex's frames afterwards: keeping the first pose's head or face in them made her expression jump on
the last frame, and scaling heads frame by frame made her face grow past both poses and shrink back.

All four frames are keyed and lined up by one placement, the one that fits frame 1 onto the first pose (petlib.register).
Frame 4 must then land on the second pose (her height, her feet, her outline), and her face in frames 2 and 3 must be as
wide as the two poses' faces a third and two thirds of the way (petlib.cheek_width; codex sometimes draws an in-between
head 5% small, and the face then shrinks and grows back); where either is off by more than GATE, codex drew it wrong and
draws it again, up to --tries times; the best try is kept and its gate printed.

Out: <name>-strip.png, the four frames in a row in the first pose's cell, as the board plays them; where the second pose
does not fit that cell (skis sticking out, a pompom hat too tall), they go into the second pose's cell in
reverse order and the board plays them backwards. The last line printed is the board's TRANS entry. Also
<name>-check.png: the first pose, frames 2 and 3, the second pose, side by side as the board shows them.
"""
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))  # petlib sits beside the tools, also under python -I
from petlib import PKG, bbox, cheek_width, codex_frames, green, pieces, pose, register, warp

GATE = {"height": 0.04, "feet": 6, "overlap": 0.80, "face": 0.04}  # frame 4 against the second pose; faces of frames 2, 3
PROMPT = ("Use your built-in image generation tool (not code) to edit the attached image into ONE new image, then copy "
          "the generated PNG file to {out}. Do not write any code. The attached image is a 2 by 2 grid of FOUR ANIMATION "
          "FRAMES on flat #00FF00 green, in this order: top-left, top-right, bottom-left, bottom-right. Frame 1 (top-left) "
          "and frame 4 (bottom-right) are already drawn: keep them exactly as they are, at the same size and in the same "
          "place. Draw frame 2 (top-right) and frame 3 (bottom-left), the in-between moments of one continuous movement "
          "from frame 1 to frame 4: the same character with her proportions, line art, colors and rendering exactly as in "
          "frames 1 and 4, her head and face exactly as big as in frames 1 and 4 (the same width across her cheeks, not "
          "smaller); her size, her height and where she stands change only as frames 1 and 4 differ, a third and two "
          "thirds of the way, her feet on the same ground line; her face and her expression go over gradually from frame "
          "1's to frame 4's. Each frame stays inside its own quarter on the same perfectly flat solid #00FF00 green, with "
          "no green anywhere on her. {what} DO NOT DRAW: {negative}")

argv = sys.argv[1:]
tries = 2
if "--tries" in argv:
    k = argv.index("--tries")
    tries = int(argv[k + 1])
    del argv[k:k + 2]
name, first, second, out = argv[0], argv[1], argv[2], Path(argv[3]).resolve()
out.mkdir(parents=True, exist_ok=True)

a, ka, ga, xa = pose(first)
b, kb, gb, xb = pose(second)
r = kb / ka  # the second pose's pixels in the first pose's
# a canvas in the first pose's pixels holding both poses where the board puts them
H, W = b.shape[:2]
bx0, by0, bx1, by1 = bbox(b)
top = min(0, int(np.floor(ga + (by0 - gb) * r)) - 8)
left = min(0, int(np.floor(xa + (bx0 - xb) * r)) - 8)
right = max(a.shape[1], int(np.ceil(xa + (bx1 - xb) * r)) + 8)
ox, oy = -left, -top
size = (a.shape[0] + oy, right - left)  # rows, columns
A = np.zeros(size + (4,), np.float32)
A[oy:oy + a.shape[0], ox:ox + a.shape[1]] = a
yy, xx = np.mgrid[0:size[0], 0:size[1]].astype(np.float32)
B = warp(b, xb + (xx - ox - xa) / r, gb + (yy - oy - ga) / r)

# the grid codex fills in: frame 1 and frame 4 drawn, frames 2 and 3 empty green
side = max(size)
q = [green(Image.fromarray(np.clip(c, 0, 255).round().astype(np.uint8))) for c in (A, B)]
grid = Image.new("RGB", (2 * side, 2 * side), (0, 255, 0))
grid.paste(q[0], (0, 0))
grid.paste(q[1], (side, side))
grid.save(out / f"{name}-grid.png")
prompt = PROMPT.format(out=out / f"{name}-frames.png", what=(PKG / f"tools/frames/{name}.txt").read_text().strip(),
                       negative=(PKG / "tools/frames-negative.txt").read_text().strip())


ends = [(cheek_width(c) or [None])[0] for c in (A, B)]  # her cheek width in the two poses (None where it does not show)


def gate(F):
    """How far frame 4 is from the second pose (height ratio off 1, feet in pixels, overlap of the outlines), and how far
    the faces of frames 2 and 3 are from the two poses' (ratio off 1, the worse of the two; 0 where a face does not show)."""
    fy, by = np.nonzero(F[3][..., 3] > 40)[0], np.nonzero(B[..., 3] > 40)[0]
    m4, mb = F[3][..., 3] > 40, B[..., 3] > 40
    face = 0
    if all(ends):
        for j, t in ((1, 1 / 3), (2, 2 / 3)):
            m = cheek_width(F[j])
            face = max(face, abs(m[0] / (ends[0] + (ends[1] - ends[0]) * t) - 1) if m else 0)
    return {"height": abs(np.ptp(fy) / np.ptp(by) - 1), "feet": abs(int(fy.max()) - int(by.max())),
            "overlap": (m4 & mb).sum() / (m4 | mb).sum(), "face": face}


fails = lambda g: g["height"] > GATE["height"] or g["feet"] > GATE["feet"] or g["overlap"] < GATE["overlap"] or g["face"] > GATE["face"]
best = None
for t in range(tries):
    raw = out / f"{name}-frames.png"
    raw.unlink(missing_ok=True)
    subprocess.run(["codex", "exec", "--skip-git-repo-check", "-s", "workspace-write", "-C", str(out), "-i", str(out / f"{name}-grid.png"),
                    "-o", str(out / f"{name}.log.txt")], input=prompt, capture_output=True, text=True)
    if not raw.exists():
        print(f"try {t + 1}: codex gave no frames")
        continue
    F, place = register(codex_frames(raw, side), A)
    g = gate(F)
    print(f"try {t + 1}: frame 1 off the first pose by {place[0]:.1f}; frame 4 height {g['height'] * 100:.1f}% off, feet "
          f"{g['feet']} px off, outline overlap {g['overlap']:.2f}; in-between faces {g['face'] * 100:.1f}% off" + ("  FAILS" if fails(g) else ""))
    # among tries that all fail, the face counts first: an outline off by a strand of hair shows less than a face off in size
    score = lambda g: (fails(g), max(0, g["face"] - GATE["face"]), max(0, GATE["overlap"] - g["overlap"]), g["face"])
    if best is None or score(best[1]) > score(g):
        best = (F, g)
        raw.replace(out / f"{name}-frames-best.png")
    if not fails(g):
        break
if best is None:
    sys.exit(f"{name}: codex gave no frames")
F, g = best
for f in F:  # specks the keying left apart from her
    for p in pieces(f[..., 3] > 0):
        if p.sum() < 30:
            f[p] = 0

# the first pose's cell if all four frames fit in it; otherwise the second pose's cell, played backwards
inside = all(not (f[:oy, :, 3] > 40).any() and not (f[:, :ox, 3] > 40).any() and not (f[:, ox + a.shape[1]:, 3] > 40).any() for f in F)
if inside:
    cells_ = [f[oy:oy + a.shape[0], ox:ox + a.shape[1]] for f in F]
    entry = f"'{first}>{second}': '{name}'"
else:
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    cells_ = [warp(f, ox + xa + (xx - xb) * r, oy + ga + (yy - gb) * r) for f in F[::-1]]
    entry = f"'{second}>{first}': '{name}'"
Image.fromarray(np.clip(np.hstack(cells_), 0, 255).round().astype(np.uint8)).save(out / f"{name}-strip.png")
chk = Image.new("RGBA", (size[1] * 4, size[0]), (255, 255, 255, 255))
for i, c in enumerate([A, F[1], F[2], B]):
    chk.alpha_composite(Image.fromarray(np.clip(c, 0, 255).round().astype(np.uint8)), (i * size[1], 0))
chk.convert("RGB").save(out / f"{name}-check.png")
print(("GATE FAILED " if fails(g) else "") + entry)
