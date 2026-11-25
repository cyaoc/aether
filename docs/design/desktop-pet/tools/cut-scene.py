"""Take her out of the scenes gen-scene.py drew, for the props the page draws under her: the hot-pot table (it hides her
legs) and the sofa (she sits and lies on it). On the page the prop is drawn first and she over it, so what is hers is what
is not the prop: outside the prop everything codex drew; on the prop only what differs from the prop as it was pasted
and touches the rest of her (her hands, the chopsticks and the beef over the pot, her body on the sofa). Codex redraws
the prop a little off (the sofa's backrest 6-10 px low in its middle): a thin line on the prop that the prop moved by up
to SHIFT px would explain is its outline, not her (her body on the sofa is thick; the chopsticks over the soup are thin
but coloured like nothing near them on the table). What of her is
coloured like the prop under it (her pink phone on the pink sofa, white socks on its cream frame) is enclosed by her own
outline, so the holes in her are filled back; and along the prop's edge, where codex drew its outline a few px off, only
what has some thickness stays (a stray outline is a thin line; her hair crossing the edge is not). Behind the table
codex drew the table over her legs, so they are left out by themselves. Not for the 扭蛋 cabinet: codex redrew it all
(the capsules in its window moved), so it was cut by an erased picture (wip-gacha/cut-mask.py).

python tools/cut-scene.py <scene> <scene dir>   (run from docs/design/desktop-pet)

Out, in the scene dir: her.png (a 3 x 2 sheet, cell 1 the anchor; each scene cell her alone, moved whole px so that
where her standing drawing was pasted in it lands where the anchor stands in cell 1: the board and the tools line every
drawing of such a sheet up on that one spot, its FIXED sheets, so the prop under her stays put whatever she does) and
her-check.png (top: her alone on grey; bottom: the prop as the page draws it with her over it). It prints where the
prop then is in each cell: its left edge and bottom, the board's place for it from her spot.
"""
import importlib.util
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("gen_scene", TOOLS / "gen-scene.py")
gs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gs)
CELL, GROUND = gs.CELL, gs.GROUND
DIFF = 70  # how far a pixel on the prop must be from the pasted prop's colour to be hers (codex's redraw of it: ~20-40)
SHIFT = 10  # how far codex's redraw of the prop strays from where it was pasted
EDGE = 2  # the prop's outline grown this much: codex moves it by 1-2 px
BAND = 7  # how far off the prop codex's own outline of it strays (the sofa's backrest: 3-5 px)
HOLE = 15000  # the biggest hole in her filled back (her phone: ~4400 px); codex's outline of the whole sofa encloses ~100000
PIECE = 300  # px: a piece apart from her this big, unexplained by the prop, is hers too (a shoe: ~800)
LEGS = 45  # sitting on the sofa: up and down her legs, gaps this long between her knees and her shoes are her socks
NOTCH = 6  # notches in her outline up to twice this wide are filled (her hair over the sofa's dark backrest edge, a thumb on its pink)


def grow(m, px):
    return np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(2 * px + 1))) > 0


def shrink(m, px):
    return np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(2 * px + 1))) > 0


def bridge(m, gap):
    """m with every run of off px down a column, at most gap long and with m on above and below it, turned on."""
    out = m.copy()
    for x in np.nonzero(m.any(0))[0]:
        ys = np.nonzero(m[:, x])[0]
        for y, d in zip(ys[:-1], np.diff(ys)):
            if 1 < d <= gap:
                out[y + 1:y + d, x] = True
    return out


def holes(m):
    """What m encloses, up to HOLE px a hole: everything off m that cannot reach the frame's edge without crossing m."""
    im = Image.fromarray(np.pad(~m, 1, constant_values=True).astype(np.uint8) * 255)
    ImageDraw.floodfill(im, (0, 0), 128)
    h = (np.asarray(im) == 255)[1:-1, 1:-1]
    lab, n = pieces(h)
    sizes = np.bincount(lab.ravel(), minlength=n + 1)
    return h & (sizes[lab] <= HOLE)


def pieces(m):
    """Labels of m's 8-connected pieces (0: off), by a flood fill over the small picture."""
    lab = np.zeros(m.shape, np.int32)
    n = 0
    for y, x in zip(*np.nonzero(m)):
        if lab[y, x]:
            continue
        n += 1
        stack = [(y, x)]
        lab[y, x] = n
        while stack:
            cy, cx = stack.pop()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    ny, nx = cy + dy, cx + dx
                    if 0 <= ny < m.shape[0] and 0 <= nx < m.shape[1] and m[ny, nx] and not lab[ny, nx]:
                        lab[ny, nx] = n
                        stack.append((ny, nx))
    return lab, n


def placed(c):
    """The prop of gen-scene Cell c where it was pasted in its scene cell, on a cell of its own, and the row from which the
    seat's front hides her (None but lying on the sofa)."""
    P = np.zeros((CELL, CELL, 4), np.uint8)
    P[GROUND - c.prop.height:GROUND, c.left:c.left + c.prop.width] = np.asarray(c.prop.convert("RGBA"))
    return P, (None if c.lip is None else GROUND - c.prop.height + c.lip)


