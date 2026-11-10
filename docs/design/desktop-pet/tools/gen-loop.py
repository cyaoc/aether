"""Draw the frame strip of a pose that moves in place (the board's STRIP), from its cell of the pose sheet.

python gen-loop.py <pose> <out dir> [--tries 3]

gen-frames.sh hands codex the cell as frame 1 of a 2 x 2 grid and codex draws frames 2-4 beside it; frame-patch.py
keeps only what moves (OPTIONS per pose). Every frame's face is then measured against the cell's own (petlib.find_face,
the two eyes together): a frame more than GATE bigger or smaller is drawn again, the whole loop, up to --tries times;
frames whose eyes cannot be found (closed, turned away) are not measured. A walking loop whose legs hardly change is drawn
again too (LEGS_MOVE). What lies on the ground beside her (a dumbbell, skis) is laid into every frame exactly as in her
cell (petlib.carry_props), so it does not shift from frame to frame. Nothing is scaled afterwards.

Out: <strip>-strip.png (the four frames in a row, each the cell's size, as the board plays them) and <strip>-check.png.
"""
import os
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))  # petlib sits beside the tools, also under python -I
from petlib import FACE_MATCH, PKG, POSE, SHEET, STRIP, bbox, carry_props, cells, eye_ref, find_face, loose, standing

T = PKG / "tools"
GATE = 0.06
# frame-patch.py options: the head kept where it does not move; frame 1 the drawing itself for a loop played in its place
# (a blink); codex's frame 1 as the base where its still parts do not meet the drawing (the bike); the legs always taken
# from the frames for walking (nothing of the drawing's feet left behind)
# (the cheer keeps no head: her raised hands sit beside her head, and keeping the drawing's head kept them too, so her hands
# never came down)
OPTIONS = {"wave": ["--head"], "clap": ["--head"], "cheer": ["upper"], "curl": ["--head"], "shakeBox": ["--head"],
           "govWave": ["--head"], "gymWipe": ["--head"], "capSalute": ["--head"], "admSalute": ["--head"],
           "spyglass": ["--head"], "bike": ["--head", "--from-first"], "idle": ["--ref-first"], "bored": ["--ref-first"],
           "hike": ["legs"], "walkA": ["legs"]}
# loops whose legs must stride: frame 2 passes between the strides, so at least LEGS_GATE[0] of the legs' outline changes
# from frame 1, and frame 3 is the other stride, LEGS_GATE[1] (codex drew the hiking legs almost the same in every frame,
# 0.07 in frame 3, then a frame 3 that changed less than the passing frames, 0.29, and the user still saw no step); the
# castle walk (feet under a gown) and pedalling change a smaller area and are not held to it
LEGS_MOVE, LEGS_GATE = {"hike"}, (0.25, 0.40)


def main():
    argv = sys.argv[1:]
    tries = 3
    if "--tries" in argv:
        k = argv.index("--tries")
        tries = int(argv[k + 1])
        del argv[k:k + 2]
    pose, out = argv[0], Path(argv[1]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    key, i = POSE[pose]
    strip, ref = STRIP[pose], f"textures/{SHEET[key]}.webp:{i + 1}"
    cell = cells(SHEET[key])[i]
    opts = []
    for o in OPTIONS.get(pose, []):
        x0, y0, x1, y1 = bbox(cell)
        if o == "legs":  # the lower 40% of her, across her whole width
            opts += ["--take", f"{x0 - 10},{int(y0 + 0.6 * (y1 - y0))},{x1 + 10},{y1 + 4}"]
        elif o == "upper":  # her upper 62%, arms and all, so no arm of the drawing's stays beside codex's (four arms)
            opts += ["--take", f"{x0 - 40},{max(0, y0 - 40)},{x1 + 40},{int(y0 + 0.62 * (y1 - y0))}"]
        else:
            opts.append(o)
    face = eye_ref(standing())
    m0, _, _, s0 = find_face(cell, face)
    py = sys.executable
    best = None
    for t in range(tries):
        name = f"{strip}-try{t + 1}"
        try:
            subprocess.run(["zsh", str(T / "gen-frames.sh"), str(out), name, ref, strip], check=True, cwd=PKG, env={**os.environ, "PYTHON": py}, capture_output=True, timeout=900)
            subprocess.run([py, "-I", str(T / "frame-patch.py"), *opts, ref, str(out / f"{name}-frames.png"), str(out / f"{name}.png"), str(out / f"{name}-check.png")], check=True, cwd=PKG, capture_output=True)
        except subprocess.CalledProcessError as e:
            print(f"try {t + 1}: {e.stderr.decode()[-300:] if e.stderr else 'failed'}")
            continue
        s = np.asarray(Image.open(out / f"{name}.png").convert("RGBA"))
        w = s.shape[1] // 4
        if loose(cell).any():  # what lies on the ground beside her stays exactly where it is in her cell, every frame
            s = np.hstack([carry_props(cell, s[:, k * w:(k + 1) * w]) for k in range(4)]).round().astype(np.uint8)
            Image.fromarray(s).save(out / f"{name}.png")
        sizes = []
        for k in range(4):
            m, _, _, sc = find_face(s[:, k * w:(k + 1) * w], face)
            sizes.append(sc / s0 if m >= FACE_MATCH and m0 >= FACE_MATCH else None)
        off = max([abs(v - 1) for v in sizes if v is not None], default=0)
        moved = None
        if pose in LEGS_MOVE:  # how much of her legs' outline differs from frame 1 in frames 2 and 3
            x0, y0, x1, y1 = bbox(cell)
            legs = [s[int(y0 + 0.6 * (y1 - y0)):y1 + 4, k * w + max(0, x0 - 10):k * w + x1 + 10, 3] > 40 for k in range(4)]
            change = [1 - (legs[0] & legs[k]).sum() / (legs[0] | legs[k]).sum() for k in (1, 2)]
            moved = min(c / g for c, g in zip(change, LEGS_GATE))  # 1 or more passes
            if moved < 1:
                off = max(off, 1)  # the legs did not stride: drawn again
        print(f"try {t + 1}: faces " + " ".join("-" if v is None else f"x{v:.2f}" for v in sizes) + ("" if moved is None else "; legs changed " + " ".join(f"{c:.2f}" for c in change)))
        if best is None or off < best[0]:
            best = (off, name)
        if off <= GATE:
            break
    if best is None:
        sys.exit(f"{pose}: no strip")
    for suffix, to in ((".png", "-strip.png"), ("-check.png", "-check.png")):
        Image.open(out / f"{best[1]}{suffix}").save(out / f"{strip}{to}")
    print(f"{pose}: {strip} kept {best[1]}, faces within x{1 + best[0]:.2f}" + ("" if best[0] <= GATE else " (still off: look by eye)"))


if __name__ == "__main__":
    main()
