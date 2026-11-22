"""The PK sign (README 15): codex draws her holding a blank sign; the board draws its own sign, with the words, over it.

python sign.py size <sheet.png> [--ref 3] [--tol 0.05]
python sign.py wipe <in.png> <out.png> [--strip]
python sign.py steps <strip.png>[,<strip2.png>] <order> [--most 14]

size: measures codex's sign in every cell of a pose sheet, so it does not change size between poses. The sign's face is
the biggest connected patch of its cream colour in a cell (her blouse and socks are smaller and apart). Each cell's face
is compared with the reference cell (cell 3, the sign held up over her head): wider or taller by more than --tol fails,
and the failing cells are printed as a --redo list for gen-sheet.py. Sizes are also given on the page (she is 250 px tall
there, 360 on the sheet), to check the words fit.

wipe: takes codex's sign out of every cell of a pose sheet (3 x 2), or every frame of a strip (--strip, 4 in a row),
keeping her hands on it, and prints the board's SIGN_AT entry for it: per cell [x, y, angle, edge, hold, h], where x, y
is the middle of the sign's outer edge the board lays its own sign along, in the cell's own pixels, angle its tilt in
radians (its right end lower is positive), edge 'b' (its bottom edge: the sign is up over her head, nothing of her above
it) or 't' (its top edge: her head shows over it, so the board's taller sign reaches further down, over her legs, not up
over her face), hold the edge her hands are at ('b' under it, 't' her fingertips over its top edge) and h codex's sign's
outer height; null where there is no sign. The board's sign is bigger than codex's every way, so it covers every pixel
taken out. Taken out: the sign with its border and a margin for its shadow, widened away from her (up and sideways when she
holds it from below, down and sideways when from behind), so none of her hair or eyes goes; kept: her skin near the edge
she holds, which the board draws again over its sign.

steps: how far the sign moves from frame to frame as the board plays a loop (its STRIP: order is the frames' numbers in
the order played, 0-3 the first strip, 4-7 the second), on codex's strips before they are wiped: page px the farther end
of the board's sign (SIGN_W wide, at codex's sign's place and tilt) moves at each step, there and back to the start. A
step over --most px is printed BIG: the user saw such loops flash (“没有过度帧、她的动作一闪一闪的”: the first bobbing
loop's sign jumped 31-38 px a frame, the first sway's ends 13-19 px; drawn again a little at a time, 1-14 px).
"""
import json
import sys
from collections import deque
import numpy as np
from PIL import Image

PAGE = 250 / 360
BORDER, MARGIN, HOLD = 14, 18, 30  # codex's pink border and dark outline beyond the cream, the shadow beyond that, her hands inside the edge
SIGN_W, SIGN_H = 252, 140  # the board's sign (SIGN), page px: what is wiped must fit under it


def cream(a):
    r, g, b, al = (a[..., k].astype(int) for k in range(4))
    return (al > 200) & (r > 238) & (g > 228) & (b > 205) & (r - b < 35)


def skin(a):
    r, g, b, al = (a[..., k].astype(int) for k in range(4))
    return (al > 150) & (r > 205) & (g > 160) & (g < 230) & (b > 130) & (b < 215) & (r - b > 25) & ~cream(a)


def patches(m):
    """Each 4-connected patch of m, as its pixels (ys, xs)."""
    h, w = m.shape
    seen = np.zeros_like(m)
    for y0, x0 in zip(*np.nonzero(m)):
        if seen[y0, x0]:
            continue
        seen[y0, x0] = True
        q, ys, xs = deque([(y0, x0)]), [], []
        while q:
            y, x = q.popleft()
            ys.append(y)
            xs.append(x)
            for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= yy < h and 0 <= xx < w and m[yy, xx] and not seen[yy, xx]:
                    seen[yy, xx] = True
                    q.append((yy, xx))
        yield np.array(ys), np.array(xs)


def biggest(m):
    """The pixels (ys, xs) of the biggest 4-connected patch of m, or None."""
    return max(patches(m), key=lambda p: len(p[0]), default=None)


