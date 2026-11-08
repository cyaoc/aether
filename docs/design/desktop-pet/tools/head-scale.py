"""Make her head the same size on every sheet, without making her bigger: enlarge only the head of a drawing.

python head-scale.py <factor> <in.png> <out.png> [--cells 3x2] [--skip 1] [--pad 48]

Codex drew each pose sheet with its own head-to-body ratio: scaled to the same standing height, her eyes sit 37 to 46 px
apart depending on the sheet, so switching to a pose from another sheet makes her face shrink or swell (the user saw it
as flickering; only a change of camera may change her size). Scaling the whole pose would make her body jump instead,
so only the head is enlarged, by <factor>, about her chin: above the chin everything grows by the factor, below it the
growth fades out over a band, so the long hair runs on with no seam, and the body is untouched. The chin is the bottom
of her face's skin; a frame where her face does not show (seen from behind, head down) uses the top of her hair and
the head length of the frames where it does. Each cell or frame gets <pad> transparent pixels on top first, so a bigger
head never runs out of its cell; the board measures everything from the pictures, so nothing else changes.
--cells: the grid of the picture (3x2 for a pose sheet, 4x1 for a frame strip); --skip: cells left as they are, counted
from 1 (a sheet's first cell is the size ruler and is only padded).
"""
import sys
import numpy as np
from PIL import Image, ImageDraw


def opt(name, default):
    if name in sys.argv:
        k = sys.argv.index(name)
        v = sys.argv[k + 1]
        del sys.argv[k:k + 2]
        return v
    return default


cols, rows = map(int, opt("--cells", "3x2").split("x"))
skip = {int(v) for v in opt("--skip", "").split(",") if v}
pad = int(opt("--pad", "48"))
factor, src, out = float(sys.argv[1]), sys.argv[2], sys.argv[3]


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


def head(a):
    """(chin x, chin y, top of hair y) of a drawing, or None where no face shows."""
    r, g, b, al = (a[..., i].astype(int) for i in range(4))
    ys, xs = np.nonzero(al > 40)
    if not len(ys):
        return None
    y0, y1 = ys.min(), ys.max()
    dark = (al > 200) & (np.maximum(np.maximum(r, g), b) < 110)
    dy = np.nonzero(dark)[0]
    crown = dy.min() if len(dy) else y0
    skin = (al > 200) & (r > 195) & (g > 145) & (b > 115) & (r > g) & (g > b) & (r - b > 18)
    skin[int(y0 + 0.6 * (y1 - y0)):] = False
    ps = pieces(skin)
    if not ps or ps[0].sum() < 1500:
        return None, None, crown
    fy, fx = np.nonzero(ps[0])
    return fx.mean(), fy.max(), crown


def bilinear(img, sx, sy):
    h, w = img.shape[:2]
    x0 = np.clip(np.floor(sx).astype(int), 0, w - 1); y0 = np.clip(np.floor(sy).astype(int), 0, h - 1)
    x1 = np.clip(x0 + 1, 0, w - 1); y1 = np.clip(y0 + 1, 0, h - 1)
    fx = np.clip(sx - x0, 0, 1)[..., None]; fy = np.clip(sy - y0, 0, 1)[..., None]
    top = img[y0, x0] * (1 - fx) + img[y0, x1] * fx
    bot = img[y1, x0] * (1 - fx) + img[y1, x1] * fx
    v = top * (1 - fy) + bot * fy
    outside = (sx < 0) | (sy < 0) | (sx > w - 1) | (sy > h - 1)
    v[outside] = 0
    return v


def grow(a, cx, chin, f, band):
    """The head above (cx, chin) grown by f, fading back to 1 over band pixels below the chin."""
    h, w = a.shape[:2]
    pm = a.astype(np.float32)
    pm[..., :3] *= pm[..., 3:] / 255  # premultiplied, so edges blend right
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    t = np.clip((yy - chin) / band, 0, 1)
    s = f + (1 - f) * (t * t * (3 - 2 * t))
    sx = cx + (xx - cx) / s
    sy = chin + (yy - chin) / s
    v = bilinear(pm, sx, sy)
    rgb = np.where(v[..., 3:] > 0, v[..., :3] * 255 / np.maximum(v[..., 3:], 1e-3), 0)
    return np.dstack([np.clip(rgb, 0, 255), np.clip(v[..., 3], 0, 255)]).round().astype(np.uint8)


im = np.asarray(Image.open(src).convert("RGBA"))
H, W = im.shape[:2]
cw, ch = W // cols, H // rows
cells = [np.pad(im[(i // cols) * ch:(i // cols + 1) * ch, (i % cols) * cw:(i % cols + 1) * cw], ((pad, 0), (0, 0), (0, 0))) for i in range(cols * rows)]
found = [head(c) for c in cells]
lens = [chin - crown for cx, chin, crown in (f for f in found if f) if chin is not None]
head_len = float(np.median(lens)) if lens else None
done = []
for i, c in enumerate(cells):
    f = found[i]
    if i + 1 in skip or f is None or factor == 1:
        done.append(c)
        continue
    cx, chin, crown = f
    if chin is None:  # no face showing: from the top of the hair and the usual head length
        if head_len is None:
            done.append(c)
            continue
        dark = (c[..., 3] > 200) & (c[..., :3].max(-1) < 110)
        ys, xs = np.nonzero(dark[crown:crown + 60])
        cx, chin = (xs.mean() if len(xs) else cw / 2), crown + head_len
    done.append(grow(c, cx, chin, factor, 0.35 * (chin - crown)))
    print(f"cell {i + 1}: chin at {cx:.0f},{chin:.0f}, top of hair {crown}")
sheet = np.zeros((rows * (ch + pad), cols * cw, 4), np.uint8)
for i, c in enumerate(done):
    sheet[(i // cols) * (ch + pad):(i // cols + 1) * (ch + pad), (i % cols) * cw:(i % cols + 1) * cw] = c
Image.fromarray(sheet).save(out)
