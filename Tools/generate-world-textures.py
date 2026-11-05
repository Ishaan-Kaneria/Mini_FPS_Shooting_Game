#!/usr/bin/env python3
"""Painted-metal maps for the drum (colour stays on the material: this is scratched near-white paint with rust at the chimes and hoops),
and the fuel pump's display. Drum_MS: metal in R, smoothness in A.   python3 Tools/generate-world-textures.py"""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageFont
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Models", "World", "Textures")
os.makedirs(OUT, exist_ok=True)
W, H = 1024, 1024
random.seed(5)
def noise(cells, seed):
    r = random.Random(seed); im = Image.new("L", (cells, cells)); im.putdata([r.randrange(256) for _ in range(cells * cells)])
    return im.resize((W, H), Image.BICUBIC)
big, mid, fine = noise(10, 1), noise(48, 2), noise(220, 3)
albedo = Image.new("RGB", (W, H)); ms = Image.new("RGBA", (W, H)); ap = albedo.load(); mp = ms.load()
BANDS = (0.015, 0.34, 0.66, 0.985)                 # chimes and rolling hoops, by height
for y in range(H):
    v = 1 - y / (H - 1)
    band = min(abs(v - b) for b in BANDS)
    for x in range(W):
        n = big.getpixel((x, y)) / 255 * .5 + mid.getpixel((x, y)) / 255 * .32 + fine.getpixel((x, y)) / 255 * .18
        rust = max(0, (0.62 - band * 7 + (n - .5) * 1.6))          # at the steel's edges first
        rust = rust + max(0, (n - .69) * 3)                         # and in patches where the paint has gone
        rust = min(1, rust ** 2 * 3.2)
        paint = 214 + (fine.getpixel((x, y)) - 128) * .1 - (1 - n) * 22
        c = [paint * (1 - rust) + 112 * rust, paint * (1 - rust) + 64 * rust, paint * (1 - rust) + 34 * rust]
        ap[x, y] = tuple(int(v2) for v2 in c)
        mp[x, y] = (int(35 * (1 - rust)), 0, 0, int((0.46 - n * .14) * (1 - rust * .75) * 255))
d = ImageDraw.Draw(albedo)
for _ in range(240):       # scratches and run marks
    x, y = random.randrange(W), random.randrange(H); a = random.gauss(math.pi / 2, .9); L = random.uniform(10, 120)
    d.line((x, y, x + math.cos(a) * L, y + math.sin(a) * L), fill=(150, 140, 128), width=1)
albedo = albedo.filter(ImageFilter.GaussianBlur(0.4))
albedo.save(os.path.join(OUT, "Drum_Albedo.png")); ms.save(os.path.join(OUT, "Drum_MS.png"))

# The pump's display: litres and price, amber on black.
disp = Image.new("RGB", (512, 256), (8, 10, 9)); d = ImageDraw.Draw(disp)
try: font = ImageFont.truetype("DejaVuSansMono-Bold.ttf", 54); small = ImageFont.truetype("DejaVuSans.ttf", 22)
except OSError: font = small = ImageFont.load_default()
for i, (label, value) in enumerate((("LITRES", "0024.61"), ("TOTAL", "0039.37"), ("PRICE/L", "01.60"))):
    y = 16 + i * 80
    d.text((18, y + 20), label, fill=(170, 170, 160), font=small)
    d.rectangle((150, y, 496, y + 66), fill=(20, 18, 8))
    d.text((170, y + 2), value, fill=(240, 170, 40), font=font)
disp.save(os.path.join(OUT, "Pump_Display.png"))
