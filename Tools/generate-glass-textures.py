#!/usr/bin/env python3
"""Texture for real glass: Dust.png (dirty desert glass: alpha from a little in the middle to thick at the sill and corners). Grey with coverage in alpha.
Cracks are not textures: they are lines on the glass itself (GlassFracture).
    python3 Tools/generate-glass-textures.py"""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Glass")
S = 512

def dust():
    random.seed(7); n = 64
    # Smooth value noise at three scales.
    layers = []
    for cells, w in ((6, .55), (18, .3), (64, .15)):
        im = Image.new("L", (cells, cells)); im.putdata([random.randrange(256) for _ in range(cells * cells)])
        layers.append((im.resize((S, S), Image.BICUBIC), w))
    out = Image.new("LA", (S, S)); px = out.load()
    for y in range(S):
        v = 1 - y / (S - 1)              # 0 at the top of the image; UV 0 (the sill) is the image's bottom
        for x in range(S):
            u = x / (S - 1)
            nz = sum(l.getpixel((x, y)) / 255 * w for l, w in layers)
            sill = (1 - v) ** 3 * 0 + (y / (S - 1)) ** 5                    # thick along the sill
            edge = max(0, 1 - min(u, 1 - u, v, 1 - v) / 0.09) ** 2          # and in the corners of the frame
            a = 0.055 + nz * 0.055 + sill * (0.22 + nz * 0.2) + edge * 0.16
            streak = max(0, math.sin(u * 37 + nz * 3) - 0.86) * (1 - v) * 0.4 # rain and wash streaks under the rail
            px[x, y] = (205, int(min(1, a + streak) * 255))
    out.convert("RGBA").save(os.path.join(OUT, "Dust.png"))

os.makedirs(OUT, exist_ok=True); dust()
