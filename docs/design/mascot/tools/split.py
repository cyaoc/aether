"""Split a launch art into a background layer and a character layer that stacks back over it.

python split.py <art.png> <plate.png> <green.png> <horizon-y> <out-dir>

- plate: the same art with the character removed (clean-plate.sh); it fills where she and her halo were.
- green: the same art with everything but the character flooded #00FF00 (greenscreen.sh); its key is her alpha.
  A segmentation model (isnet-anime) breaks thin loose hair strands; the green key keeps them whole.
- Her colors come from the art itself, unblended from the halo-lit background, so no green spill reaches them.
- The soft glow right behind her (low-frequency light the art has over the plate, above the horizon) is hers: it goes
  into the character layer and travels with her when the background changes.
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

art, plate, green, horizon, out = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), sys.argv[5]


def f32(img):
    return np.asarray(img, dtype=np.float32)


def blur(a, r):
    return f32(Image.fromarray(a.round().clip(0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)))


def grow(mask, px, r):
    img = Image.fromarray((mask * 255).astype(np.uint8))
    for _ in range(px // 20):
        img = img.filter(ImageFilter.MaxFilter(41))
    return f32(img.filter(ImageFilter.GaussianBlur(r))) / 255


def unblend(top, under, a):
    return np.clip((top - (1 - a[..., None]) * under) / np.maximum(a[..., None], 1e-3), 0, 255)


I = f32(Image.open(art).convert("RGB"))
P = f32(Image.open(plate).convert("RGB"))
G = f32(Image.open(green).convert("RGB"))
assert I.shape == P.shape == G.shape, "art, plate and green must be the same size"

spill = G[..., 1] - np.maximum(G[..., 0], G[..., 2])  # how much greener than red and blue
a_g = 1 - np.clip((spill - 30) / (200 - 30), 0, 1)
a_g = np.where(a_g < 0.02, 0, a_g)

zone = grow((a_g > 0.5).astype(np.float32), 180, 40)  # her and her halo
B = I * (1 - zone[..., None]) + P * zone[..., None]   # the art itself away from her

sky = np.clip((horizon - np.arange(I.shape[0], dtype=np.float32)) / 60, 0, 1)[:, None]
Ib, Bb = blur(I, 18), blur(B, 18)
a_h = np.where(Ib > Bb, (Ib - Bb) / np.maximum(255 - Bb, 1), 0).max(axis=2) * zone * sky
a_h = np.where(a_h < 0.04, 0, a_h)
F_h = unblend(Ib, Bb, a_h)

Bh = F_h * a_h[..., None] + B * (1 - a_h[..., None])
F_g = unblend(I, Bh, a_g)
a = a_g + a_h * (1 - a_g)
F = (F_g * a_g[..., None] + F_h * (a_h * (1 - a_g))[..., None]) / np.maximum(a[..., None], 1e-3)

Image.fromarray(B.round().astype(np.uint8)).save(f"{out}/background.png")
Image.fromarray(np.dstack([F, a * 255]).round().clip(0, 255).astype(np.uint8)).save(f"{out}/character.png")
R = F * a[..., None] + B * (1 - a[..., None])
d = np.abs(R - I).max(axis=2)
print("stacked back vs art: pixels off by more than 24: %.2f%%" % ((d > 24).mean() * 100))
