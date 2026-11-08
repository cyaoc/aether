"""Paint her sprite into a character layer, with the soft pink-white glow the sprite always carries.

python place-sprite.py <character-layer> <sprite> <centre-x> <centre-y> <height> <out>

Positions are in the layer's pixels; the height is the sprite's visible height, from the flag tip to its lowest wisp.
"""
import sys
from PIL import Image, ImageFilter

layer_path, sprite_path, cx, cy, height, out = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), sys.argv[6]
layer = Image.open(layer_path).convert("RGBA")
sp = Image.open(sprite_path).convert("RGBA")
sp = sp.crop(sp.getchannel("A").getbbox())
sp = sp.resize((round(sp.width * height / sp.height), height), Image.LANCZOS)
radius = max(6, height // 11)
pad = radius * 3
glow_alpha = Image.new("L", (sp.width + 2 * pad, sp.height + 2 * pad), 0)
glow_alpha.paste(sp.getchannel("A"), (pad, pad))
glow_alpha = glow_alpha.filter(ImageFilter.GaussianBlur(radius)).point(lambda v: int(v * 0.55))
glow = Image.new("RGBA", glow_alpha.size, (255, 236, 246, 0))
glow.putalpha(glow_alpha)
layer.alpha_composite(glow, (cx - sp.width // 2 - pad, cy - sp.height // 2 - pad))
layer.alpha_composite(sp, (cx - sp.width // 2, cy - sp.height // 2))
layer.save(out)
