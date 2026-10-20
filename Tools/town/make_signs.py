"""Signs, clock face, carpets and awning cloth for the desert town.

Arabic is shaped and laid out right-to-left by Pillow's raqm layout (so the letters join as they should), in Noto
Naskh Arabic; the English line under it is DejaVu Sans. Every sign gets a little dust and fading so none of them
looks freshly printed. Deterministic (seeded): re-running writes identical files.

Outputs (Assets/FPSKit_Generated/Town/Textures):
  TownSigns.png      2048 x 2048, 4 x 16 cells of 512 x 128 -- shop, street and direction signs
  TownSigns.txt      one cell name per line, in cell order (row-major from the top), read by the builder
  TownRoadSigns.png  1024 x 1024, 4 x 4 cells of 256 x 256 -- stop, give way, no entry, speed, bus, crossing ...
  TownRoadSigns.txt  cell names
  TownClock.png      512 x 512 clock face, Eastern Arabic numerals
  TownCarpets.png    1024 x 1024, 2 x 2 carpets (souk stock, a runner, the mosque's prayer rows, a kilim)
  TownAwnings.png    512 x 512, 2 x 2 striped awning cloths

Usage: python3 -I Tools/town/make_signs.py
"""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageFont, features

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets/FPSKit_Generated/Town/Textures")
os.makedirs(OUT, exist_ok=True)
assert features.check("raqm"), "Pillow needs raqm (libraqm) to shape Arabic"

AR = "/usr/share/fonts/truetype/noto/NotoNaskhArabic-Bold.ttf"
AR_SANS = "/usr/share/fonts/truetype/noto/NotoSansArabic-Bold.ttf"
EN = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
rng = random.Random(4242)


def font(path, size):
    return ImageFont.truetype(path, size, layout_engine=ImageFont.Layout.RAQM)


def fit(draw, text, path, max_w, max_h, start, **kw):
    size = start
    while size > 10:
        f = font(path, size)
        box = draw.textbbox((0, 0), text, font=f, **kw)
        if box[2] - box[0] <= max_w and box[3] - box[1] <= max_h:
            return f, box
        size -= 2
    f = font(path, size)
    return f, draw.textbbox((0, 0), text, font=f, **kw)


def centred(draw, cx, cy, text, f, fill, **kw):
    box = draw.textbbox((0, 0), text, font=f, **kw)
    draw.text((cx - (box[0] + box[2]) / 2, cy - (box[1] + box[3]) / 2), text, font=f, fill=fill, **kw)


def weather(im, amount=0.18, seed=0):
    """Dust towards the bottom, a little fading, speckle: a sign that has stood in the sun."""
    r = random.Random(seed)
    w, h = im.size
    dust = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(dust)
    for _ in range(int(w * h / 900)):
        x, y = r.randrange(w), r.randrange(h)
        a = int(r.uniform(10, 60) * amount * 3 * (0.4 + y / h))
        d.point((x, y), fill=(196, 170, 128, a))
    grad = Image.new("L", (1, h))
    for y in range(h):
        grad.putpixel((0, y), int(255 * amount * (y / h) ** 1.6))
    tint = Image.new("RGBA", (w, h), (205, 180, 140, 0))
    tint.putalpha(grad.resize((w, h)))
    out = Image.alpha_composite(im.convert("RGBA"), tint)
    out = Image.alpha_composite(out, dust.filter(ImageFilter.GaussianBlur(0.6)))
    return out


