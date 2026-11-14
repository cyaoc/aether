"""chestpic.py: the chest as the board draws it, for previews and for the canvases codex draws her against.
chest(props dir, open, left, right) -> RGBA image at her scale; left/right are the angles of the two rings in degrees from
hanging straight down (positive swings the ring outward, 90 is level and pulled away from the chest). The picture is
the plain chest's own size plus PAD on every side, so a level ring is not cut off."""
from pathlib import Path
from PIL import Image

PAD, PIVOT = 16, (11.5, 40.0)   # room for a ring pulled out; a ring's pivot from the chest's side and above its foot
RC, RP = (12.5, 12.0), (12.5, 4.0)  # in the ring's own picture: its centre, and the point it hangs from


def chest(props, open_=False, left=0, right=0):
    props = Path(props)
    body = Image.open(props / ("chest-open-plain.png" if open_ else "chest-plain.png")).convert("RGBA")
    ring = Image.open(props / "chest-ring.png").convert("RGBA")
    out = Image.new("RGBA", (body.width + 2 * PAD, body.height + 2 * PAD), (0, 0, 0, 0))
    out.alpha_composite(body, (PAD, PAD))
    for side, ang in ((-1, left), (1, right)):
        px = PAD + (PIVOT[0] if side < 0 else body.width - PIVOT[0])
        py = PAD + body.height - PIVOT[1]
        big = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
        big.alpha_composite(ring, (int(32 - RP[0]), int(32 - RP[1])))  # the hanging point at the big picture's middle
        # PIL turns counter-clockwise for positive angles: the left ring swings outward (to the left) clockwise on screen
        big = big.rotate(-ang * side * -1, resample=Image.BICUBIC, center=(32 - RP[0] % 1, 32 - RP[1] % 1))
        out.alpha_composite(big, (int(round(px - 32 + RP[0] % 1)), int(round(py - 32 + RP[1] % 1))))
    return out


if __name__ == "__main__":
    import sys
    props, dest = sys.argv[1], sys.argv[2]
    orig = [Image.open(Path(props) / n).convert("RGBA") for n in ("chest.png", "chest-open.png")]
    shots = [orig[0], chest(props), chest(props, False, 90, 90), chest(props, False, 180, 30), orig[1], chest(props, True), chest(props, True, 90, 90), chest(props, True, 60, 0)]
    w = sum(s.width + 12 for s in shots)
    for k, (bg, name) in enumerate((((24, 27, 40, 255), "dark"), ((226, 231, 238, 255), "light"))):
        page = Image.new("RGBA", (w, 230), bg)
        x = 6
        for s in shots:
            page.alpha_composite(s, (x, 220 - s.height - (0 if s.height in (98, 178) else -PAD)))
            x += s.width + 12
        page.convert("RGB").resize((w * 2, 460), Image.LANCZOS).save(f"{dest}-{name}.png")
    print(w * 2, 460)
