"""Distant desert mountains for the town's horizon, from the Rowlan heightmap stamps.

Each chosen 4096^2 16-bit stamp is downsampled to N x N, multiplied by a radial falloff so the massif rises out
of the plain instead of ending in a cliff at its square edge, and written as little-endian uint16 to
Assets/FPSKit_Generated/Town/Heights/<name>.bytes (a TextAsset the builder meshes; no collider, backdrop layer).
Run once; the outputs are committed, the 1.3 GB stamp pack is not needed again.

Usage: python3 -I Tools/town/make_backdrop.py
"""
import os, struct, sys
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Assets/Rowlan/Terrain/Stamps/Examples/Heightmaps")
OUT = os.path.join(ROOT, "Assets/FPSKit_Generated/Town/Heights")
N = 161
PICK = ["Monument Desert 02", "Terraced Cliffs 06", "Layered Rocks 15", "Massive Blocks 02",
        "Rocky Plateaus 01", "Canyon 18", "Pride Rocks 02"]

os.makedirs(OUT, exist_ok=True)
for name in PICK:
    im = Image.open(os.path.join(SRC, name + ".png"))
    if im.mode != "I;16":
        im = im.convert("L").point(lambda v: v * 257).convert("I")
    im = im.convert("I").resize((N, N), Image.BILINEAR)
    px = im.load()
    vals = []
    for y in range(N):
        for x in range(N):
            dx = (x / (N - 1)) * 2 - 1; dy = (y / (N - 1)) * 2 - 1
            r = (dx * dx + dy * dy) ** 0.5
            t = min(1.0, max(0.0, (r - 0.62) / 0.36))
            fall = 1.0 - t * t * (3 - 2 * t)
            v = max(0.0, px[x, y] / 65535.0 * fall - 0.015) / 0.985
            vals.append(int(round(min(1.0, v) * 65535)))
    with open(os.path.join(OUT, name.replace(" ", "") + ".bytes"), "wb") as f:
        f.write(struct.pack("<%dH" % len(vals), *vals))
    print("wrote", name)