# ----------------------------------------------------------------------------------------------- shop signs
#   name, arabic, english, background, text colour, accent
SHOPS = [
    ("pharmacy", "صيدلية", "PHARMACY", (24, 120, 70), (255, 255, 255), "cross"),
    ("bakery", "مخبز", "BAKERY", (240, 226, 196), (140, 60, 20), None),
    ("grocery", "بقالة", "GROCERY", (196, 40, 36), (255, 255, 255), None),
    ("restaurant", "مطعم", "RESTAURANT", (30, 30, 34), (240, 196, 80), None),
    ("cafe", "مقهى", "CAFE", (90, 52, 30), (250, 232, 200), None),
    ("barber", "حلاق", "BARBER", (250, 250, 248), (26, 64, 150), "pole"),
    ("phones", "هواتف", "MOBILE PHONES", (20, 90, 170), (255, 255, 255), None),
    ("clothing", "ملابس", "CLOTHING", (120, 30, 90), (255, 240, 250), None),
    ("gold", "ذهب ومجوهرات", "GOLD & JEWELLERY", (24, 22, 20), (230, 186, 80), None),
    ("perfume", "عطور", "PERFUMES", (60, 30, 100), (240, 220, 255), None),
    ("butcher", "لحوم", "BUTCHER", (170, 20, 24), (255, 255, 255), None),
    ("fruit", "خضار وفواكه", "FRUIT & VEG", (60, 140, 50), (255, 255, 255), None),
    ("books", "مكتبة", "BOOKSHOP", (240, 236, 224), (30, 50, 110), None),
    ("hotel", "فندق", "HOTEL", (16, 44, 90), (255, 255, 255), None),
    ("bank", "بنك", "BANK", (230, 232, 236), (10, 70, 130), None),
    ("clinic", "عيادة", "CLINIC", (250, 250, 250), (200, 20, 30), "cross"),
    ("autorepair", "ميكانيك سيارات", "AUTO REPAIR", (250, 200, 30), (20, 20, 20), None),
    ("tyres", "إطارات", "TYRES", (20, 20, 20), (250, 200, 30), None),
    ("fuel", "محطة وقود", "FUEL STATION", (0, 110, 70), (255, 255, 255), None),
    ("sweets", "حلويات", "SWEETS", (250, 214, 226), (150, 20, 70), None),
    ("spices", "توابل", "SPICES", (176, 90, 20), (255, 240, 210), None),
    ("carpets", "سجاد", "CARPETS", (120, 24, 30), (240, 210, 150), None),
    ("fabrics", "أقمشة", "FABRICS", (40, 90, 120), (255, 255, 255), None),
    ("housewares", "أدوات منزلية", "HOUSEWARES", (240, 240, 236), (200, 60, 20), None),
    ("electricals", "كهربائيات", "ELECTRICALS", (14, 60, 120), (250, 210, 40), None),
    ("laundry", "مغسلة", "LAUNDRY", (200, 230, 250), (10, 70, 140), None),
    ("shawarma", "شاورما", "SHAWARMA", (200, 60, 20), (255, 236, 180), None),
    ("juice", "عصير", "FRESH JUICE", (250, 150, 20), (255, 255, 255), None),
    ("supermarket", "سوبر ماركت", "SUPERMARKET", (230, 30, 40), (255, 255, 255), None),
    ("carpentry", "نجارة", "CARPENTRY", (120, 80, 40), (250, 230, 190), None),
    ("gas", "غاز", "GAS CYLINDERS", (240, 120, 20), (255, 255, 255), None),
    ("tailor", "خياط", "TAILOR", (236, 228, 210), (60, 40, 30), None),
    ("municipality", "بلدية", "MUNICIPALITY", (236, 232, 220), (30, 70, 40), None),
    ("police", "مركز شرطة", "POLICE", (20, 40, 100), (255, 255, 255), None),
    ("school", "مدرسة", "SCHOOL", (240, 236, 224), (20, 100, 60), None),
    ("hospital", "مستشفى", "HOSPITAL", (250, 250, 250), (200, 20, 30), "cross"),
    ("checkpoint", "نقطة تفتيش", "CHECKPOINT", (200, 20, 24), (255, 255, 255), None),
    ("busstop", "موقف حافلات", "BUS STOP", (20, 80, 160), (255, 255, 255), None),
    ("soukgate", "باب السوق", "", (24, 70, 50), (230, 196, 110), "frame"),
]
STREETS = [
    ("st_souk", "شارع السوق", "SOUK ST."),
    ("st_mosque", "شارع الجامع", "MOSQUE ST."),
    ("st_citadel", "شارع القلعة", "CITADEL ST."),
    ("st_palm", "شارع النخيل", "PALM ST."),
    ("st_palace", "شارع القصر", "PALACE ST."),
    ("st_station", "شارع المحطة", "STATION ST."),
]
DIRECTIONS = [
    ("dir_mosque", "الجامع الكبير", "GRAND MOSQUE", "up"),
    ("dir_citadel", "القلعة", "CITADEL", "right"),
    ("dir_souk", "السوق", "SOUK", "left"),
    ("dir_palace", "القصر", "PALACE", "left"),
    ("dir_highway", "الطريق السريع", "HIGHWAY", "down"),
    ("dir_oasis", "الواحة", "OASIS", "left"),
]


