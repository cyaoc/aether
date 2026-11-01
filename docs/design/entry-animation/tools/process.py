# Turns codex output into board textures (needs Pillow and NumPy):
#   python3 process.py cutout <in.png> <out.webp> [height]      crop to the opaque area, scale to height (default 1200)
#   python3 process.py glow <in.png> <out.webp> [size]          white-on-black art -> white + alpha, for masks and tinting
#   python3 process.py fade-right <in.webp> <out.webp> [px]     fade the right edge where a figure touches the frame
#   python3 process.py photo <in.png> <out.webp>                backgrounds, no alpha
import sys
import numpy as np
from PIL import Image

mode, src, dst, *rest = sys.argv[1:]
if mode == 'cutout':
    im = Image.open(src).convert('RGBA')
    ys, xs = np.nonzero(np.asarray(im)[..., 3] > 12)
    im = im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
    h = int(rest[0]) if rest else 1200
    im = im.resize((round(im.width * h / im.height), h), Image.LANCZOS)
    im.save(dst, quality=90, method=6)
    print(im.size)
elif mode == 'glow':
    im = Image.open(src).convert('RGB')
    if rest: im = im.resize((int(rest[0]), int(rest[0])), Image.LANCZOS)
    a = np.clip((np.asarray(im, dtype=np.float32).max(axis=2) - 8) / 247, 0, 1) * 255
    Image.fromarray(np.dstack([np.full_like(a, 255)] * 3 + [a]).astype(np.uint8)).save(dst, quality=86, method=6)
elif mode == 'fade-right':
    a = np.asarray(Image.open(src).convert('RGBA')).copy()
    w, px = a.shape[1], float(rest[0]) if rest else 90.0
    a[..., 3] = (a[..., 3] * np.clip((w - 1 - np.arange(w)) / px, 0, 1)[None, :]).astype(np.uint8)
    Image.fromarray(a).save(dst, quality=90, method=6)
elif mode == 'photo':
    Image.open(src).convert('RGB').save(dst, quality=82, method=6)
else:
    sys.exit('unknown mode: ' + mode)
