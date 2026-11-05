#!/usr/bin/env python3
"""Textures for real glass: Dust.png (dirty desert glass: alpha from a little in the middle to thick at the sill and corners),
Crack.png (a star of radial cracks from a bullet), Web.png (a windscreen's spider web). Grey with coverage in alpha.
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

def crack():
    random.seed(11); im = Image.new("L", (S, S), 0); d = ImageDraw.Draw(im); c = S / 2
    for _ in range(15):
        ang = random.uniform(0, math.tau); x, y = c, c; L = random.uniform(.25, .95) * c; steps = 9
        for i in range(steps):
            ang += random.uniform(-.2, .2); nx = x + math.cos(ang) * L / steps; ny = y + math.sin(ang) * L / steps
            d.line((x, y, nx, ny), fill=int(255 - 170 * i / steps), width=2 if i < 3 else 1); x, y = nx, ny
    d.ellipse((c - 7, c - 7, c + 7, c + 7), fill=255)
    a = im.filter(ImageFilter.GaussianBlur(0.6)); out = Image.new("RGBA", (S, S), (235, 240, 240, 0)); out.putalpha(a)
    out.save(os.path.join(OUT, "Crack.png"))

def web():
    random.seed(13); im = Image.new("L", (S, S), 0); d = ImageDraw.Draw(im); c = S / 2; rays = []
    for i in range(22):
        ang = i * math.tau / 22 + random.uniform(-.1, .1); pts = [(c, c)]; 
        for k in range(1, 9):
            r = k / 8 * c * random.uniform(.92, 1.0); pts.append((c + math.cos(ang) * r, c + math.sin(ang) * r)); ang += random.uniform(-.03, .03)
        rays.append(pts); d.line(pts, fill=220, width=1)
    for ring in range(1, 8):
        for i in range(22):
            if random.random() < .72:
                d.line((rays[i][ring], rays[(i + 1) % 22][ring + random.choice((0, 0, 0, 1)) if ring < 8 else ring]), fill=int(200 - ring * 12), width=1)
    d.ellipse((c - 10, c - 10, c + 10, c + 10), fill=255)
    out = Image.new("RGBA", (S, S), (235, 240, 240, 0)); out.putalpha(im.filter(ImageFilter.GaussianBlur(0.5)))
    out.save(os.path.join(OUT, "Web.png"))

os.makedirs(OUT, exist_ok=True); dust(); crack(); web()
