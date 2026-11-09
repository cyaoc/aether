"""Make her head the same size on every sheet, without making her bigger: enlarge only the head of a drawing.

python head-scale.py <factor>[,<factor>...] <in.png> <out.png> [--cells 3x2] [--skip 1] [--pad 48]

Codex drew each pose sheet with its own head-to-body ratio: scaled to the same standing height, her eyes sit 37 to 46 px
apart depending on the sheet, so switching to a pose from another sheet makes her face shrink or swell (the user saw it
as flickering; only a change of camera may change her size). Scaling the whole pose would make her body jump instead,
so only the head is enlarged, by <factor>, about her chin: above the chin everything grows by the factor, below it the
growth fades out over a band, so the long hair runs on with no seam, and the body is untouched. The chin is the bottom
of her face's skin; a frame where her face does not show (seen from behind, head down) uses the top of her hair and
the head length of the frames where it does. Each cell or frame gets <pad> transparent pixels on top first, so a bigger
head never runs out of its cell; the board measures everything from the pictures, so nothing else changes.
--cells: the grid of the picture (3x2 for a pose sheet, 4x1 for a frame strip); --skip: cells left as they are, counted
from 1 (a sheet's first cell is the size ruler and is only padded). Several factors, one per cell, scale each cell by
its own (face-width.py measures them); a factor of 1 leaves a cell as it is, only padded. A head that would reach the
top of its cell is reported: it needs more --pad.
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))  # petlib sits beside the tools, also under python -I
from petlib import pieces, skin, warp


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
factors, src, out = [float(v) for v in sys.argv[1].split(",")], sys.argv[2], sys.argv[3]


def head(a):
    """(chin x, chin y, top of hair y) of a drawing, or None where no face shows."""
    ys = np.nonzero(a[..., 3] > 40)[0]
    if not len(ys):
        return None
    y0, y1 = ys.min(), ys.max()
    dark = (a[..., 3] > 200) & (a[..., :3].max(-1) < 110)
    dy = np.nonzero(dark)[0]
    crown = dy.min() if len(dy) else y0
    s = skin(a)
    s[int(y0 + 0.6 * (y1 - y0)):] = False
    ps = pieces(s)
    if not ps or ps[0].sum() < 1500:
        return None, None, crown
    fy, fx = np.nonzero(ps[0])
    return fx.mean(), fy.max(), crown


def grow(a, cx, chin, f, band):
    """The head above (cx, chin) grown by f, fading back to 1 over band pixels below the chin."""
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    t = np.clip((yy - chin) / band, 0, 1)
    s = f + (1 - f) * (t * t * (3 - 2 * t))
    return np.clip(warp(a, cx + (xx - cx) / s, chin + (yy - chin) / s), 0, 255).round().astype(np.uint8)


im = np.asarray(Image.open(src).convert("RGBA"))
H, W = im.shape[:2]
cw, ch = W // cols, H // rows
cells = [np.pad(im[(i // cols) * ch:(i // cols + 1) * ch, (i % cols) * cw:(i % cols + 1) * cw], ((pad, 0), (0, 0), (0, 0))) for i in range(cols * rows)]
found = [head(c) for c in cells]
lens = [chin - crown for cx, chin, crown in (f for f in found if f) if chin is not None]
head_len = float(np.median(lens)) if lens else None
done = []
for i, c in enumerate(cells):
    f, factor = found[i], factors[i] if len(factors) > 1 else factors[0]
    if i + 1 in skip or f is None or factor == 1:
        if factor != 1 and i + 1 not in skip:
            print(f"cell {i + 1}: no head found, left as it is")
        done.append(c)
        continue
    cx, chin, crown = f
    if chin is None:  # no face showing: from the top of the hair and the usual head length
        if head_len is None:
            print(f"cell {i + 1}: no face anywhere to take a head length from, left as it is")
            done.append(c)
            continue
        dark = (c[..., 3] > 200) & (c[..., :3].max(-1) < 110)
        ys, xs = np.nonzero(dark[crown:crown + 60])
        cx, chin = (xs.mean() if len(xs) else cw / 2), crown + head_len
    done.append(grow(c, cx, chin, factor, 0.35 * (chin - crown)))
    print(f"cell {i + 1}: x{factor}, chin at {cx:.0f},{chin:.0f}, top of hair {crown}")
    if (done[-1][:2, :, 3] > 40).any():
        print(f"cell {i + 1}: her head reaches the top of the cell, give it more --pad")
sheet = np.zeros((rows * (ch + pad), cols * cw, 4), np.uint8)
for i, c in enumerate(done):
    sheet[(i // cols) * (ch + pad):(i // cols + 1) * (ch + pad), (i % cols) * cw:(i % cols + 1) * cw] = c
Image.fromarray(sheet).save(out)