def cut(S, P, lip=None, legs=False):
    """Her alone out of a picture S (RGBA) codex drew her in with the prop P, P where it was pasted on a picture S's size;
    lip: the row from which the sofa's seat front hides her, lying on it (None otherwise); legs: on the sofa, where her legs
    may hang down its front. Also for in-betweens drawn with the prop (inbetween.py --prop)."""
    on = grow(P[..., 3] > 40, EDGE)
    pad = np.pad(P.astype(np.int16), ((SHIFT, SHIFT), (SHIFT, SHIFT), (0, 0)))
    diff = np.full(S.shape[:2], 255, np.int16)
    for dy in range(-SHIFT, SHIFT + 1):
        for dx in range(-SHIFT, SHIFT + 1):  # the prop moved by (dx, dy): the closest it comes to each pixel's colour
            q = pad[SHIFT + dy:SHIFT + dy + S.shape[0], SHIFT + dx:SHIFT + dx + S.shape[1]]
            d = np.abs(S[..., :3].astype(np.int16) - q[..., :3]).max(-1)
            diff = np.minimum(diff, np.where(q[..., 3] > 40, d, 255))
    plain = np.abs(S[..., :3].astype(np.int16) - P[..., :3].astype(np.int16)).max(-1)
    mine = (S[..., 3] > 40) & (~on | (plain > DIFF))
    if lip is not None:  # lying on the sofa: its seat's front is in front of her (and codex's own, a little off, goes too)
        mine[lip:] &= ~on[lip:]
    band = grow(P[..., 3] > 40, BAND) & ~on
    thick = grow(shrink(mine, 2), 2)
    r_, g_, b_ = (S[..., j].astype(int) for j in range(3))
    gold = (r_ > 200) & (g_ > 140) & (b_ < 120) & (r_ - b_ > 110)  # the props' gold rims; she wears no gold
    mine &= ~band | thick & ~gold  # by the prop's edge only what is thicker than a stray outline, and none of its gold
    mine &= ~(on & (~thick | gold) & (diff <= DIFF))  # on it, no thin line nor gold the prop moved a little would explain (her
    # phone's gold ring, near the sofa's rim, goes too, and comes back as a hole she encloses)
    # she is the biggest piece once small gaps are sealed (lying on the sofa her hair across its dark outline leaves a gap),
    # and any other piece of PIECE px or more that the prop moved a little would not explain (her shoes, cut off from her
    # where her white socks cross the sofa's cream frame: dropped, her feet went); the rest is codex's own outline of the
    # prop, drawn a little off
    lab, n = pieces(shrink(grow(mine, 3), 3))
    if n:
        size = np.bincount(lab[mine], minlength=n + 1)
        odd = np.bincount(lab[mine], weights=(diff > DIFF)[mine], minlength=n + 1) / np.maximum(size, 1)
        ok = (size >= PIECE) & (odd >= 0.5)
        ok[np.argmax(size)] = True
        ok[0] = False
        keep = mine & ok[lab]
    else:
        keep = mine
    # gaps in her sealed (her hair across the sofa's dark outline), then what she encloses (her phone on the sofa); the band
    # left out, so codex's outline round the whole prop never encloses it
    if legs:  # her white socks on the sofa's cream frame, their thin outlines taken for its own: knees to shoes, filled
        keep |= bridge(keep, LEGS) & on & (S[..., 3] > 40)
    sealed = shrink(grow(keep, 3), 3) & ~band
    keep |= (sealed | holes(sealed)) & ~band & (S[..., 3] > 40)
    # notches in her outline (her dark hair over the sofa's dark edge, her pink phone on its pink) filled: what fills them is
    # only ever the prop as codex drew it, laid over the prop
    keep |= shrink(grow(keep, NOTCH), NOTCH) & ~band & (S[..., 3] > 40)
    if lip is not None:  # what the sealing put back under the seat's front goes again
        keep[lip:] &= ~on[lip:]
    out = S.copy()
    out[..., 3] = np.where(keep, S[..., 3], 0)
    return out


def main():
    name, d = sys.argv[1], Path(sys.argv[2])
    cells = gs.SCENES[name][2]
    S = np.asarray(Image.open(d / "scene.png").convert("RGBA"))
    her = np.zeros_like(S)
    her[:CELL, :CELL] = S[:CELL, :CELL]
    A = gs.standing()
    check = Image.new("RGBA", (3 * CELL, 2 * CELL), (96, 100, 112, 255))
    for i, c in cells.items():
        x, y = gs.cell_xy(i)
        P, lip = placed(c)
        h = cut(S[y:y + CELL, x:x + CELL], P, lip, legs=c.prop is gs.SOFA)
        dx, dy = (CELL - A.shape[1]) // 2 - gs.her_x(A, c), c.drop  # her pasted spot onto the anchor's
        her[y:y + CELL, x:x + CELL] = np.roll(h, (dy, dx), (0, 1))
        print(f"  {c.pose}: moved {dx:+d}, {dy:+d}; the prop's left edge {c.left + dx}, bottom {GROUND + dy}")
        top = Image.new("RGBA", (CELL, CELL), (96, 100, 112, 255))
        top.alpha_composite(Image.fromarray(h))
        page = Image.new("RGBA", (CELL, CELL), (96, 100, 112, 255))
        page.alpha_composite(Image.fromarray(P))
        page.alpha_composite(Image.fromarray(h))
        check.paste(top.resize((CELL // 2, CELL // 2), Image.LANCZOS), ((i - 1) * CELL // 2, 0))
        check.paste(page.resize((CELL // 2, CELL // 2), Image.LANCZOS), ((i - 1) * CELL // 2, CELL // 2))
    Image.fromarray(her).save(d / "her.png")
    check.crop((0, 0, len(cells) * CELL // 2, CELL)).convert("RGB").save(d / "her-check.png")
    print(f"{name}: {len(cells)} cells cut")


if __name__ == "__main__":
    main()
