"""Turn codex's four animation frames of one drawing into a frame strip that does not boil.

python frame-patch.py [--from-first] [--box x0,y0,x1,y1] <ref> <frames.png> <out.png> [check.png]
python frame-patch.py --green <ref> <out.png>

<ref> is the drawing the frames were made from: `sheet.webp:3` (cell 3 of a 3 x 2 sheet, counted left to right, top row
first), `sheet.webp:2/4x1` (cell 2 of a 4 x 1 strip) or `single.webp`. --green writes it centred on a square of flat
#00FF00, the reference gen-frames.sh hands codex.

<frames.png> is codex's 2 x 2 grid of frames on that green, frame 1 being the reference itself. Each frame is keyed off
its green; frame 1 is slid and scaled until it lines up with the reference, and the other frames get the same placement
(they were drawn in one image, so they line up with each other). Codex redraws everything a little each frame, which
would boil when played, so only where the frames really differ from frame 1 (the arm that waves, the legs that pedal;
found automatically, or only inside --box) is taken from them; everywhere else every frame keeps the reference's
pixels. When codex drew the still parts too differently for that edge to meet the reference (a bike tube showing
twice), --from-first uses its frame 1 as the base instead. Out: the four frames in one row, each the reference's size,
transparent, in the reference's own coordinates.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter


def load_ref(spec):
    path, _, cell = spec.partition(":")
    im = Image.open(path).convert("RGBA")
    if not cell:
        return im
    n, _, grid = cell.partition("/")
    cols, rows = map(int, (grid or "3x2").split("x"))
    w, h = im.width // cols, im.height // rows
    i = int(n) - 1
    return im.crop(((i % cols) * w, (i // cols) * h, (i % cols + 1) * w, (i // cols + 1) * h))


if sys.argv[1] == "--green":
    ref = load_ref(sys.argv[2])
    side = max(ref.size)
    g = Image.new("RGBA", (side, side), (0, 255, 0, 255))
    g.alpha_composite(ref, ((side - ref.width) // 2, (side - ref.height) // 2))
    g.convert("RGB").save(sys.argv[3])
    sys.exit()

args = sys.argv[1:]
from_first = "--from-first" in args
args = [a for a in args if a != "--from-first"]
box = None
if "--box" in args:
    k = args.index("--box")
    box = tuple(map(int, args[k + 1].split(",")))
    del args[k:k + 2]
ref_spec, grid, out = args[:3]

ref = load_ref(ref_spec)
cw, ch = ref.size
side = max(cw, ch)
R = np.asarray(ref, dtype=np.float32)


def key(img):
    """Green screen to alpha. A pixel's greenness says how much of the green ground shows through it (thin basket wires,
    gaps between hair strands); that much green is taken back out of its colour, so no olive fringe is left."""
    a = np.asarray(img.convert("RGB"), dtype=np.float32)
    green = a[..., 1] - np.maximum(a[..., 0], a[..., 2])
    alpha = np.clip(1 - (green - 8) / 235, 0, 1)
    alpha[alpha < 0.06] = 0
    fg = (a - (1 - alpha)[..., None] * np.array([0, 255, 0], np.float32)) / np.maximum(alpha, 0.06)[..., None]
    fg[..., 1] = np.minimum(fg[..., 1], np.maximum(fg[..., 0], fg[..., 2]) + 24)
    return np.dstack([np.clip(fg, 0, 255), alpha * 255])


def drop_strays(f):
    """Codex's figures can reach over into the next quarter (a standing head poking up into the frame above): small
    pieces touching the frame's edge are cut away. Props apart from her inside the frame (a box in the air) stay."""
    a = f[..., 3]
    small = Image.fromarray(((a > 76) * 255).astype(np.uint8)).resize((a.shape[1] // 4, a.shape[0] // 4), Image.BOX)
    img = small.point(lambda v: 255 if v > 40 else 0).filter(ImageFilter.MaxFilter(3))
    pieces = []
    for label in range(1, 250):
        ys, xs = np.nonzero(np.asarray(img) == 255)
        if not len(xs):
            break
        ImageDraw.floodfill(img, (int(xs[0]), int(ys[0])), label)
        m = np.asarray(img) == label
        pieces.append((m.sum(), m[0].any() or m[-1].any() or m[:, 0].any() or m[:, -1].any(), m))
    biggest = max(n for n, _, _ in pieces)
    gone = np.zeros_like(pieces[0][2])
    for n, edge, m in pieces:
        if edge and n < 0.3 * biggest:
            gone |= m
    gone = np.asarray(Image.fromarray((gone * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3)).resize((a.shape[1], a.shape[0]), Image.NEAREST)) > 0
    f = f.copy()
    f[..., 3][gone] = 0
    return f


def place(f, s, dx, dy):
    """A square keyed frame scaled by s about its centre, moved by dx, dy, and cut to the reference's size."""
    im = Image.fromarray(f.round().astype(np.uint8))
    n = round(side * s)
    im = im.resize((n, n), Image.LANCZOS)
    canvas = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    canvas.paste(im, (round((cw - n) / 2 + dx), round((ch - n) / 2 + dy)))
    return np.asarray(canvas, dtype=np.float32)


def misfit(F):
    d = np.abs(F[..., 3] - R[..., 3]) + np.abs(F[..., :3] - R[..., :3]).mean(-1) * (np.minimum(F[..., 3], R[..., 3]) / 255)
    return d.mean()


img = Image.open(grid)
gw, gh = img.width // 2, img.height // 2
frames = [drop_strays(key(img.crop(((k % 2) * gw, (k // 2) * gh, (k % 2 + 1) * gw, (k // 2 + 1) * gh)).resize((side, side), Image.LANCZOS))) for k in range(4)]

def bbox(a):
    ys, xs = np.nonzero(a[..., 3] > 40)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


# start from the outlines (same height, bottoms and centres together), then search around that
rx0, ry0, rx1, ry1 = bbox(R)
fx0, fy0, fx1, fy1 = bbox(frames[0])
s0 = (ry1 - ry0) / (fy1 - fy0)
n0 = side * s0
dx0 = round((rx0 + rx1) / 2 - ((fx0 + fx1) / 2 * s0 + (cw - n0) / 2))
dy0 = round(ry1 - (fy1 * s0 + (ch - n0) / 2))
best = None
for step_s, span_s, step_d, span_d in ((0.01, 0.04, 2, 8), (0.003, 0.009, 1, 2)):
    c = best[1:] if best else (s0, dx0, dy0)
    for s in np.arange(c[0] - span_s, c[0] + span_s + 1e-9, step_s):
        for dx in range(c[1] - span_d, c[1] + span_d + 1, step_d):
            for dy in range(c[2] - span_d, c[2] + span_d + 1, step_d):
                e = misfit(place(frames[0], s, dx, dy))
                if best is None or e < best[0]:
                    best = (e, s, dx, dy)
F = [place(f, *best[1:]) for f in frames]
print(f"placed at scale {best[1]:.3f}, moved {best[2]},{best[3]}; frame 1 off the reference by {best[0]:.1f}")

# where the frames move: a big difference from frame 1, specks dropped, grown so the soft edge lands on still parts
diff = np.max([np.abs(f[..., 3] - F[0][..., 3]) + np.abs(f[..., :3] - F[0][..., :3]).mean(-1) * np.maximum(f[..., 3], F[0][..., 3]) / 255 for f in F[1:]], axis=0)
m = Image.fromarray((diff > 70).astype(np.uint8) * 255).filter(ImageFilter.MinFilter(3))
for _ in range(3):
    m = m.filter(ImageFilter.MaxFilter(9))
mask = np.asarray(m.filter(ImageFilter.GaussianBlur(6)), dtype=np.float32) / 255
if box:
    b = np.zeros((ch, cw), np.float32)
    b[box[1]:box[3], box[0]:box[2]] = 1
    mask = mask * np.asarray(Image.fromarray((b * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(5)), dtype=np.float32) / 255
print(f"moving part: {int((mask > 0.5).sum())} px of {cw * ch}")
mask = mask[..., None]

base = F[0] if from_first else R
strip = Image.new("RGBA", (cw * 4, ch), (0, 0, 0, 0))
for k, f in enumerate(F):
    strip.paste(Image.fromarray((base * (1 - mask) + f * mask).round().astype(np.uint8)), (k * cw, 0))
strip.save(out)
if len(args) > 3:
    chk = Image.new("RGBA", strip.size, (60, 66, 90, 255))
    chk.alpha_composite(strip)
    chk.convert("RGB").save(args[3])
