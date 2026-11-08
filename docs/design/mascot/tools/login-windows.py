"""Window previews of a login illustration, like Avalonia UniformToFill: the GUI title (left 44, top 56), the version
(right 20, bottom 14) and the QR card (316x378, centered in the right half of the window) drawn on top.

python login-windows.py <art> <out>
"""
import sys
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
im = Image.open(src).convert("RGB")
bold = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 30, index=1)
small = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 12)
tiles = []
for w, h in [(1200, 720), (1600, 900), (1040, 600)]:
    s = max(w / im.width, h / im.height)
    big = im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS)
    y0, x0 = (big.height - h) // 2, (big.width - w) // 2
    win = big.crop((x0, y0, x0 + w, y0 + h)).convert("RGBA")
    layer = Image.new("RGBA", win.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.text((44, 50), "Aether", fill="white", font=bold)
    d.text((w - 20 - 40, h - 14 - 12), "v0.6.0", fill=(255, 255, 255, 180), font=small)
    cx, cy = w * 3 // 4, h // 2
    d.rounded_rectangle([cx - 158, cy - 189, cx + 158, cy + 189], 14, fill=(255, 255, 255, 245))
    d.rectangle([cx - 130, cy - 130, cx + 130, cy + 130], outline=(40, 40, 40), width=2)
    win = Image.alpha_composite(win, layer).convert("RGB")
    print(f"{w}x{h}: crops {y0 / s:.0f} px of the source off top and bottom")
    tiles.append(win.resize((w // 2, h // 2), Image.LANCZOS))
sheet = Image.new("RGB", (sum(t.width for t in tiles) + 40, max(t.height for t in tiles)), "white")
x = 0
for t in tiles:
    sheet.paste(t, (x, 0))
    x += t.width + 20
sheet.save(out)