def arrow(draw, cx, cy, size, way, fill):
    pts = {"up": [(0, -1), (0.8, 0), (0.3, 0), (0.3, 1), (-0.3, 1), (-0.3, 0), (-0.8, 0)]}
    base = pts["up"]
    rot = {"up": 0, "right": 90, "down": 180, "left": 270}[way] * math.pi / 180
    poly = [(cx + size * (x * math.cos(rot) - y * math.sin(rot)), cy + size * (x * math.sin(rot) + y * math.cos(rot))) for x, y in base]
    draw.polygon(poly, fill=fill)


def shop_cell(spec, seed):
    name, ar, en, bg, fg, accent = spec
    W, H = 512, 128
    im = Image.new("RGBA", (W, H), bg + (255,))
    d = ImageDraw.Draw(im)
    if accent == "frame":
        d.rectangle([6, 6, W - 7, H - 7], outline=fg, width=4)
        d.rectangle([14, 14, W - 15, H - 15], outline=fg, width=1)
    else:
        d.rectangle([0, 0, W - 1, H - 1], outline=tuple(max(0, c - 40) for c in bg), width=4)
    left = 18
    if accent == "cross":
        c = (24, 160, 80) if bg[1] < 200 else (210, 20, 30)
        if bg == (24, 120, 70): c = (255, 255, 255)
        d.rectangle([22, 40, 70, 88], fill=c); d.rectangle([34, 28, 58, 100], fill=c)
        left = 86
    if accent == "pole":
        for i in range(8):
            d.polygon([(26, 14 + i * 14), (52, 14 + i * 14 - 8), (52, 14 + i * 14 + 2), (26, 14 + i * 14 + 10)], fill=(200, 20, 30) if i % 2 else (26, 64, 150))
        left = 64
    if en:
        fa, _ = fit(d, ar, AR, W - left - 20, 74, 84, direction="rtl", language="ar")
        centred(d, (left + W) / 2, 50, ar, fa, fg, direction="rtl", language="ar")
        fe, _ = fit(d, en, EN, W - left - 30, 26, 24)
        centred(d, (left + W) / 2, 106, en, fe, fg)
    else:
        fa, _ = fit(d, ar, AR, W - 60, 96, 100, direction="rtl", language="ar")
        centred(d, W / 2, H / 2 - 4, ar, fa, fg, direction="rtl", language="ar")
    return weather(im, 0.16 + rng.random() * 0.12, seed)


def street_cell(spec, seed):
    name, ar, en = spec
    W, H = 512, 128
    im = Image.new("RGBA", (W, H), (20, 70, 150, 255))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([5, 5, W - 6, H - 6], radius=12, outline=(255, 255, 255), width=5)
    fa, _ = fit(d, ar, AR, W - 60, 64, 70, direction="rtl", language="ar")
    centred(d, W / 2, 48, ar, fa, (255, 255, 255), direction="rtl", language="ar")
    fe, _ = fit(d, en, EN, W - 80, 26, 26)
    centred(d, W / 2, 101, en, fe, (255, 255, 255))
    return weather(im, 0.14, seed)


