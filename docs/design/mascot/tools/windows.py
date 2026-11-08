import sys
from PIL import Image, ImageDraw, ImageFont
# window previews like Avalonia UniformToFill: scale to cover, crop top and bottom, then the GUI title (left 44, top 56, 30 bold)
# and the update progress column (left 44, top 110, width 360) drawn on top
src, out = sys.argv[1], sys.argv[2]
im = Image.open(src).convert("RGB")
bold = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 30, index=1)
small = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 13)
tiles = []
for w, h in [(1200, 720), (1600, 900), (1040, 600)]:
    s = max(w / im.width, h / im.height); sw, sh = round(im.width * s), round(im.height * s)
    big = im.resize((sw, sh), Image.LANCZOS); y0 = (sh - h) // 2; x0 = (sw - w) // 2
    win = big.crop((x0, y0, x0 + w, y0 + h)); d = ImageDraw.Draw(win)
    d.text((44, 56 - 6), "Aether", fill="white", font=bold)
    d.text((44, 110), "Downloading v0.6.0  12.3 / 48.0 MB", fill="white", font=small)
    d.rounded_rectangle([44, 132, 404, 138], 3, fill=(255, 255, 255, 60), outline=None); d.rounded_rectangle([44, 132, 44 + 92, 138], 3, fill=(255, 255, 255))
    print(f"{w}x{h}: crops {y0 / s:.0f} px of the source off top and bottom")
    tiles.append(win.resize((w // 2, h // 2), Image.LANCZOS))
W = sum(t.width for t in tiles) + 20 * 2; H = max(t.height for t in tiles)
sheet = Image.new("RGB", (W, H), "white"); x = 0
for t in tiles: sheet.paste(t, (x, 0)); x += t.width + 20
sheet.save(out)
