"""Shared pieces of the desktop pet's picture tools, so a fix lands in every tool at once.

The board's tables (POSE, STRIP, TRANS) and its sheets as the board scales and places them; finding pieces and
skin in a drawing; keying codex's green; lining a frame up with a drawing; resampling.
"""
import json
import re
from functools import lru_cache
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

PKG = Path(__file__).resolve().parent.parent
BOARD = (PKG / "boards/DP-DesktopPet.dc.html").read_text(encoding="utf-8")
POSE = {p: (k, int(i)) for p, k, i in re.findall(r"(\w+): \['(\w+)', (\d)\]", re.search(r"const POSE = \{(.*?)\};", BOARD, re.S).group(1))}
STRIP = dict(re.findall(r"(\w+): \{ f: '(\w+)'", re.search(r"const STRIP = \{(.*?)\n\};", BOARD, re.S).group(1)))
TRANS = dict(re.findall(r"'(\w+>\w+)': '(\w+)'", re.search(r"const TRANS = \{(.*?)\n\};", BOARD, re.S).group(1)))
SAME_SPOT = dict(re.findall(r"(\w+): '(\w+)'", re.search(r"const SAME_SPOT = \{(.*?)\};", BOARD).group(1)))
_BLOB = dict(re.findall(r"(\w+): '([0-9a-f]{32})'", re.search(r"const BLOB = \{(.*?)\};", BOARD, re.S).group(1)))
_FILES = json.loads((PKG / "tools/blobs.json").read_text())
SHEET = {k: Path(_FILES[md5]).stem for k, md5 in _BLOB.items() if _FILES.get(md5, "").startswith("textures/pose-")}


# --- her drawings as the board has them -------------------------------------------------------------------------------