def direction_cell(spec, seed):
    name, ar, en, way = spec
    W, H = 512, 128
    im = Image.new("RGBA", (W, H), (0, 100, 60, 255))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([5, 5, W - 6, H - 6], radius=10, outline=(255, 255, 255), width=4)
    arrow(d, 64 if way != "right" else W - 64, H / 2, 34, way, (255, 255, 255))
    x0, x1 = (120, W - 20) if way != "right" else (20, W - 120)
    fa, _ = fit(d, ar, AR, x1 - x0, 62, 66, direction="rtl", language="ar")
    centred(d, (x0 + x1) / 2, 46, ar, fa, (255, 255, 255), direction="rtl", language="ar")
    fe, _ = fit(d, en, EN, x1 - x0, 26, 26)
    centred(d, (x0 + x1) / 2, 100, en, fe, (255, 255, 255))
    return weather(im, 0.12, seed)


cells = [("sh_" + s[0], shop_cell(s, i)) for i, s in enumerate(SHOPS)]
cells += [(s[0], street_cell(s, 100 + i)) for i, s in enumerate(STREETS)]
cells += [(s[0], direction_cell(s, 200 + i)) for i, s in enumerate(DIRECTIONS)]
assert len(cells) <= 64, len(cells)
atlas = Image.new("RGBA", (2048, 2048), (40, 40, 40, 255))
for k, (name, im) in enumerate(cells):
    atlas.paste(im, ((k % 4) * 512, (k // 4) * 128))
atlas.convert("RGB").save(os.path.join(OUT, "TownSigns.png"), optimize=True)
open(os.path.join(OUT, "TownSigns.txt"), "w").write("\n".join(n for n, _ in cells) + "\n")

# ----------------------------------------------------------------------------------------------- road signs
def road_cell(kind, seed):
    S = 256
    im = Image.new("RGBA", (S, S), (150, 150, 150, 255))
    d = ImageDraw.Draw(im)
    c = S / 2
    if kind == "stop":
        pts = [(c + 118 * math.cos(math.pi / 8 + k * math.pi / 4), c + 118 * math.sin(math.pi / 8 + k * math.pi / 4)) for k in range(8)]
        d.polygon(pts, fill=(255, 255, 255))
        pts = [(c + 108 * math.cos(math.pi / 8 + k * math.pi / 4), c + 108 * math.sin(math.pi / 8 + k * math.pi / 4)) for k in range(8)]
        d.polygon(pts, fill=(200, 20, 24))
        centred(d, c, c - 26, "قف", font(AR, 72), (255, 255, 255), direction="rtl", language="ar")
        centred(d, c, c + 44, "STOP", font(EN, 46), (255, 255, 255))
    elif kind == "giveway":
        d.polygon([(c - 116, 30), (c + 116, 30), (c, 230)], fill=(200, 20, 24))
        d.polygon([(c - 82, 52), (c + 82, 52), (c, 194)], fill=(255, 255, 255))
    elif kind == "noentry":
        d.ellipse([10, 10, S - 10, S - 10], fill=(200, 20, 24)); d.rectangle([40, c - 20, S - 40, c + 20], fill=(255, 255, 255))
    elif kind == "speed50":
        d.ellipse([10, 10, S - 10, S - 10], fill=(200, 20, 24)); d.ellipse([34, 34, S - 34, S - 34], fill=(255, 255, 255))
        centred(d, c, c - 22, "٥٠", font(AR, 70), (20, 20, 20), direction="rtl", language="ar")
        centred(d, c, c + 40, "50", font(EN, 56), (20, 20, 20))
    elif kind == "speed80":
        d.ellipse([10, 10, S - 10, S - 10], fill=(200, 20, 24)); d.ellipse([34, 34, S - 34, S - 34], fill=(255, 255, 255))
        centred(d, c, c - 22, "٨٠", font(AR, 70), (20, 20, 20), direction="rtl", language="ar")
        centred(d, c, c + 40, "80", font(EN, 56), (20, 20, 20))
    elif kind == "roundabout":
        d.ellipse([10, 10, S - 10, S - 10], fill=(20, 80, 170))
        for k in range(3):
            a = k * 2 * math.pi / 3
            d.arc([60, 60, S - 60, S - 60], math.degrees(a) + 20, math.degrees(a) + 100, fill=(255, 255, 255), width=18)
    elif kind == "crossing":
        d.rectangle([12, 12, S - 12, S - 12], fill=(20, 80, 170))
        d.polygon([(c, 34), (S - 40, S - 50), (40, S - 50)], fill=(255, 255, 255))
        d.ellipse([c - 14, 80, c + 14, 108], fill=(20, 20, 20)); d.line([(c, 110), (c - 10, 160), (c - 26, 190)], fill=(20, 20, 20), width=10)
        d.line([(c - 10, 160), (c + 16, 190)], fill=(20, 20, 20), width=10)
    elif kind == "parking":
        d.rectangle([12, 12, S - 12, S - 12], fill=(20, 80, 170)); centred(d, c, c, "P", font(EN, 170), (255, 255, 255))
    elif kind == "busstop":
        d.rectangle([12, 12, S - 12, S - 12], fill=(20, 80, 170))
        d.rounded_rectangle([60, 70, S - 60, 170], radius=14, fill=(255, 255, 255)); d.rectangle([74, 84, S - 74, 122], fill=(20, 80, 170))
        d.ellipse([78, 160, 106, 188], fill=(255, 255, 255)); d.ellipse([S - 106, 160, S - 78, 188], fill=(255, 255, 255))
        centred(d, c, 220, "حافلات", font(AR, 30), (255, 255, 255), direction="rtl", language="ar")
    elif kind == "keepright":
        d.ellipse([10, 10, S - 10, S - 10], fill=(20, 80, 170)); arrow(d, c, c, 70, "right", (255, 255, 255))
    elif kind == "notraffic":
        d.ellipse([10, 10, S - 10, S - 10], fill=(200, 20, 24)); d.ellipse([34, 34, S - 34, S - 34], fill=(255, 255, 255))
    elif kind == "hump":
        d.polygon([(c, 18), (S - 18, S - 30), (18, S - 30)], fill=(200, 20, 24)); d.polygon([(c, 46), (S - 46, S - 46), (46, S - 46)], fill=(255, 255, 255))
        d.chord([70, 130, S - 70, 210], 180, 360, fill=(20, 20, 20))
    elif kind == "danger":
        d.polygon([(c, 18), (S - 18, S - 30), (18, S - 30)], fill=(200, 20, 24)); d.polygon([(c, 46), (S - 46, S - 46), (46, S - 46)], fill=(255, 255, 255))
        d.rectangle([c - 10, 92, c + 10, 160], fill=(20, 20, 20)); d.ellipse([c - 11, 172, c + 11, 194], fill=(20, 20, 20))
    elif kind == "back":
        im = Image.new("RGBA", (S, S), (140, 142, 144, 255))
    return weather(im, 0.14, seed)


ROADS = ["stop", "giveway", "noentry", "speed50", "speed80", "roundabout", "crossing", "parking", "busstop", "keepright", "notraffic", "hump", "danger", "back"]
ratlas = Image.new("RGBA", (1024, 1024), (150, 150, 150, 255))
for k, kind in enumerate(ROADS):
    ratlas.paste(road_cell(kind, 300 + k), ((k % 4) * 256, (k // 4) * 256))
ratlas.convert("RGB").save(os.path.join(OUT, "TownRoadSigns.png"), optimize=True)
open(os.path.join(OUT, "TownRoadSigns.txt"), "w").write("\n".join(ROADS) + "\n")

# ----------------------------------------------------------------------------------------------- clock face
S = 512
im = Image.new("RGBA", (S, S), (236, 230, 214, 255))
d = ImageDraw.Draw(im)
c = S / 2
d.ellipse([6, 6, S - 6, S - 6], fill=(242, 238, 226), outline=(40, 34, 28), width=10)
d.ellipse([30, 30, S - 30, S - 30], outline=(120, 100, 70), width=2)
nums = "١٢ ١ ٢ ٣ ٤ ٥ ٦ ٧ ٨ ٩ ١٠ ١١".split()
fn = font(AR, 46)
for k in range(12):
    a = k * math.pi / 6 - math.pi / 2
    centred(d, c + math.cos(a) * 190, c + math.sin(a) * 190, nums[k], fn, (30, 26, 22), direction="rtl", language="ar")
for k in range(60):
    a = k * math.pi / 30
    r0 = 222 if k % 5 else 210
    d.line([(c + math.cos(a) * r0, c + math.sin(a) * r0), (c + math.cos(a) * 232, c + math.sin(a) * 232)], fill=(40, 34, 28), width=3 if k % 5 else 6)
weather(im, 0.10, 400).convert("RGB").save(os.path.join(OUT, "TownClock.png"), optimize=True)

# ----------------------------------------------------------------------------------------------- carpets
def carpet(seed, palette, kind):
    r = random.Random(seed)
    S = 512
    im = Image.new("RGB", (S, S), palette[0])
    d = ImageDraw.Draw(im)
    if kind == "prayer":
        # Rows of prayer places: each a niche (mihrab arch) in a border.
        w = S // 4
        for row in range(2):
            for col in range(4):
                x0, y0 = col * w, row * (S // 2)
                d.rectangle([x0 + 4, y0 + 4, x0 + w - 4, y0 + S // 2 - 4], fill=palette[1])
                d.rectangle([x0 + 12, y0 + 40, x0 + w - 12, y0 + S // 2 - 12], fill=palette[0])
                d.pieslice([x0 + 12, y0 + 12, x0 + w - 12, y0 + 70], 180, 360, fill=palette[0])
                d.line([(x0 + w / 2, y0 + 60), (x0 + w / 2, y0 + 220)], fill=palette[2], width=3)
        return im
    d.rectangle([0, 0, S - 1, S - 1], outline=palette[1], width=36)
    d.rectangle([44, 44, S - 45, S - 45], outline=palette[2], width=8)
    for i in range(14):
        x = 70 + (i % 7) * 62; y = 120 if i < 7 else 360
        d.polygon([(x, y - 30), (x + 26, y), (x, y + 30), (x - 26, y)], outline=palette[2], fill=palette[3 if i % 2 else 1])
    d.polygon([(c, 140), (c + 110, c), (c, S - 140), (c - 110, c)], fill=palette[1], outline=palette[2])
    d.polygon([(c, 190), (c + 60, c), (c, S - 190), (c - 60, c)], fill=palette[3])
    if kind == "kilim":
        for y in range(60, S - 60, 24):
            d.line([(60, y), (S - 60, y)], fill=palette[r.randrange(1, 4)], width=6)
    noise = Image.effect_noise((S, S), 22).convert("RGB")
    return Image.blend(im, Image.composite(im, noise, Image.new("L", (S, S), 210)), 0.25)


ca = Image.new("RGB", (1024, 1024))
ca.paste(carpet(1, [(140, 24, 30), (30, 34, 70), (226, 196, 140), (190, 120, 40)], "rug"), (0, 0))
ca.paste(carpet(2, [(30, 50, 90), (150, 30, 36), (230, 210, 170), (40, 110, 90)], "rug"), (512, 0))
ca.paste(carpet(3, [(150, 20, 30), (196, 150, 70), (230, 220, 190)], "prayer"), (0, 512))
ca.paste(carpet(4, [(180, 90, 40), (60, 30, 20), (230, 200, 140), (120, 40, 30)], "kilim"), (512, 512))
ca.save(os.path.join(OUT, "TownCarpets.png"), optimize=True)

# ----------------------------------------------------------------------------------------------- awnings
aw = Image.new("RGB", (512, 512))
d = ImageDraw.Draw(aw)
pairs = [((176, 30, 30), (236, 228, 210)), ((30, 110, 60), (236, 228, 210)), ((30, 70, 140), (236, 228, 210)), ((200, 130, 30), (120, 60, 30))]
for k, (a, b) in enumerate(pairs):
    x0, y0 = (k % 2) * 256, (k // 2) * 256
    for i in range(8):
        d.rectangle([x0 + i * 32, y0, x0 + i * 32 + 31, y0 + 255], fill=a if i % 2 == 0 else b)
aw = Image.blend(aw, Image.effect_noise((512, 512), 18).convert("RGB"), 0.08)
aw.save(os.path.join(OUT, "TownAwnings.png"), optimize=True)
print("signs", len(cells), "road signs", len(ROADS))
