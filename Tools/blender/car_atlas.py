#!/usr/bin/env python3
"""The one atlas every non-paint part of every car shares (Tools/blender/make_cars.py): 8 x 8 cells of 256 px. One material for trim, lamps, tyres,
seats and dials means a parked car is three draws (paint, atlas, glass), which is what lets a street hold two hundred of them.
Writes CarAtlas.png (colour) and CarAtlas_MS.png (URP metallic in R, smoothness in A).
    python3 Tools/blender/car_atlas.py Assets/Models/Cars/Textures"""
import math, os, random, sys

CELL = 256
GRID = 8
# name: (index, metallic, smoothness). make_cars.py reads this table.
CELLS = {
    "Trim": (0, 0.0, 0.38), "Seam": (1, 0.0, 0.05), "Grille": (2, 0.3, 0.45), "HeadLamp": (3, 0.7, 0.92), "TailLamp": (4, 0.0, 0.85), "Reverse": (5, 0.2, 0.9),
    "Plate": (6, 0.0, 0.35), "Chrome": (7, 1.0, 0.9), "Alloy": (8, 0.9, 0.62), "Sidewall": (9, 0.0, 0.22), "Tread": (10, 0.0, 0.12), "Dash": (11, 0.0, 0.28),
    "Card": (12, 0.0, 0.25), "Liner": (13, 0.0, 0.08), "SeatDark": (14, 0.0, 0.1), "SeatTan": (15, 0.0, 0.1), "Carpet": (16, 0.0, 0.03), "Dials": (17, 0.0, 0.55),
    "Leather": (18, 0.0, 0.35), "Needle": (19, 0.0, 0.6), "Mirror": (20, 0.0, 0.92), "Under": (21, 0.0, 0.08), "Amber": (22, 0.0, 0.85), "BrakeOff": (23, 0.0, 0.85),
    "Steel": (24, 0.85, 0.4), "Wood": (25, 0.0, 0.6), "Hubcap": (26, 0.6, 0.5), "DashTan": (27, 0.0, 0.25), "Taxi": (28, 0.0, 0.5), "White": (29, 0.0, 0.6),
    "Radio": (30, 0.0, 0.5), "Rack": (31, 0.6, 0.4), "Tarp": (32, 0.0, 0.1), "Rust": (33, 0.2, 0.2),
}

