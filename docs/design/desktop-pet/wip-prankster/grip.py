"""grip.py <tools dir> <props dir> canvas <n> <out.png>                 canvas n: her anchor and the chests she is drawn holding
grip.py <tools dir> <props dir> cut <sheet.png> <check.png> <n>=<codex.png> ...   her sheet 16 from codex's pictures of the
                                                                                 canvases named; a later one's cells win

Her drawings that hold the chest's ring handle are drawn WITH the chest: it stands in the canvas at her scale, the ring
on its left swung out as far as that pose pulls it (SCENES), and codex draws her beside it with her fists on the ring. So
her fists come out at the ring's height. Each scene is then cut CUT px in from the picture's side of the chest (found
again in codex's picture by its lock plate), past the chest's link, with codex's own chest taken off (the plain chest's
shape there, a little grown): what is left is her and the whole ring codex drew in her fists, running into the link.
It goes into its cell of an ordinary 3 x 2 sheet with that cut at the cell's column EDGE: in every cell the chest's link
is EDGE - CUT + 11.5 (the board's GRIP). The board draws its chest over her, link and all.
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

tools, props = Path(sys.argv[1]), Path(sys.argv[2])
sys.path.insert(0, str(tools))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import importlib.util
spec = importlib.util.spec_from_file_location("gen_sheet", tools / "gen-sheet.py")
gs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gs)
from petlib import drop_strays, eye_ref, key, standing
from chestpic import PAD, chest

W, H, CELL, GROUND = 1536, 1024, gs.CELL, gs.GROUND
CUT, EDGE = 16, 500
# canvas: [(pose, its cell on the sheet, the scene's box, chest open?, the left ring's swing in degrees, the chest's left side x)]
SCENES = {1: [("lunge", 1, (512, 0, 1536, 512), False, 70, 1100), ("tug", 2, (0, 512, 768, 1024), True, 90, 500), ("tugZap", 3, (768, 512, 1536, 1024), True, 90, 1268)],
          2: [("dragged", 4, (512, 0, 1536, 512), True, 40, 1100), ("draggedZap", 5, (0, 512, 768, 1024), True, 40, 520)],
          # the pull after the lightning again, narrower (the first, 426 px wide, does not fit the page beside the chest and
          # the prankster): 3 leaves her only the room between the canvas's edge and the chest, 4 only asks for it
          3: [("tugZap", 3, (0, 512, 768, 1024), True, 90, 352)], 4: [("tugZap", 3, (0, 512, 768, 1024), True, 90, 500)],
          # after the lightning she pulls harder, teeth clenched, not dazed (the user: 桌宠被电以后样子有点蠢、应该更加努力的往回拉的表情)
          5: [("tugZap", 3, (0, 512, 768, 1024), True, 90, 500)], 6: [("draggedZap", 5, (0, 512, 768, 1024), True, 40, 520)],
          # 6 again: there her fists stopped short of the ring and she lay 10 px above the chest's foot
          7: [("draggedZap", 5, (0, 512, 768, 1024), True, 40, 520)], 8: [("draggedZap", 5, (0, 512, 768, 1024), True, 40, 520)],
          # 5 again: there her fists held a thin bar short of the ring
          9: [("tugZap", 3, (0, 512, 768, 1024), True, 90, 500)], 10: [("tugZap", 3, (0, 512, 768, 1024), True, 90, 500)]}


def canvas(n):
    sheet = gs.place_anchor(np.zeros((H, W, 4), np.uint8), standing())
    im = Image.fromarray(sheet)
    for _, _, (x0, y0, x1, y1), open_, swing, left in SCENES[n]:
        c = chest(props, open_, swing, 0)
        im.alpha_composite(c, (left - PAD, y0 + GROUND - (c.height - 2 * PAD) - PAD))
    return gs.on_green(np.asarray(im))


def find(pic, can, left, top):
    """Where the chest's lock plate (a patch of the canvas at left, top) is in codex's picture: its shift dx, dy."""
    x, y, w, h = left + 55, top + 30, 50, 44
    t = np.asarray(can.crop((x, y, x + w, y + h)), np.float32)
    p = np.asarray(pic, np.float32)
    best = None
    for dy in range(-16, 17):
        for dx in range(-16, 17):
            d = np.abs(p[y + dy:y + dy + h, x + dx:x + dx + w] - t).mean()
            if best is None or d < best[0]:
                best = (d, dx, dy)
    return best


if sys.argv[3] == "canvas":
    canvas(int(sys.argv[4])).save(sys.argv[5])
    print(sys.argv[5])
else:
    A = standing()
    ref = eye_ref(A)
    sheet = np.zeros((H, W, 4), np.uint8)
    for n, path in ((int(a.split("=")[0]), a.split("=")[1]) for a in sys.argv[6:]):
        pic = Image.open(path).convert("RGB")
        print(f"canvas {n}: codex size", pic.size)
        pic = pic.resize((W, H), Image.LANCZOS)  # the whole picture back to the canvas size, nothing scaled on its own
        can = canvas(n)
        a = key(pic)
        for pose, cell, (x0, y0, x1, y1), open_, swing, left in SCENES[n]:
            body_h = 178 if open_ else 98
            miss, dx, dy = find(pic, can, left, y0 + GROUND - body_h + (80 if open_ else 0))
            cut = left + dx + CUT
            body = np.asarray(Image.open(props / ("chest-open-plain.png" if open_ else "chest-plain.png")))[..., 3] > 0
            m = np.zeros(a.shape[:2], bool)
            by = y0 + GROUND - body_h + dy
            m[by:by + body.shape[0], left + dx:left + dx + body.shape[1]] = body
            for _ in range(2):
                m = m | np.roll(m, 1, 0) | np.roll(m, -1, 0) | np.roll(m, 1, 1) | np.roll(m, -1, 1)
            # but not the ring, where it runs through the link: the board's chest covers it there, and taking it off left
            # a gap between it and the link (the ring's middle is 8 px out from its pivot, swung out by swing)
            r = np.radians(swing)
            rx, ry = left + dx + 11.5 - 8 * np.sin(r), y0 + GROUND + dy - 40 + 8 * np.cos(r)
            yy, xx = np.mgrid[0:m.shape[0], 0:m.shape[1]]
            m &= np.hypot(xx - rx, yy - ry) > 14
            b = a.copy()
            b[m] = 0
            part = b[y0:y1, max(x0, cut - EDGE):cut]
            c = np.zeros((CELL, CELL, 4), np.float32)
            c[:, EDGE - part.shape[1]:EDGE] = part
            c = drop_strays(c).round().astype(np.uint8)
            ys, xs = np.nonzero(c[..., 3] > 40)
            # codex's chest foot onto the ground line, so the ring she holds meets the board's link; only what would then reach
            # below the ground is lifted onto it (she may lie a few px over the ground: the ring on the link matters more)
            c = np.roll(c, -dy, axis=0)
            if c[GROUND + 1:, :, 3].max() > 40:
                c = gs.to_ground(c)
            g = gs.measure(c, ref, None)
            low = np.nonzero(b[y0:y1, max(x0, cut - EDGE):cut, 3].max(1) > 40)[0].max() + y0
            print(f"{pose}: the chest moved {dx:+d},{dy:+d} (miss {miss:.1f}); her drawing {xs.max() - xs.min() + 1} px wide, its right end {EDGE - 1 - xs.max()} px short of the cut,"
                  f" left margin {xs.min()} px; her lowest point {low - (y0 + GROUND + dy):+d} px from codex's chest foot",
                  "| eyes found" if g["eyes"] else "| eyes not found (look by eye)", f"face x{g['scale']:.3f}" if g["eyes"] else "", " ".join(g["fails"]) or "ok")
            cx, cy = gs.canvas_cell(cell)
            sheet[cy:cy + CELL, cx:cx + CELL] = c
    sheet = gs.place_anchor(sheet, A)
    Image.fromarray(sheet).save(sys.argv[4])
    page = Image.new("RGBA", (W, H), (150, 150, 150, 255))
    page.alpha_composite(Image.fromarray(sheet))
    page.convert("RGB").save(sys.argv[5])
