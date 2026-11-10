"""How far her body sits from where the everyday idle pose has it, as the board places each pose.

python body-align.py [<pose> ...]   (default: the first and last pose of every sport)

The board lines a drawing up on its centre of mass, and a bike, the dumbbells or the skis in the drawing pull that centre
aside, so her body would jump when the sprite changes her clothes and they vanish. This slides each pose's drawing (the
things lying apart from her left out) sideways over the idle pose and keeps the shift where the two overlap most. For each
pose it prints the offset as the board places it now (0 when the board's BODY is right) and without BODY (the number BODY
takes, negated: BODY moves the drawing right by its value).
"""
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))  # petlib sits beside the tools, also under python -I
from petlib import body_shift, pose, warp

W, H, FX, FY = 480, 330, 240, 320  # a strip of the page around her, her feet at (FX, FY)
SPORTS = ["gymReady", "gymWipe", "hikeReady", "hikeUp", "bikeReady", "bikeUp", "skiReady", "skiShake"]


def placed(name, body):
    a, s, g, x = pose(name, body)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    return warp(a, x + (xx - FX) / s, g + (yy - FY) / s)


def offset(name, body=True):
    return body_shift(placed(name, body), placed("idle", True))


for n in sys.argv[1:] or SPORTS:
    print(f"{n:12s} now {offset(n):+d}   without BODY {offset(n, False):+d}")
