"""Paste only the painted-out bandages from an edited image onto the original, so nothing else moves.

python bandage-patch.py <original> <edited> <out> <rows> <min-y-fraction> [x0,y0,x1,y1 ...]

The bandages are found by colour in the original (orange-tan: clearly more green than blue, unlike pink skin) and only
below <min-y-fraction> of each of <rows> equal bands (1 for a turnaround, 2 for a 3x2 pose sheet), where the knees are;
each spot is grown a little and taken from the edit. The boxes, in the original's pixels, are left untouched (for
example the cell where she is putting a bandage on). Prints the spots that still look like a bandage after the patch.
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

orig, edited, out, rows, min_y = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), float(sys.argv[5])
a = Image.open(orig).convert("RGBA")
b = Image.open(edited).convert("RGBA").resize(a.size, Image.LANCZOS)
A, B = np.asarray(a, dtype=np.float32), np.asarray(b, dtype=np.float32)


def bandage(img):
    r, g, bl, al = img[..., 0], img[..., 1], img[..., 2], img[..., 3]
    m = (al > 128) & (r > 195) & (r - bl > 60) & (g - bl > 28) & (g > 140) & (g < 225)
    band = img.shape[0] // rows
    ys = np.arange(img.shape[0])[:, None]
    m &= (ys % band) >= min_y * band
    for box in sys.argv[6:]:
        x0, y0, x1, y1 = map(int, box.split(","))
        m[y0:y1, x0:x1] = False
    return np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))) > 0


spots = Image.fromarray((bandage(A) * 255).astype(np.uint8))
k = np.asarray(spots.filter(ImageFilter.MaxFilter(15)).filter(ImageFilter.GaussianBlur(3)), dtype=np.float32) / 255
res = A.copy()
res[..., :3] = A[..., :3] * (1 - k[..., None]) + B[..., :3] * k[..., None]
Image.fromarray(res.round().astype(np.uint8)).save(out)
left = bandage(res)
print(f"took {int((k > 0.5).sum())} pixels from the edit; bandage-coloured pixels left: {int(left.sum())}")
