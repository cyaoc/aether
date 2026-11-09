"""Give a drawing the reference's head, so her face is the very same face in both.

python head-swap.py <ref> <dx> <in.png> <out.png> [--cells 3x2] [--only 2]

Codex draws the face anew in every cell of a sheet: in the wave the face was as wide at the cheeks as in the everyday
pose but its jaw was narrower (by up to 5 px on the board) and its chin 2 px higher, so her face slimmed down as she
began to wave. Where the head does not move, the reference's head (<ref>: `sheet.webp:1`, as in frame-patch.py) is laid
over the drawing (or over frames whose head was kept but whose jaw by a hand stayed codex's), <dx> pixels to the side (the difference of the two cells' anchors, so on the board the face lands where
the reference's does): its whole face, the bangs and the crown, inside an oval about the face down to the chin. The rest
(long hair, neck, body) stays the drawing's, and so does a hand in front of the head (skin off the reference's face).
--cells: the grid of <in> (3x2 a pose sheet, 4x1 a frame strip); --only: the cells to change, counted from 1.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter


def opt(name, default):
    if name in sys.argv:
        k = sys.argv.index(name)
        v = sys.argv[k + 1]
        del sys.argv[k:k + 2]
        return v
    return default


def pieces(mask):
    img = Image.fromarray((mask * 255).astype(np.uint8)).copy()
    found = []
    for label in range(1, 250):
        ys, xs = np.nonzero(np.asarray(img) == 255)
        if not len(xs):
            break
        ImageDraw.floodfill(img, (int(xs[0]), int(ys[0])), label)
        found.append(np.asarray(img) == label)
    return sorted(found, key=lambda m: -m.sum())


def skin(a):
    r, g, b, al = (a[..., i].astype(int) for i in range(4))
    return (al > 200) & (r > 195) & (g > 145) & (b > 115) & (r > g) & (g > b) & (r - b > 18)


def grow(mask, px):
    return np.asarray(Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(2 * px + 1))) > 0


cols, rows = map(int, opt("--cells", "3x2").split("x"))
only = {int(v) for v in opt("--only", "").split(",") if v}
ref_spec, dx, src, out = sys.argv[1], int(sys.argv[2]), sys.argv[3], sys.argv[4]

path, _, n = ref_spec.partition(":")
sheet = Image.open(path).convert("RGBA")
w, h = sheet.width // 3, sheet.height // 2
i = int(n) - 1
R = np.asarray(sheet.crop(((i % 3) * w, (i // 3) * h, (i % 3 + 1) * w, (i // 3 + 1) * h)))
R = np.roll(R, dx, axis=1)

# the reference's face: its biggest piece of skin above the shoulders, holes (eyes, mouth) filled column by column
s = skin(R)
s[int(0.55 * h):] = False
face = pieces(s)[0]
filled = np.zeros_like(face)
for x in np.nonzero(face.any(0))[0]:
    c = np.nonzero(face[:, x])[0]
    filled[c.min():c.max() + 1, x] = True
ys, xs = np.nonzero(filled)
chin = ys.max()
wide = [y for y in range(h) if filled[y].sum() > 0.8 * (xs.max() - xs.min())]  # the cheek rows, not the parting strand
cx, fw = (xs.min() + xs.max()) / 2, xs.max() - xs.min()
dark = (R[..., 3] > 200) & (R[..., :3].max(-1) < 110)
crown = np.nonzero(dark)[0].min()
oval = Image.new("L", (w, h), 0)
ImageDraw.Draw(oval).ellipse((cx - 0.62 * fw, crown - 4, cx + 0.62 * fw, 2 * min(wide) - crown + 4), fill=255)
head = np.asarray(oval) > 0
head[chin + 2:] = False
head |= grow(filled, 3)

im = np.asarray(Image.open(src).convert("RGBA")).astype(np.float32)
H, W = im.shape[:2]
cw, ch = W // cols, H // rows
assert (cw, ch) == (w, h), "cells must be the reference's size"
res = im.copy()
for k in range(cols * rows):
    if only and k + 1 not in only:
        continue
    r, c = divmod(k, cols)
    cell = im[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw]
    # her hand in front of her head stays in front: skin where the reference has none, below her eyes (above them it is
    # forehead showing between differently drawn bangs), in pieces bigger than the slivers two necks or ears leave
    seed = skin(cell) & ~grow(skin(R) | filled, 2)
    seed[:min(wide) + 15] = False
    hand = grow(np.any([p for p in pieces(seed) if p.sum() > 150] or [np.zeros_like(seed)], axis=0), 3)
    m = Image.fromarray(((head & ~hand) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))
    m = np.asarray(m, dtype=np.float32)[..., None] / 255
    # composite premultiplied, so the oval's soft edge blends the two drawings' edges right
    a, b = cell.copy(), R.astype(np.float32)
    a[..., :3] *= a[..., 3:] / 255
    b[..., :3] *= b[..., 3:] / 255
    v = a * (1 - m) + b * m
    rgb = np.where(v[..., 3:] > 0, v[..., :3] * 255 / np.maximum(v[..., 3:], 1e-3), 0)
    res[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw] = np.dstack([rgb, v[..., 3]])
    print(f"cell {k + 1}: head laid over")
Image.fromarray(np.clip(res, 0, 255).round().astype(np.uint8)).save(out)
