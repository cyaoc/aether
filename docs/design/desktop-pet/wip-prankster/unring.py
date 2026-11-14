"""unring.py <props dir>: the chest's two ring handles come off its drawings so the board can swing them.

chest.png / chest-open.png (the chest alone, at her scale) -> chest-plain.png / chest-open-plain.png, the same size with
the rings gone and the links they hang in left, and chest-ring.png, one ring cut from the closed chest's left side.

Both sides of both drawings are built alike, counted in from the side (k = 0 at the picture's edge): the ring's band at
k 2-5 and 17-20, its hole between, the chest's own outline showing through the hole from k B0 on; the link's lower end
in the rows E just above. Behind the ring the chest's edge is put back: its outline, and beside it the colour of the
same row just clear of the ring (k 23). The outline leans in toward the chest's foot as it does below the ring.
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

# rows of the link's lower end; rows of the ring below it; the outline's k through the hole; the ring's centre row
SHAPE = {"chest": (range(57, 61), range(61, 78), 12, 66), "chest-open": (range(137, 141), range(141, 158), 13, 146)}
FOOT, CLEAR = 16, 23          # the outline's k just under the ring; the first k clear of the ring
CX, R_OUT, R_HOLE, MID = 11.5, 11.2, 4.6, 8.0   # the ring: its centre from the side, outer edge, hole, middle of its band


def side(a, name):
    """One side, a seen with that side on the left."""
    link, below, b0, cy = SHAPE[name]
    line = a[cy - 2, b0 + 1].copy()
    a[link[0] - 1, :5] = 0
    for y in link:
        a[y, :8] = 0
        a[y, 15:CLEAR] = a[y, CLEAR]
    a[link[0], 5:8] = line   # the link's outer bar closed where the ring ran on from it
    for y in below:
        xo = b0 + round(max(0, y - cy - 1) * (FOOT - b0) / (below[-1] + 1 - cy - 1))
        a[y, :xo - 1] = 0
        a[y, xo - 1] = (*line[:3], 110)
        a[y, xo:xo + 2] = line
        a[y, xo + 2:CLEAR] = a[y, CLEAR]
    return a


def ring(a, cy):
    """The ring alone from the left side: its band's top, hidden in the link, mirrored up from its bottom."""
    n = 12
    p = np.pad(a, ((0, 0), (1, 0), (0, 0)))[cy - n:cy + n + 1, :2 * n + 1].astype(np.float32)  # it hangs to the picture's very edge
    ys, xs = np.mgrid[0:2 * n + 1, 0:2 * n + 1]
    d = np.hypot(xs - 1 - CX, ys - n)
    for y in range(n - 6):
        p[y] = p[2 * n - y]
    p[..., 3] = np.minimum(p[..., 3], 255 * np.clip(np.minimum(R_OUT - d + 0.5, d - R_HOLE + 0.5), 0, 1))
    return p.round().astype(np.uint8)


src = Path(sys.argv[1])
for name in SHAPE:
    a = np.asarray(Image.open(src / f"{name}.png").convert("RGBA")).copy()
    if name == "chest":
        Image.fromarray(ring(a, SHAPE[name][3])).save(src / "chest-ring.png")
    a = side(a, name)
    a = side(a[:, ::-1].copy(), name)[:, ::-1]
    Image.fromarray(a).save(src / f"{name}-plain.png")
    print(name, a.shape[1], "x", a.shape[0], "; a ring turns about k", CX, "row", SHAPE[name][3] - MID, "=", a.shape[0] - SHAPE[name][3] + MID, "px above its foot")