def main(out):
    from PIL import Image, ImageDraw, ImageFont, ImageFilter
    random.seed(4711)
    size = CELL * GRID
    colour = Image.new("RGB", (size, size), (30, 30, 30))
    ms = Image.new("RGBA", (size, size), (0, 0, 0, 80))
    d = ImageDraw.Draw(colour)
    # A flat colour with a little noise, so nothing is one dead value in the sun.
    def speckle(box, base, amount):
        px = colour.load()
        for y in range(box[1], box[3]):
            for x in range(box[0], box[2]):
                n = random.randint(-amount, amount)
                px[x, y] = (max(0, min(255, base[0] + n)), max(0, min(255, base[1] + n)), max(0, min(255, base[2] + n)))
    def box_of(index):
        cx, cy = index % GRID, index // GRID
        return (cx * CELL, cy * CELL, (cx + 1) * CELL, (cy + 1) * CELL)
    flat = {"Trim": (20, 20, 21), "Seam": (6, 6, 6), "Chrome": (225, 225, 228), "Alloy": (178, 180, 184), "Dash": (28, 28, 29), "Card": (44, 43, 42), "Liner": (98, 94, 86),
            "Carpet": (24, 23, 22), "Under": (15, 14, 13), "Steel": (92, 90, 88), "Hubcap": (168, 170, 172), "DashTan": (120, 104, 82),
            "Taxi": (226, 176, 28), "White": (232, 232, 226), "Rack": (40, 40, 42), "Tarp": (70, 72, 52), "Rust": (112, 62, 34), "Needle": (240, 90, 30), "Leather": (22, 21, 20),
            "Sidewall": (24, 24, 25), "Tread": (19, 19, 20)}
    for name, c in flat.items():
        speckle(box_of(CELLS[name][0]), c, 3)
    # A mirror in a game: what it would show, near enough. Pale sky over a grey road running away, seen in a glance; a real reflection is a second camera.
    b = box_of(CELLS["Mirror"][0])
    for i in range(CELL):
        t = i / CELL
        c = (196 - int(30 * t * 2), 214 - int(20 * t * 2), 232 - int(10 * t * 2)) if t < 0.5 else (116 - int(46 * (t - 0.5)), 110 - int(46 * (t - 0.5)), 104 - int(46 * (t - 0.5)))
        d.line([(b[0], b[1] + i), (b[2], b[1] + i)], fill=c)
    d.polygon([(b[0] + 104, b[1] + 128), (b[0] + 152, b[1] + 128), (b[0] + 210, b[3]), (b[0] + 46, b[3])], fill=(70, 68, 66))
    # Grille: a honeycomb of dark openings in a dark frame.
    b = box_of(CELLS["Grille"][0]); d.rectangle(b, fill=(26, 26, 28))
    for row in range(0, 14):
        for col in range(0, 14):
            x = b[0] + col * 20 + (row % 2) * 10; y = b[1] + row * 18
            if x + 14 < b[2] and y + 12 < b[3]:
                d.polygon([(x + 7, y), (x + 14, y + 4), (x + 14, y + 10), (x + 7, y + 14), (x, y + 10), (x, y + 4)], fill=(4, 4, 5))
    # Headlamp: the reflector bowl and a projector under a clear lens.
    b = box_of(CELLS["HeadLamp"][0])
    for i in range(CELL):
        t = i / CELL; v = int(120 + 110 * math.sin(t * math.pi))
        d.line([(b[0], b[1] + i), (b[2], b[1] + i)], fill=(v, v, min(255, v + 6)))
    for cx, r in ((0.3, 62), (0.72, 48)):
        x, y = b[0] + int(cx * CELL), b[1] + CELL // 2
        d.ellipse((x - r, y - r, x + r, y + r), fill=(235, 238, 240), outline=(60, 60, 64), width=6)
        d.ellipse((x - r // 2, y - r // 2, x + r // 2, y + r // 2), fill=(255, 255, 250))
    # Tail lamp: red segments behind the lens.
    b = box_of(CELLS["TailLamp"][0]); d.rectangle(b, fill=(92, 8, 10))
    for x in range(b[0], b[2], 32):
        for y in range(b[1], b[3], 32):
            d.rectangle((x + 2, y + 2, x + 29, y + 29), fill=(146 + random.randint(-14, 14), 12, 16))
    b = box_of(CELLS["BrakeOff"][0]); d.rectangle(b, fill=(116, 10, 12))
    b = box_of(CELLS["Amber"][0]); d.rectangle(b, fill=(214, 112, 12))
    b = box_of(CELLS["Reverse"][0]); d.rectangle(b, fill=(214, 214, 212))
    # Number plate: no real country's, just a plate.
    b = box_of(CELLS["Plate"][0]); d.rectangle(b, fill=(226, 222, 196)); d.rectangle(b, outline=(30, 30, 30), width=8)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf", 54)
        small = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf", 22)
        taxi = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 72)
    except OSError:
        font = small = taxi = ImageFont.load_default()
    d.text((b[0] + 22, b[1] + 96), "42 716", fill=(16, 16, 16), font=font)
    b = box_of(CELLS["Taxi"][0]); d.text((b[0] + 28, b[1] + 86), "TAXI", fill=(18, 18, 18), font=taxi)
    # Seat cloth: a weave.
    for name, c, n in (("SeatDark", (34, 34, 36), 7), ("SeatTan", (124, 106, 82), 9)):
        b = box_of(CELLS[name][0]); speckle(b, c, n)
        for y in range(b[1], b[3], 6): d.line([(b[0], y), (b[2], y)], fill=tuple(int(v * 0.8) for v in c))
    # Wood trim of the older car.
    b = box_of(CELLS["Wood"][0])
    for y in range(b[1], b[3]):
        v = 70 + int(22 * math.sin(y * 0.11 + 2 * math.sin(y * 0.031)))
        d.line([(b[0], y), (b[2], y)], fill=(v + 26, v - 6, max(0, v - 34)))
    # Tread blocks across the width of the tyre.
    b = box_of(CELLS["Tread"][0])
    for x in range(b[0], b[2], 40):
        for y in range(b[1], b[3], 26):
            d.line([(x, y), (x + 20, y + 13), (x, y + 26)], fill=(5, 5, 5), width=5)
    # The tyre's wall: two moulded rings.
    b = box_of(CELLS["Sidewall"][0])
    for y in (b[1] + 60, b[1] + 196): d.line([(b[0], y), (b[2], y)], fill=(40, 40, 41), width=4)
    # The two dials side by side: light marks on black, as read through the wheel.
    b = box_of(CELLS["Dials"][0]); d.rectangle(b, fill=(6, 6, 8))
    def dial(cx, label, top):
        cy = b[1] + CELL // 2; r = 58
        d.ellipse((cx - r - 4, cy - r - 4, cx + r + 4, cy + r + 4), outline=(90, 90, 96), width=3)
        for i in range(0, top + 1):
            a = math.radians(225 - 270 * i / top)
            major = i % 2 == 0
            r0 = r - (13 if major else 7)
            d.line([(cx + math.cos(a) * r0, cy - math.sin(a) * r0), (cx + math.cos(a) * r, cy - math.sin(a) * r)], fill=(232, 232, 226), width=3 if major else 2)
        d.text((cx - len(label) * 6.5, cy + 20), label, fill=(190, 190, 186), font=small)
    dial(b[0] + 64, "km/h", 16)
    dial(b[0] + 192, "rpm", 8)
    # The radio and climate stack in the middle of the dash.
    b = box_of(CELLS["Radio"][0]); d.rectangle(b, fill=(16, 16, 18))
    d.rectangle((b[0] + 28, b[1] + 26, b[2] - 28, b[1] + 92), fill=(10, 30, 26), outline=(70, 70, 74), width=3)
    d.text((b[0] + 54, b[1] + 44), "98.6 FM", fill=(90, 220, 170), font=small)
    for i in range(3):
        x = b[0] + 54 + i * 74; d.ellipse((x - 25, b[1] + 142, x + 25, b[1] + 192), outline=(120, 120, 124), width=4, fill=(32, 32, 34))
    colour.save(os.path.join(out, "CarAtlas.png"))
    # Metallic and smoothness, per cell.
    md = ImageDraw.Draw(ms)
    for name, (index, metal, smooth) in CELLS.items():
        md.rectangle(box_of(index), fill=(int(metal * 255), 0, 0, int(smooth * 255)))
    # The dark openings of the grille and the plate's face are not shiny all over.
    ms.save(os.path.join(out, "CarAtlas_MS.png"))
    # The dust on paint: heavier low down, streaks under the belt, a multiply on the colour. u along the car, v up it (a metre to 1/2.5 of the map).
    dust = Image.new("L", (512, 512), 255)
    px = dust.load()
    for y in range(512):
        h = y / 511.0            # 0 at the top of the image = v 1
        low = max(0.0, (h - 0.55) / 0.45)
        for x in range(512):
            n = random.randint(-3, 3)
            px[x, y] = max(0, min(255, int(250 - 58 * low * low + n)))
    dust = dust.filter(ImageFilter.GaussianBlur(1.2))
    dust.convert("RGB").save(os.path.join(out, "CarDust.png"))
    print("[cars] atlas written to", out)

if __name__ == "__main__":
    target = sys.argv[1]
    os.makedirs(target, exist_ok=True)
    main(target)