def face(cell):
    """Codex's sign face: centre (x, y), angle, width, height (along and across it), or None."""
    p = biggest(cream(cell))
    if p is None or len(p[0]) < 2000:  # a patch of blouse is not a sign
        return None
    ys, xs = p
    cx, cy = xs.mean(), ys.mean()
    w_, v_ = np.linalg.eigh(np.cov(np.vstack([xs - cx, ys - cy])))
    ux, uy = v_[:, 1] if v_[0, 1] >= 0 else -v_[:, 1]  # along its long side, pointing right
    u, v = (xs - cx) * ux + (ys - cy) * uy, -(xs - cx) * uy + (ys - cy) * ux
    du, dv = (u.max() + u.min()) / 2, (v.max() + v.min()) / 2
    return cx + du * ux - dv * uy, cy + du * uy + dv * ux, float(np.arctan2(uy, ux)), u.max() - u.min(), v.max() - v.min()


def local(shape, f):
    """Every pixel's place along (u) and across (v, down positive) the sign."""
    cx, cy, a = f[:3]
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]]
    return (xx - cx) * np.cos(a) + (yy - cy) * np.sin(a), -(xx - cx) * np.sin(a) + (yy - cy) * np.cos(a)


def wipe_cell(cell):
    """(the cell without codex's sign, its SIGN entry or None)."""
    f = face(cell)
    if f is None:
        return cell, None
    cx, cy, a, w, h = f
    u, v = local(cell.shape, f)
    sk = skin(cell)
    near = np.abs(u) <= w / 2 + BORDER
    # fingertips curled over its top edge lie on its face; her chin just over a sign held low, or her hands under its
    # bottom edge, touch it only at the border (a tie is her hands under it). Counted in her skin's own colour, greener
    # than blue by 17 or more: where codex's pink border meets the cream is skin's colour too, but bluer, and on both edges
    # it outnumbered her fingertips.
    hand = sk & (cell[..., 1].astype(int) - cell[..., 2].astype(int) >= 17) & near
    bottom = (hand & (v > h / 2 - 25) & (v <= h / 2 + 4)).sum()
    top = (hand & (v < -h / 2 + 25) & (v >= -h / 2)).sum()
    edge = "b" if bottom >= top else "t"
    side = w / 2 + BORDER  # beside it: no shadow, and her hair hangs there when she hides behind it
    if edge == "b":
        out = (np.abs(u) <= side) & (v >= -h / 2 - BORDER - MARGIN) & (v <= h / 2 + BORDER)
        hands = v >= h / 2 - HOLD
    else:
        out = (np.abs(u) <= side) & (v >= -h / 2 - BORDER) & (v <= h / 2 + BORDER + MARGIN)
        hands = v <= -h / 2 + HOLD
    # her hands: skin by the edge she holds that runs on outside the sign, into her arms or fingertips (where codex's pink
    # border meets the cream, its colour is skin's too, but it runs on nowhere)
    keep = np.zeros_like(sk)
    for ys, xs in patches(sk):
        if (~out[ys, xs]).any():
            keep[ys, xs] = True
    res = cell.copy()
    res[out & ~(keep & hands), 3] = 0
    # laid along its top edge when anything of her shows over it (her head, her hair), else along its bottom edge
    over = (res[..., 3] > 40) & (np.abs(u) <= w / 2) & (v < -h / 2 - BORDER - 4)
    hold, edge = edge, "t" if over.sum() > 200 else "b"
    d = h / 2 + BORDER if edge == "b" else -(h / 2 + BORDER)
    x, y = cx - d * np.sin(a), cy + d * np.cos(a)
    wide, tall = (w + 2 * BORDER) * PAGE, (h + 2 * BORDER + MARGIN) * PAGE
    if wide > SIGN_W or tall > SIGN_H:
        print(f"codex's sign {wide:.0f}x{tall:.0f} page px with what was wiped: the board's {SIGN_W}x{SIGN_H} does not cover it",
              file=sys.stderr)
    return res, [round(float(x), 1), round(float(y), 1), round(a, 3), edge, hold, round(float(h + 2 * BORDER))]


def split(im, strip):
    n, m = (4, 1) if strip else (3, 2)
    w, h = im.shape[1] // n, im.shape[0] // m
    return [(x * w, y * h, im[y * h:(y + 1) * h, x * w:(x + 1) * w]) for y in range(m) for x in range(n)], w, h


def wipe(src, dst, strip):
    im = np.asarray(Image.open(src).convert("RGBA"))
    out, entries = im.copy(), []
    parts, w, h = split(im, strip)
    for x, y, c in parts:
        res, e = wipe_cell(c)
        out[y:y + h, x:x + w] = res
        entries.append(e)
    Image.fromarray(out).save(dst)
    print(json.dumps(entries).replace('"', "'"))


