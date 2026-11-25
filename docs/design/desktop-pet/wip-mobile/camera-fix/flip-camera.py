"""Put the phone's camera where the phone's own path through the sofa scenes puts it (2026-11-25, the user: "手机砸脸和举着
欢呼的摄像头方向是不是反了、其他图的摄像头在右手、但这两在左手", then "你看现在的手机轨迹……她的手机拿的方向就可能不对、建议你
拿了手机实际的轨迹查看").

Followed as a real phone moves (the viewer sees its back): sitting, it is upright and its camera at the top-left, by her
right hand (掏手机, 坐沙发). To play she turns its top to the viewer's left, into her right hand: the camera comes to the
LEFT end's LOWER corner, under that hand's fingers (打游戏, 愣住; codex drew it at the upper corner, which only a mirrored
phone has). Lost, her hands go slack and it drops flat on her face as it was: left end, lower corner, in sight (砸脸;
codex drew the right end). Won, she lifts it in her right hand by that end, which turns it upright upside down: the camera
at the bottom, by her thumb (赢了; codex drew the top). It slides onto her tummy: left end, lower corner (捂鼻子, as codex
drew it, but with a second camera at its other end). Codex also put an orange dot like a flash at the right end while she
plays (打游戏, 愣住); a flash sits by the camera, so it goes too.

The phones' corners and lenses were read off a 5x zoom of each cell (sofa pink is as pink as the case, so they are not
found by colour). A lens taken away is covered by plain case from the phone's middle, shaded to the case round it, out to
FILL px; within EDGE px of the phone's outline only the ring's gold goes, and her skin never. A lens added is the cell's
own lens moved whole px (reflected and resampled, the ring came out ragged), drawn only over the case (her thumb stays on
top). Each with a 1 px soft edge.

python wip-mobile/camera-fix/flip-camera.py   (run from docs/design/desktop-pet; reads camera-fix/scene-before.png,
writes wip-mobile/scene/scene.png)
"""
import numpy as np
from PIL import Image

CELL, R, FILL, EDGE = 512, 6.5, 9, 1.5  # the ring is ~5 px in radius, ~7 with its outline; 5-8 px from the phone's edges
# cell index: the phone's corners (top-left, top-right, bottom-right, bottom-left as drawn, in the cell's px; a corner
# under her hand is where its edges meet), the lenses to take away, a plain spot of case, where the lens goes (None: under
# her hand, not drawn)
PHONES = {1: ([(225, 301), (276, 319), (270, 352), (219, 334)], [(231, 309), (266, 320)], (250, 326), None),  # 打游戏 (and its flash)
          2: ([(194, 241), (228, 248), (210, 294), (183, 288)], [(201, 249)], (203, 268), (206, 286)),  # 赢了
          3: ([(239, 301), (290, 319), (284, 352), (233, 334)], [(244, 310), (280, 321)], (262, 326), None),  # 愣住 (and its flash)
          4: ([(277, 279), (338, 304), (324, 338), (262, 311)], [(328, 308)], (300, 308), (272, 308)),  # 砸脸
          5: ([(216, 359), (285, 361), (287, 395), (219, 393)], [(262, 366)], (240, 376), None)}  # 捂鼻子: its second camera

sc = np.asarray(Image.open("wip-mobile/camera-fix/scene-before.png").convert("RGBA")).copy()
yy, xx = np.mgrid[0:CELL, 0:CELL]


def disc(at, r):
    return np.clip(r + 0.5 - np.hypot(xx - at[0], yy - at[1]), 0, 1)


for i, (corners, gone, plain, new) in PHONES.items():
    x0, y0 = (i % 3) * CELL, (i // 3) * CELL
    c = sc[y0:y0 + CELL, x0:x0 + CELL].copy()
    f = c.astype(float)
    r_, g_, b_ = (c[..., j].astype(int) for j in range(3))
    case = (r_ > 200) & (g_ > 90) & (g_ < 200) & (b_ > 110) & (b_ < 215) & (r_ - g_ > 45)
    gold = (r_ > 150) & (g_ > 90) & (r_ - b_ > 80) & (g_ - b_ > 30)
    skin = (r_ > 225) & (g_ > 170) & (b_ > 140) & (r_ - b_ < 90) & ~case
    k = np.array(corners, float)
    edge = np.full(xx.shape, np.inf)  # how far each px is from the phone's outline (its four sides)
    for a, b in zip(k, np.roll(k, -1, 0)):
        ab = b - a
        t = np.clip(((xx - a[0]) * ab[0] + (yy - a[1]) * ab[1]) / (ab @ ab), 0, 1)
        edge = np.minimum(edge, np.hypot(xx - a[0] - t * ab[0], yy - a[1] - t * ab[1]))

    def shade(at):  # the case's colour in a ring round a spot
        d = np.hypot(xx - at[0], yy - at[1])
        return np.median(f[(d > R + 0.5) & (d < R + 4) & case], 0)

    def moved(frm, to):  # the picture moved whole px so that frm lands on to, shaded to the case at to
        m = np.roll(f, (to[1] - frm[1], to[0] - frm[0]), (0, 1))
        m[..., :3] += (shade(to) - shade(frm))[:3]
        return np.clip(m, 0, 255)

    out = f.copy()
    for lens in gone:
        w = disc(lens, FILL) * np.maximum(np.clip(edge - EDGE, 0, 1), gold) * ~skin
        out = out * (1 - w[..., None]) + moved(plain, lens) * w[..., None]
    if new is not None:
        w = disc(new, R) * case  # over the case only: her thumb stays on top
        out = out * (1 - w[..., None]) + moved(gone[0], new) * w[..., None]
    print(f"cell {i + 1}: lens {gone} -> {new or 'under her hand'}")
    sc[y0:y0 + CELL, x0:x0 + CELL] = out.round().astype(np.uint8)
Image.fromarray(sc).save("wip-mobile/scene/scene.png")
