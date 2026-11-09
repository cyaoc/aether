"""Turn codex's four frames of one pose moving in place (a loop) into a frame strip that does not boil.

python frame-patch.py [--from-first | --ref-first] [--box x0,y0,x1,y1] [--take x0,y0,x1,y1] [--head] <ref> <frames.png> <out.png> [check.png]
python frame-patch.py --grid <ref> <out.png>

<ref> is the drawing the frames were made from (as petlib.load_ref: `sheet.webp:3`, `sheet.webp:2/4x1`, `single.webp`).
--grid writes the 2 x 2 grid gen-frames.sh hands codex: flat #00FF00, the reference already in the top-left quarter as
frame 1, so codex draws frames 2-4 beside it at its size. A change from one pose to another is not made here but by
inbetween.py.

<frames.png> is codex's 2 x 2 grid of frames on that green, frame 1 being the reference itself. Each frame is keyed off
its green and all four are lined up with the reference by frame 1 (petlib.register). Codex redraws everything a little
each frame, which would boil when played, so only where the frames really differ from frame 1 (the arm that waves, the
legs that pedal; found automatically, or only inside --box) is taken from them; everywhere else every frame keeps the
reference's pixels. When codex drew the still parts too differently for that edge to meet the reference (a bike tube
showing twice), --from-first uses its frame 1 as the base instead. Out: the four frames in one row, each the reference's
size, transparent, in the reference's own coordinates. --take always takes that box from the frames, every frame
including the first, for a part that moves all the time (walking legs), so nothing of the reference's stays there (a
shoe's shadow). --ref-first makes frame 1 the reference itself, for a loop played in place of its drawing (the idle
blink): the open eyes are then the drawing's own, so leaving the loop does not shift them by codex's pixel.

--head keeps her head: where codex's head did not move, the reference's whole head is kept, so a head codex drew a
little different never shows. Where her head moves (a sway, a hop) the frames keep codex's own head; nothing is scaled or
laid over it, and gen-loop.py draws the loop again when a frame's face comes out off size. Not for loops whose point is
the face changing (a blink).
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
sys.path.insert(0, str(Path(__file__).resolve().parent))  # petlib sits beside the tools, also under python -I
from petlib import bbox, codex_frames, green, load_ref, register, skin

if sys.argv[1] == "--grid":
    q = green(load_ref(sys.argv[2]))
    grid = Image.new("RGB", (2 * q.width, 2 * q.height), (0, 255, 0))
    grid.paste(q, (0, 0))
    grid.save(sys.argv[3])
    sys.exit()

args = sys.argv[1:]
from_first, ref_first, keep_head = "--from-first" in args, "--ref-first" in args, "--head" in args
args = [a for a in args if a not in ("--from-first", "--ref-first", "--head")]


def opt(name):
    if name not in args:
        return None
    k = args.index(name)
    v = tuple(map(int, args[k + 1].split(",")))
    del args[k:k + 2]
    return v


box, take = opt("--box"), opt("--take")
ref_spec, grid, out = args[:3]
ref = load_ref(ref_spec)
cw, ch = ref.size
R = np.asarray(ref, dtype=np.float32)
F, best = register(codex_frames(grid, max(cw, ch)), R)
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
if take:
    b = np.zeros((ch, cw), np.uint8)
    b[take[1]:take[3], take[0]:take[2]] = 255
    mask = np.maximum(mask, np.asarray(Image.fromarray(b).filter(ImageFilter.GaussianBlur(5)), dtype=np.float32) / 255)
print(f"moving part: {int((mask > 0.5).sum())} px of {cw * ch}")
mask = mask[..., None]


def skin_box(A):
    """Her face: the biggest patch of skin in the top half of the drawing, as a box (or None)."""
    if not (A[..., 3] > 40).any():
        return None
    x0, y0, x1, y1 = bbox(A)
    s = skin(A)
    s[int(y0 + 0.55 * (y1 - y0)):] = False
    img = Image.fromarray((s * 255).astype(np.uint8)).resize((cw // 2, ch // 2), Image.BOX).point(lambda v: 255 if v > 100 else 0)
    best_n, best_m = 0, None
    for label in range(1, 250):
        ys, xs = np.nonzero(np.asarray(img) == 255)
        if not len(xs):
            break
        ImageDraw.floodfill(img, (int(xs[0]), int(ys[0])), label)
        m = np.asarray(img) == label
        if m.sum() > best_n:
            best_n, best_m = m.sum(), m
    if best_n < 300:  # no face showing (turned away, covered)
        return None
    ys, xs = np.nonzero(best_m)
    return xs.min() * 2, ys.min() * 2, xs.max() * 2 + 2, ys.max() * 2 + 2


def crown(A):
    """Where the top of her hair is: its highest dark row and the middle of the hair just below it (hands are not dark)."""
    dark = (A[..., 3] > 200) & (A[..., :3].max(-1) < 110)
    ys, xs = np.nonzero(dark)
    if not len(ys):
        return None
    by, bx = np.nonzero(dark[ys.min():ys.min() + 60])
    return bx.mean(), ys.min()


def true_face():
    """Her face without a hand held against it: of the skin boxes in the reference and the frames, the shortest."""
    boxes = [b for b in (skin_box(A) for A in [R] + F) if b is not None]
    return min(boxes, key=lambda b: b[3] - b[1]) if boxes else None


FACE = true_face()


def still_head(k):
    """Where codex's head did not move from frame 1 to frame k (the top of her hair stayed put), the reference's whole head
    (face, bangs, crown) is kept, so a head codex drew a little bigger never shows (a face that goes fat for one frame of a
    wave). Skin off her face, in the reference or the frame, is a hand: it stays the frame's, so a hand that moved away
    leaves nothing behind and a hand that came in front of her face shows."""
    a, b = crown(F[0]), crown(F[k])
    if FACE is None or a is None or b is None or abs(a[0] - b[0]) > 6 or abs(a[1] - b[1]) > 6:
        return None
    x0, y0, x1, y1 = FACE
    cx, cy, w, h = (x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0
    oval = Image.new("L", (cw, ch), 0)
    ImageDraw.Draw(oval).ellipse((cx - 0.62 * w, cy - 0.2 * h - 0.9 * h, cx + 0.62 * w, cy - 0.2 * h + 0.9 * h), fill=255)
    keep = np.asarray(oval, dtype=np.float32) / 255
    face_oval = Image.new("L", (cw, ch), 0)
    ImageDraw.Draw(face_oval).ellipse((x0, y0, x1, y1), fill=255)
    off_face = ~(np.asarray(face_oval) > 0)
    hand = np.asarray(Image.fromarray((((skin(R) | skin(F[k])) & off_face) * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(13))) > 0
    keep[hand] = 0
    return np.asarray(Image.fromarray((keep * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(4)), dtype=np.float32)[..., None] / 255


base = F[0] if from_first else R
strip = Image.new("RGBA", (cw * 4, ch), (0, 0, 0, 0))
for k, f in enumerate(F):
    keep = still_head(k) if k and keep_head else None
    m = mask * (1 - keep) if keep is not None else mask
    res = base * (1 - m) + f * m
    if k == 0 and ref_first:
        res = R
    elif keep is not None:
        print(f"frame {k + 1}: head kept from the reference")
    strip.paste(Image.fromarray(res.round().astype(np.uint8)), (k * cw, 0))
strip.save(out)
if len(args) > 3:
    chk = Image.new("RGBA", strip.size, (60, 66, 90, 255))
    chk.alpha_composite(strip)
    chk.convert("RGB").save(args[3])