def size(src, ref, tol):
    parts, _, _ = split(np.asarray(Image.open(src).convert("RGBA")), False)
    sizes = {n: face(c) for n, (_, _, c) in enumerate(parts, 1) if n > 1}
    rw, rh = sizes[ref][3:]
    bad = []
    for n, f in sizes.items():
        if f is None:
            print(f"{n}: no sign")
            bad.append(n)
            continue
        w, h = f[3:]
        off = abs(w / rw - 1) > tol or abs(h / rh - 1) > tol
        bad += [n] if off else []
        print(f"{n}: {w:.0f}x{h:.0f} sheet, {w * PAGE:.0f}x{h * PAGE:.0f} page, x{w / rw:.2f} wide x{h / rh:.2f} tall"
              f"{'  OFF' if off else ''}")
    print("all signs match" if not bad else "--redo " + ",".join(map(str, bad)))


def ends(f):
    """The two ends of the board's sign laid where codex's sign f is, page px."""
    c, d = np.array(f[:2]) * PAGE, SIGN_W / 2 * np.array([np.cos(f[2]), np.sin(f[2])])
    return c - d, c + d


def steps(srcs, order, most):
    frames = [c for s in srcs for _, _, c in split(np.asarray(Image.open(s).convert("RGBA")), True)[0]]
    f = [face(c) for c in frames]
    big = []
    for a, b in zip(order, order[1:] + order[:1]):
        if f[a] is None or f[b] is None:
            print(f"{a}>{b}: no sign")
            big.append((a, b))
            continue
        move = max(np.linalg.norm(p - q) for p, q in zip(ends(f[a]), ends(f[b])))
        big += [(a, b)] if move > most else []
        print(f"{a}>{b}: {move:.0f} px{'  BIG' if move > most else ''}")
    print("all steps small" if not big else f"{len(big)} big steps")


def _self_check():
    """A tilted cream sign with a pink border, a hand under it and hair below: the sign goes, the hand and hair stay."""
    c = np.zeros((512, 512, 4), np.uint8)
    yy, xx = np.mgrid[0:512, 0:512]
    a = 0.1
    u, v = (xx - 256) * np.cos(a) + (yy - 150) * np.sin(a), -(xx - 256) * np.sin(a) + (yy - 150) * np.cos(a)
    c[(np.abs(u) < 130 + BORDER - 2) & (np.abs(v) < 65 + BORDER - 2)] = (240, 160, 190, 255)
    c[(np.abs(u) < 130) & (np.abs(v) < 65)] = (250, 245, 235, 255)
    c[(np.abs(u - 60) < 12) & (np.abs(v - 72) < 14)] = (250, 210, 190, 255)  # her hand on the bottom edge
    c[260:300, 200:320] = (60, 40, 40, 255)  # her hair, below the sign
    c[(np.abs(u + 90) < 30) & (v > 66) & (v < 70)] = (250, 200, 185, 255)  # where the border meets the cream: skin's colour
    res, e = wipe_cell(c)
    assert e[3] == e[4] == "b" and abs(e[2] - a) < 0.02, e
    d = 65 + BORDER  # the cream's half height and the border: the outer edge
    assert abs(e[0] - (256 - d * np.sin(a))) < 3 and abs(e[1] - (150 + d * np.cos(a))) < 3, e
    assert res[150, 256, 3] == 0 and res[150, 380, 3] == 0  # the cream and the pink border went
    assert not res[..., 3][(np.abs(u + 90) < 30) & (v > 66) & (v < 70)].any()  # and the border's skin-coloured edge
    hand = (np.abs(u - 60) < 12) & (np.abs(v - 72) < 14)
    assert (res[..., 3][hand] > 0).mean() > 0.9 and (res[260:300, 200:320, 3] > 0).all()


_self_check()
if __name__ == "__main__":
    argv = sys.argv[1:]
    if argv[0] == "size":
        opt = lambda k, d: type(d)(argv[argv.index(k) + 1]) if k in argv else d
        size(argv[1], opt("--ref", 3), opt("--tol", 0.05))
    elif argv[0] == "wipe":
        wipe(argv[1], argv[2], "--strip" in argv)
    elif argv[0] == "steps":
        opt = lambda k, d: type(d)(argv[argv.index(k) + 1]) if k in argv else d
        steps(argv[1].split(","), [int(i) for i in argv[2].split(",")], opt("--most", 14.0))
    else:
        sys.exit(__doc__)