@lru_cache(None)
def cells(stem):
    """The six cells (3 x 2) of textures/<stem>.webp, as RGBA arrays."""
    im = np.asarray(Image.open(PKG / f"textures/{stem}.webp").convert("RGBA"))
    h, w = im.shape[0] // 2, im.shape[1] // 3
    return tuple(im[(i // 3) * h:(i // 3 + 1) * h, (i % 3) * w:(i % 3 + 1) * w] for i in range(6))


def rows(a):
    """Her rows (opacity over 40)."""
    return np.nonzero(a[..., 3] > 40)[0]


def scale(key):
    """Board px per picture px on a sheet: her standing height (its first cell, the same on every sheet) is 250 on the board."""
    return 250 / (np.ptp(rows(cells(SHEET[key])[0])) + 1)


def ground(key, i):
    """The ground line of cell i, as the board takes it: the lowest of her in that row of the sheet."""
    cs = cells(SHEET[key])
    return max(rows(cs[j]).max() for j in range((i // 3) * 3, (i // 3) * 3 + 3))


def anchor(a):
    """Her centre of mass across, which the board lines her drawings up on."""
    al = a[..., 3].astype(float)
    w = np.where(al > 40, al, 0)
    return (w * np.arange(a.shape[1])).sum() / w.sum()


def pose(name):
    """(drawing, board scale, ground, anchor) of a pose of the board; a pose in SAME_SPOT takes its earlier pose's centre."""
    k, i = POSE[name]
    a = cells(SHEET[k])[i]
    return a, scale(k), ground(k, i), anchor(cells(SHEET[k])[POSE[SAME_SPOT[name]][1]] if name in SAME_SPOT else a)


def load_ref(spec):
    """`sheet.webp:3` (cell 3 of a 3 x 2 sheet, counted from 1), `sheet.webp:2/4x1` (of a strip) or `single.webp`."""
    path, _, cell = spec.partition(":")
    im = Image.open(path if Path(path).exists() else PKG / path).convert("RGBA")
    if not cell:
        return im
    n, _, grid = cell.partition("/")
    cols, nrows = map(int, (grid or "3x2").split("x"))
    w, h = im.width // cols, im.height // nrows
    i = int(n) - 1
    return im.crop(((i % cols) * w, (i // cols) * h, (i % cols + 1) * w, (i // cols + 1) * h))


# --- looking at a drawing ---------------------------------------------------------------------------------------------

def pieces(mask):
    """Connected pieces of a boolean mask, biggest first."""
    img = Image.fromarray((mask * 255).astype(np.uint8)).copy()  # its own pixels, so the fills stick
    found = []
    for label in range(1, 250):
        ys, xs = np.nonzero(np.asarray(img) == 255)
        if not len(xs):
            break
        ImageDraw.floodfill(img, (int(xs[0]), int(ys[0])), label)
        found.append(np.asarray(img) == label)
    return sorted(found, key=lambda m: -m.sum())


def skin(a):
    """Her skin, blush counted (a warm-lit or blushing face is all blush and would fall apart without it)."""
    r, g, b, al = (a[..., i].astype(int) for i in range(4))
    plain = (r > 195) & (g > 145) & (b > 115) & (r > g) & (g > b) & (r - b > 18)
    blush = (r > 180) & (g > 90) & (b > 80) & (r - g > 35) & (r - b > 25)
    return (al > 200) & (plain | blush)


def cheek_width(a):
    """(cheek width, the boxes of her two eyes) in the picture's pixels, or None where her eyes are not found."""
    ys = np.nonzero(a[..., 3] > 40)[0]
    if not len(ys):
        return None
    y0, y1 = ys.min(), ys.max()
    s = skin(a)
    s[int(y0 + 0.55 * (y1 - y0)):] = False
    ps = pieces(s)
    if not ps or ps[0].sum() < 1500:
        return None
    face = ps[0]
    filled = np.zeros_like(face)
    for x in np.nonzero(face.any(0))[0]:
        c = np.nonzero(face[:, x])[0]
        filled[c.min():c.max() + 1, x] = True
    fy = np.nonzero(filled.any(1))[0]
    top, bot = fy.min(), fy.max()
    band = np.zeros_like(face)
    band[int(top + 0.3 * (bot - top)):int(top + 0.75 * (bot - top))] = True
    holes = [m for m in pieces(filled & ~face & band) if m.sum() > 150][:4]
    best = None
    for i in range(len(holes)):
        for j in range(i + 1, len(holes)):
            (xa, ya), (xb, yb) = [(np.nonzero(m)[1].mean(), np.nonzero(m)[0].mean()) for m in (holes[i], holes[j])]
            if abs(ya - yb) < 0.25 * abs(xa - xb) and abs(xa - xb) > 40 and (best is None or holes[i].sum() + holes[j].sum() > best[0]):
                best = (holes[i].sum() + holes[j].sum(), (ya + yb) / 2, (holes[i], holes[j]))
    if not best:
        return None
    ey = int(best[1])
    boxes = sorted((np.nonzero(m)[1].min(), np.nonzero(m)[0].min(), np.nonzero(m)[1].max(), np.nonzero(m)[0].max()) for m in best[2])
    return max(np.ptp(np.nonzero(filled[y])[0]) + 1 for y in range(ey - 15, ey + 11) if filled[y].any()), boxes


def warp(a, sx, sy):
    """The picture a sampled at (sx, sy) for every output pixel, bilinear and premultiplied, so edges blend right."""
    pm = a.astype(np.float32)
    pm[..., :3] *= pm[..., 3:] / 255
    H, W = a.shape[:2]
    x0, y0 = np.floor(sx).astype(int), np.floor(sy).astype(int)
    fx, fy = (sx - x0)[..., None], (sy - y0)[..., None]
    get = lambda yv, xv: np.where(((xv >= 0) & (xv < W) & (yv >= 0) & (yv < H))[..., None], pm[np.clip(yv, 0, H - 1), np.clip(xv, 0, W - 1)], 0)
    v = (get(y0, x0) * (1 - fx) + get(y0, x0 + 1) * fx) * (1 - fy) + (get(y0 + 1, x0) * (1 - fx) + get(y0 + 1, x0 + 1) * fx) * fy
    rgb = np.where(v[..., 3:] > 0, v[..., :3] * 255 / np.maximum(v[..., 3:], 1e-3), 0)
    return np.dstack([rgb, v[..., 3]])


# --- codex's frames ---------------------------------------------------------------------------------------------------

def green(img):
    """A drawing centred on a square of flat #00FF00, the way codex is handed a reference."""
    side = max(img.size)
    g = Image.new("RGBA", (side, side), (0, 255, 0, 255))
    g.alpha_composite(img, ((side - img.width) // 2, (side - img.height) // 2))
    return g.convert("RGB")


def key(img):
    """Green screen to alpha. A pixel's greenness says how much of the green ground shows through it (thin basket wires,
    gaps between hair strands); that much green is taken back out of its colour, so no olive fringe is left."""
    a = np.asarray(img.convert("RGB"), dtype=np.float32)
    g = a[..., 1] - np.maximum(a[..., 0], a[..., 2])
    alpha = np.clip(1 - (g - 8) / 235, 0, 1)
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
    found = []
    for label in range(1, 250):
        ys, xs = np.nonzero(np.asarray(img) == 255)
        if not len(xs):
            break
        ImageDraw.floodfill(img, (int(xs[0]), int(ys[0])), label)
        m = np.asarray(img) == label
        found.append((m.sum(), m[0].any() or m[-1].any() or m[:, 0].any() or m[:, -1].any(), m))
    biggest = max(n for n, _, _ in found)
    gone = np.zeros_like(found[0][2])
    for n, edge, m in found:
        if edge and n < 0.3 * biggest:
            gone |= m
    gone = np.asarray(Image.fromarray((gone * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3)).resize((a.shape[1], a.shape[0]), Image.NEAREST)) > 0
    f = f.copy()
    f[..., 3][gone] = 0
    return f


def codex_frames(path, side):
    """Codex's 2 x 2 grid of frames, each keyed off its green and cleared of strays, at side x side."""
    img = Image.open(path)
    gw, gh = img.width // 2, img.height // 2
    return [drop_strays(key(img.crop(((k % 2) * gw, (k // 2) * gh, (k % 2 + 1) * gw, (k // 2 + 1) * gh)).resize((side, side), Image.LANCZOS))) for k in range(4)]


def bbox(a):
    ys, xs = np.nonzero(a[..., 3] > 40)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def register(frames, R):
    """Line codex's frames up with the drawing R they were drawn from: frame 1 is slid and scaled until it fits R (first
    by its outline: same height, bottoms and centres together; then searched around that), and every frame gets that
    same placement, since they were drawn in one image. Returns the placed frames, R's size, and (misfit, s, dx, dy)."""
    ch, cw = R.shape[:2]
    side = frames[0].shape[0]

    def place(f, s, dx, dy):
        im = Image.fromarray(f.round().astype(np.uint8))
        n = round(side * s)
        im = im.resize((n, n), Image.LANCZOS)
        canvas = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
        canvas.paste(im, (round((cw - n) / 2 + dx), round((ch - n) / 2 + dy)))
        return np.asarray(canvas, dtype=np.float32)

    def misfit(F):
        d = np.abs(F[..., 3] - R[..., 3]) + np.abs(F[..., :3] - R[..., :3]).mean(-1) * (np.minimum(F[..., 3], R[..., 3]) / 255)
        return d.mean()

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
    return [place(f, *best[1:]) for f in frames], best

def gray(a):
    """The drawing on white, as grey levels."""
    al = a[..., 3:4].astype(np.float32) / 255
    return (a[..., :3].astype(np.float32) * al + 255 * (1 - al)).mean(-1)


def find_eyes(a, templates, scales=np.arange(0.8, 1.26, 0.05)):
    """Her two eyes in drawing a, each found by matching one of the standing drawing's eyes (templates, grey) across
    scales in the upper 60%: [(match 0-1, centre x, centre y, scale)] for her right and left eye."""
    from numpy.lib.stride_tricks import sliding_window_view
    g = gray(a)[:int(a.shape[0] * 0.6)]
    found = []
    for t in templates:
        best = (-1, 0, 0, 1)
        for s in scales:
            ts = np.asarray(Image.fromarray(t).resize((max(4, round(t.shape[1] * s)), max(4, round(t.shape[0] * s))), Image.BILINEAR), np.float32)
            th, tw = ts.shape
            if th >= g.shape[0] or tw >= g.shape[1]:
                continue
            win = sliding_window_view(g, (th, tw))[::2, ::2]
            wz, tz = win - win.mean(axis=(2, 3), keepdims=True), ts - ts.mean()
            ncc = (wz * tz).sum(axis=(2, 3)) / (np.sqrt((wz ** 2).sum(axis=(2, 3)) * (tz ** 2).sum()) + 1e-6)
            iy, ix = np.unravel_index(ncc.argmax(), ncc.shape)
            if ncc[iy, ix] > best[0]:
                best = (float(ncc[iy, ix]), ix * 2 + tw / 2, iy * 2 + th / 2, float(s))
        found.append(best)
    return found


ANCHOR_H = 360  # px: her everyday standing drawing, pasted at this height into every sheet and prop picture
FACE_MATCH = 0.7  # below this the two-eye template did not find her eyes (closed, turned away, behind her hands)


def standing():
    """Her everyday standing drawing (the standing sheet's cell 1), cropped and brought to ANCHOR_H px: the size ruler
    pasted into every sheet and prop picture."""
    a = cells(SHEET["common"])[0]
    x0, y0, x1, y1 = bbox(a)
    im = Image.fromarray(a[y0:y1, x0:x1])
    if im.height != ANCHOR_H:
        im = im.resize((round(im.width * ANCHOR_H / im.height), ANCHOR_H), Image.LANCZOS)
    return np.asarray(im)


def eye_ref(A):
    """The anchor's two eyes together as one template (an expression or a hand by one eye moves a single eye's match,
    not the pair's), and how high they are above her feet."""
    _, boxes = cheek_width(A)
    x0, x1 = min(b[0] for b in boxes) - 6, max(b[2] for b in boxes) + 6
    y0, y1 = min(b[1] for b in boxes) - 8, max(b[3] for b in boxes) + 4
    return {"pair": gray(A)[y0:y1, x0:x1].astype(np.float32), "eyeline": A.shape[0] - (y0 + y1) / 2}


def find_face(c, ref, scales=np.arange(0.8, 1.21, 0.02)):
    """(match, centre x, centre y, scale) of her eyes in drawing c against the anchor's: the scale is her face's size."""
    return find_eyes(c, [ref["pair"]], scales)[0]


def face_size(c, ref):
    """Her face's size against the anchor's, or None where her eyes are not found."""
    m, _, _, s = find_face(c, ref)
    return s if m >= FACE_MATCH else None


PROP_MIN = 400  # px: a loose piece at least this big is a thing lying on the ground, smaller ones are specks


def loose(a):
    """The things lying apart from her in drawing a (a dumbbell, skis, a fallen bike), as one mask: every piece but the
    biggest (her, with whatever she holds or sits in), its soft edge included."""
    ps = pieces(a[..., 3] > 40)
    m = np.zeros(a.shape[:2], bool)
    for q in ps[1:]:
        if q.sum() >= PROP_MIN:
            m |= q
    return np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))) > 0


def carry_props(src, dst):
    """dst with the things lying on the ground taken exactly from src: dst's own copies go and src's are laid behind her,
    so a dumbbell or a ski does not move between drawings once it has landed. Same size arrays, float or uint8 RGBA."""
    src, dst = src.astype(np.float32), dst.astype(np.float32)
    out = dst.copy()
    out[loose(dst)] = 0
    m = loose(src)
    under = np.zeros_like(dst)
    under[m] = src[m]
    ta, ua = out[..., 3:] / 255, under[..., 3:] / 255  # her over the things on the ground
    a = ta + ua * (1 - ta)
    rgb = (out[..., :3] * ta + under[..., :3] * ua * (1 - ta)) / np.maximum(a, 1e-6)
    return np.dstack([rgb, a * 255])


def spot(name):
    """The pose whose things on the ground this pose keeps (SAME_SPOT), or the pose itself."""
    return SAME_SPOT.get(name, name)
