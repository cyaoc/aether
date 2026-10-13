"""Export the approved SVG; tooling dependencies: cairosvg and pillow."""

from io import BytesIO
from pathlib import Path
import struct

import cairosvg
from PIL import Image, ImageDraw


root = Path(__file__).resolve().parent
sizes = (16, 24, 32, 48, 64, 128, 256, 512, 1024)
frames = {}
for size in sizes:
    png = cairosvg.svg2png(url=str(root / "aether.svg"), output_width=size, output_height=size)
    (root / f"aether-{size}.png").write_bytes(png)
    frames[size] = Image.open(BytesIO(png)).convert("RGBA")

# Each ICO frame is rendered from the vector master, not a resized bitmap.
ico_sizes = sizes[:7]
payloads = [(root / f"aether-{size}.png").read_bytes() for size in ico_sizes]
offset = 6 + 16 * len(ico_sizes)
entries = []
for size, payload in zip(ico_sizes, payloads):
    entries.append(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(payload), offset))
    offset += len(payload)
(root / "aether.ico").write_bytes(
    struct.pack("<HHH", 0, 1, len(entries)) + b"".join(entries) + b"".join(payloads)
)
icns_sizes = {b"icp4": 16, b"icp5": 32, b"ic07": 128,
              b"ic08": 256, b"ic09": 512, b"ic10": 1024,
              b"ic11": 32, b"ic12": 64, b"ic13": 256, b"ic14": 512}
chunks = []
for kind, size in icns_sizes.items():
    payload = (root / f"aether-{size}.png").read_bytes()
    chunks.append(kind + struct.pack(">I", len(payload) + 8) + payload)
body = b"".join(chunks)
(root / "aether.icns").write_bytes(b"icns" + struct.pack(">I", len(body) + 8) + body)

preview = Image.new("RGB", (720, 370))
draw = ImageDraw.Draw(preview)
for left, background, foreground in ((0, "#F4F4F5", "#18181B"), (360, "#18181B", "#F4F4F5")):
    draw.rectangle((left, 0, left + 359, 369), fill=background)
    preview.paste(frames[256], (left + 52, 10), frames[256])
    x = left + 28
    for size in (16, 24, 32, 48, 64):
        preview.paste(frames[size], (x, 286 + (64 - size) // 2), frames[size])
        draw.text((x, 353), str(size), fill=foreground)
        x += size + 18
preview.save(root / "preview.png")

# Flat interiors and empty margins guard against the rejected texture/stray pixels.
for size, frame in frames.items():
    assert frame.size == (size, size)
    assert frame.getpixel((0, 0))[3] == 0
    assert frame.getpixel((size // 2, size // 8)) == (66, 85, 255, 255)
    assert frame.getpixel((size // 2, size // 3)) == (255, 255, 255, 255)
master = frames[1024]
assert master.crop((100, 100, 924, 200)).getcolors() == [(82400, (66, 85, 255, 255))]
assert all(master.getpixel((x, y))[3] == 0 for x, y in
           [(i, 0) for i in range(1024)] + [(0, i) for i in range(1024)] +
           [(i, 1023) for i in range(1024)] + [(1023, i) for i in range(1024)])
with Image.open(root / "aether.ico") as icon:
    assert icon.ico.sizes() == {(s, s) for s in ico_sizes}
    for size in ico_sizes:
        assert icon.ico.getimage((size, size)).convert("RGBA").tobytes() == frames[size].tobytes()
with Image.open(root / "aether.icns") as icon:
    assert len(icon.info["sizes"]) == len(icns_sizes)
    for width, height, scale in icon.info["sizes"]:
        assert icon.icns.getimage((width, height, scale)).convert("RGBA").tobytes() == frames[width * scale].tobytes()
    icon.load()
    assert icon.size == (1024, 1024)
    assert icon.convert("RGBA").tobytes() == master.tobytes()
print("Exported SVG-derived PNG / ICO / ICNS; size, alpha, flat-fill and decode checks passed.")
