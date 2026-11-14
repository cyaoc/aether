"""despill.py <in> <out.png>: the green the key leaves at her edges, taken out. Nothing of her is green (hair, skin,
uniform, badge): wherever green is the strongest of the three it comes down to between red and blue; what is then left
olive (green near red, blue well below: the fringe of a frizzy strand, half see-through) becomes her hair's dark brown
outline. Skin, the white blouse, the red bow and the blue tears all fail the olive test and stay as they are."""
import sys
import numpy as np
from PIL import Image
a = np.asarray(Image.open(sys.argv[1]).convert("RGBA")).astype(np.float32)
r, g, b = a[..., 0], a[..., 1], a[..., 2]
over = (g > r) & (g > b)
a[..., 1] = g = np.where(over, np.minimum(g, (r + b) / 2 + 6), g)
olive = (a[..., 3] > 0) & (g >= 0.85 * r) & (b < 0.75 * g) & (np.maximum(r, g) > 45) & (r < 200)
a[olive, :3] = (58, 40, 36)
Image.fromarray(a.round().clip(0, 255).astype(np.uint8)).save(sys.argv[2])
print(sys.argv[2], int(over.sum()), "pressed,", int(olive.sum()), "olive to brown")
