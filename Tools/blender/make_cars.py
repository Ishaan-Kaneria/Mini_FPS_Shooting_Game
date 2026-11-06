"""The town's cars, made in Blender. No brand's shape, badge or name. Each body is ONE surface: a grid of cross-sections along the car (stations) and round
it (rings), smoothed by Catmull-Clark and then cut, by grid cell, into paint, glass, lamps, doors and trim, so a window fits its frame because it IS that
piece of the surface, and an arch is the body's lower edge following the wheel. One FBX per car, front towards -Y (Unity +Z), driver on +X (Unity -X, left).

Objects the game reads by name (FPSKitCars):
  Body, Door_FL/FR/RL/RR (origin on the hinge), Card_*, Glass_Front/Rear/FL/FR/RL/RR, Wheel_FL/FR/RL/RR (origin on the axle), Interior, SteerWheel (origin
  on the hub), Needle_Speed/Rpm (origin on the pivot), Glow_Brake/Reverse/Head, LOD1_*, LOD2_*; empties Seat_Eye, Grip_L, Grip_R, Exhaust, Steer_Axis, Exit.
Materials: Paint (uv = metres, for the dust), Atlas (Tools/blender/car_atlas.py), Glass.

    ~/Programs/blender/blender --background --factory-startup --python Tools/blender/make_cars.py -- OUT_DIR [--render DIR] [Name ...]"""
import math, os, sys
import bmesh, bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from car_atlas import CELLS, GRID

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0]); RENDER = None; ONLY = set()
rest = argv[1:]
while rest:
    a = rest.pop(0)
    if a == "--render": RENDER = os.path.abspath(rest.pop(0))
    else: ONLY.add(a)
os.makedirs(OUT, exist_ok=True)

# ============================================================================ curves
def curve(keys, y):
    """Straight between keys: the grid's smoothing rounds them."""
    if y <= keys[0][0]: return keys[0][1]
    for (a, va), (b, vb) in zip(keys, keys[1:]):
        if y <= b: return va + (vb - va) * (y - a) / max(1e-6, b - a)
    return keys[-1][1]

R_BOTTOM, R_FLOOR, R_LIP, R_SEAMA, R_SEAMB, R_DOOR, R_CREASE, R_BELT, R_WIN0, R_WIN1, R_RAIL, R_INNER, R_TOP = range(13)
RINGS = 13

# ============================================================================ the cars
def spec(**k):
    k.setdefault("rows_front", [(0.0, 0.0), (0.34, 0.012), (0.62, 0.05), (0.80, 0.11), (0.93, 0.21), (1.0, 0.42)])
    k.setdefault("rows_rear", [(0.0, 0.0), (0.40, 0.006), (0.70, 0.024), (0.88, 0.07), (0.97, 0.15), (1.0, 0.28)])
    k.setdefault("seat_tan", False); k.setdefault("wood", False); k.setdefault("hubcap", False); k.setdefault("spokes", 5)
    k.setdefault("bed", None); k.setdefault("extras", []); k.setdefault("drive", "rear"); k.setdefault("doors", 4); k.setdefault("lamp", "wide")
    return k

CARS = {
    # A modern family saloon: raked screen, high deck, the rounded shape of the last twenty years.
    "Saloon": spec(L=4.56, W=1.80, tyre=0.325, tyre_w=0.215, fa=-1.36, ra=1.36, drive="front", spokes=5,
        top=[(-2.28, 0.74), (-2.15, 0.80), (-0.86, 0.985), (-0.18, 1.395), (0.32, 1.44), (0.92, 1.405), (1.58, 1.035), (2.14, 1.00), (2.28, 0.90)],
        belt=[(-2.28, 0.70), (-0.86, 0.905), (1.58, 0.975), (2.28, 0.88)], bottom=[(-2.28, 0.31), (-1.9, 0.20), (1.9, 0.21), (2.28, 0.36)],
        halfw=[(-2.28, 0.78), (-1.7, 0.885), (-1.0, 0.90), (1.2, 0.90), (1.9, 0.875), (2.28, 0.78)], roofw=[(-2.28, 0.46), (-0.86, 0.72), (-0.18, 0.62), (0.32, 0.63), (0.92, 0.61), (1.58, 0.71), (2.28, 0.5)],
        cowl=-0.86, roofF=-0.18, roofR=0.92, deck=1.58, door0=-0.70, doorB=0.40, door1=1.20, win0=-0.56, winC=0.98),
    # The boxy older saloon that does every taxi job in the town: upright screen, long bonnet, chrome, hubcaps.
    "OldSaloon": spec(L=4.72, W=1.74, tyre=0.315, tyre_w=0.185, fa=-1.42, ra=1.40, hubcap=True, wood=True, seat_tan=True, lamp="square",
        top=[(-2.36, 0.76), (-2.26, 0.85), (-0.80, 0.97), (-0.30, 1.375), (0.36, 1.41), (1.02, 1.385), (1.50, 1.01), (2.26, 0.985), (2.36, 0.87)],
        belt=[(-2.36, 0.74), (-0.80, 0.90), (1.50, 0.93), (2.36, 0.85)], bottom=[(-2.36, 0.30), (-2.05, 0.22), (2.0, 0.23), (2.36, 0.34)],
        halfw=[(-2.36, 0.81), (-2.16, 0.86), (-1.0, 0.87), (1.3, 0.87), (2.12, 0.85), (2.36, 0.80)], roofw=[(-2.36, 0.52), (-0.80, 0.72), (-0.30, 0.655), (0.36, 0.665), (1.02, 0.655), (1.50, 0.72), (2.36, 0.56)],
        rows_front=[(0.0, 0.0), (0.45, 0.002), (0.75, 0.008), (0.92, 0.02), (0.98, 0.05), (1.0, 0.14)], rows_rear=[(0.0, 0.0), (0.5, 0.003), (0.8, 0.01), (0.94, 0.03), (0.99, 0.06), (1.0, 0.14)],
        cowl=-0.80, roofF=-0.30, roofR=1.02, deck=1.50, door0=-0.66, doorB=0.36, door1=1.26, win0=-0.56, winC=1.06, extras=["chrome_bumpers", "taxi_sign"]),
    # A small hatchback: short, tall, nearly no tail.
    "Hatch": spec(L=3.92, W=1.72, tyre=0.305, tyre_w=0.185, fa=-1.25, ra=1.21, drive="front", spokes=4,
        top=[(-1.96, 0.72), (-1.84, 0.80), (-0.82, 0.985), (-0.24, 1.42), (0.28, 1.47), (1.12, 1.40), (1.76, 1.03), (1.96, 0.85)],
        belt=[(-1.96, 0.70), (-0.82, 0.92), (1.76, 1.02), (1.96, 0.84)], bottom=[(-1.96, 0.31), (-1.66, 0.19), (1.64, 0.20), (1.96, 0.38)],
        halfw=[(-1.96, 0.74), (-1.52, 0.85), (-1.0, 0.86), (1.1, 0.86), (1.62, 0.84), (1.96, 0.75)], roofw=[(-1.96, 0.44), (-0.82, 0.70), (-0.24, 0.625), (0.28, 0.635), (1.12, 0.60), (1.76, 0.68), (1.96, 0.55)],
        rows_rear=[(0.0, 0.0), (0.42, 0.003), (0.74, 0.012), (0.91, 0.03), (0.98, 0.07), (1.0, 0.16)],
        cowl=-0.82, roofF=-0.24, roofR=1.12, deck=1.76, door0=-0.66, doorB=0.28, door1=1.04, win0=-0.54, winC=1.14),
    # A tall off-roader: square shoulders, roof rack, chunky tyres and black arches.
    "OffRoader": spec(L=4.68, W=1.90, tyre=0.385, tyre_w=0.255, fa=-1.42, ra=1.42, spokes=6, lamp="square", seat_up=0.04,
        top=[(-2.34, 0.92), (-2.20, 1.06), (-0.95, 1.20), (-0.52, 1.76), (0.40, 1.80), (1.96, 1.74), (2.28, 1.30), (2.34, 1.10)],
        belt=[(-2.34, 0.92), (-0.95, 1.14), (2.28, 1.20), (2.34, 1.05)], bottom=[(-2.34, 0.46), (-2.0, 0.34), (2.0, 0.34), (2.34, 0.48)],
        halfw=[(-2.34, 0.86), (-2.0, 0.93), (-1.0, 0.95), (1.5, 0.95), (2.12, 0.93), (2.34, 0.86)], roofw=[(-2.34, 0.6), (-0.95, 0.80), (-0.52, 0.72), (0.40, 0.74), (1.96, 0.72), (2.28, 0.79), (2.34, 0.66)],
        rows_front=[(0.0, 0.0), (0.45, 0.002), (0.75, 0.01), (0.92, 0.03), (0.98, 0.06), (1.0, 0.16)], rows_rear=[(0.0, 0.0), (0.5, 0.002), (0.8, 0.008), (0.94, 0.025), (0.99, 0.05), (1.0, 0.12)],
        cowl=-0.95, roofF=-0.52, roofR=1.96, deck=2.28, door0=-0.84, doorB=0.28, door1=1.12, win0=-0.72, winC=1.86, extras=["roof_rack", "side_steps"]),
    # A double-cab pickup: the region's most common vehicle. The bed is really hollow.
    "Pickup": spec(L=5.25, W=1.85, tyre=0.37, tyre_w=0.245, fa=-1.60, ra=1.52, spokes=6, lamp="square", seat_up=0.04,
        top=[(-2.62, 0.90), (-2.50, 1.02), (-1.14, 1.18), (-0.66, 1.72), (0.0, 1.76), (0.72, 1.72), (0.86, 1.20), (2.62, 1.18)],
        belt=[(-2.62, 0.90), (-1.14, 1.11), (0.86, 1.15), (2.62, 1.15)], bottom=[(-2.62, 0.44), (-2.3, 0.36), (2.3, 0.40), (2.62, 0.50)],
        halfw=[(-2.62, 0.84), (-2.3, 0.90), (-1.2, 0.925), (2.4, 0.925), (2.62, 0.90)], roofw=[(-2.62, 0.6), (-1.14, 0.78), (-0.66, 0.72), (0.0, 0.73), (0.72, 0.72), (0.86, 0.84), (2.62, 0.84)],
        rows_front=[(0.0, 0.0), (0.45, 0.002), (0.75, 0.01), (0.92, 0.03), (0.98, 0.06), (1.0, 0.16)], rows_rear=[(0.0, 0.0), (0.5, 0.0), (0.9, 0.002), (0.985, 0.005), (0.995, 0.01), (1.0, 0.03)],
        cowl=-1.14, roofF=-0.66, roofR=0.72, deck=0.86, door0=-1.00, doorB=-0.04, door1=0.72, win0=-0.88, winC=0.64, bed=(0.92, 2.56, 0.80), extras=["tow_bar"]),
    # A people-carrier: no bonnet to speak of, a high flat roof, a stripe down its side. Bus stop traffic.
    "Minibus": spec(L=4.95, W=1.86, tyre=0.33, tyre_w=0.205, fa=-1.68, ra=1.56, hubcap=True, drive="rear", lamp="square", seat_tan=True, seat_up=0.26,
        top=[(-2.48, 0.86), (-2.38, 1.02), (-1.90, 1.26), (-1.05, 2.02), (0.2, 2.10), (2.3, 2.02), (2.48, 1.3)],
        belt=[(-2.48, 0.82), (-1.90, 1.18), (2.48, 1.24)], bottom=[(-2.48, 0.40), (-2.2, 0.26), (2.2, 0.27), (2.48, 0.42)],
        halfw=[(-2.48, 0.82), (-2.1, 0.92), (-1.6, 0.93), (1.9, 0.93), (2.48, 0.86)], roofw=[(-2.48, 0.62), (-1.9, 0.82), (-1.05, 0.76), (0.2, 0.77), (2.3, 0.75), (2.48, 0.72)],
        rows_front=[(0.0, 0.0), (0.45, 0.002), (0.75, 0.01), (0.92, 0.03), (0.98, 0.06), (1.0, 0.16)], rows_rear=[(0.0, 0.0), (0.5, 0.002), (0.8, 0.006), (0.94, 0.02), (0.99, 0.04), (1.0, 0.09)],
        cowl=-1.90, roofF=-1.05, roofR=2.30, deck=2.47, door0=-1.70, doorB=-0.82, door1=0.20, win0=-1.46, winC=2.20, extras=["stripe"]),
}

# ============================================================================ the grid
class Grid: pass

def stations(s):
    L = s["L"]; ys = set()
    def add(*v):
        for y in v: ys.add(round(y, 4))
    nose, tail = -L / 2, L / 2
    for xf, d in s["rows_front"]: add(nose + d)
    for xf, d in s["rows_rear"]: add(tail - d)
    for axle in (s["fa"], s["ra"]):
        r = s["tyre"] + 0.055
        for i in range(0, 9): add(axle + r * math.cos(math.pi * i / 8))
        add(axle - r - 0.006, axle + r + 0.006)
    add(s["cowl"], s["roofF"], s["roofR"], s["deck"], s["win0"], s["winC"])
    for y in (s["cowl"], s["deck"]): add(y - 0.05)                 # hood and deck lid seams sit here
    for y in (s["door0"], s["doorB"], s["door1"]): add(y - 0.0035, y + 0.0035)
    add(s["doorB"] - 0.07, s["doorB"] + 0.07)                      # the B pillar between the side windows
    add((s["roofF"] + s["roofR"]) / 2, (s["cowl"] + s["roofF"]) / 2, (s["roofR"] + s["deck"]) / 2)
    if s["bed"]:
        add(s["bed"][0], s["bed"][1], s["bed"][0] + 0.02, s["bed"][1] - 0.02)
    y = nose
    while y < tail:                                                # never a long empty span: the surface would bow
        y += 0.34; add(y)
    out = sorted(v for v in ys if nose <= v <= tail)
    merged = [out[0]]
    protected = {round(y, 4) for y in (s["door0"] - 0.0035, s["door0"] + 0.0035, s["doorB"] - 0.0035, s["doorB"] + 0.0035, s["door1"] - 0.0035, s["door1"] + 0.0035)}
    for v in out[1:]:
        if v - merged[-1] < 0.022 and v not in protected and merged[-1] not in protected and v != out[-1]: continue
        merged.append(v)
    return merged

def closure(s, y):
    """How much of the half-width a station has at the two ends, where the body turns in and closes on the centre line."""
    L = s["L"]; nose, tail = -L / 2, L / 2
    for rows, base, sign in ((s["rows_front"], nose, 1), (s["rows_rear"], tail, -1)):
        d = (y - base) * sign
        if d < rows[-1][1] - 1e-5:
            for (xa, da), (xb, db) in zip(rows, rows[1:]):
                if d <= db + 1e-6: return xa + (xb - xa) * (d - da) / max(1e-6, db - da)
    return 1.0

def arch_height(s, y):
    best = 0.0
    for axle in (s["fa"], s["ra"]):
        r = s["tyre"] + 0.055; dy = y - axle
        if abs(dy) < r: best = max(best, s["tyre"] + math.sqrt(r * r - dy * dy))
    return best

def section(s, y):
    """The half ring at y, from the centre of the floor to the centre of the roof. x >= 0."""
    bottom, belt, top = curve(s["bottom"], y), curve(s["belt"], y), curve(s["top"], y)
    w, roof = curve(s["halfw"], y), curve(s["roofw"], y)
    arch = arch_height(s, y)
    h = max(0.0, top - belt)
    # Over a wheel the side swells a little: the arch's flare.
    near = min(abs(y - s["fa"]), abs(y - s["ra"]))
    flare = 0.014 * max(0.0, 1.0 - near / (s["tyre"] + 0.25))
    w += flare
    low = max(bottom, arch)
    pts = [None] * RINGS
    side = belt - bottom
    z_seam = max(bottom + 0.095, min(low + 0.03, belt - 0.14) if arch else bottom + 0.095)
    pts[R_BOTTOM] = (0.0, low + (0.02 if arch else 0.0))
    pts[R_FLOOR] = (w * 0.80, low + (0.01 if arch else 0.0))
    pts[R_LIP] = (w * 0.985, low + 0.012)
    pts[R_SEAMA] = (w, max(z_seam, low + 0.028))
    pts[R_SEAMB] = (w, max(z_seam + 0.008, low + 0.036))
    pts[R_DOOR] = (w * 1.0, max(bottom + side * 0.46, low + 0.05))
    pts[R_CREASE] = (w * 1.002, max(bottom + side * 0.76, low + 0.065))
    pts[R_BELT] = (w * 0.985, belt - 0.015)
    xb = w * 0.955
    pts[R_WIN0] = (xb, belt + 0.004)
    rx = min(roof, xb)
    f9, f10 = (0.91, 0.985) if h > 0.12 else (0.55, 0.85)
    pts[R_WIN1] = (xb + (rx - xb) * 0.90, belt + h * f9)
    pts[R_RAIL] = (rx, belt + h * f10)
    pts[R_INNER] = (rx * 0.52, belt + h * 0.998)
    pts[R_TOP] = (0.0, belt + h)
    # Off the cabin, the two rings at the belt are the thin shut line down each side of the bonnet and the lid.
    if h <= 0.12 or not (s["cowl"] <= y <= s["deck"]):
        pts[R_WIN1] = (xb - 0.009, belt + 0.006)
        pts[R_RAIL] = (rx + (xb - rx) * 0.45, belt + h * 0.62)
    # At the two ends the rings are spaced for what is there: from the ground up, chin, intake, bumper, then the band of lamps and grille.
    close_now = closure(s, y)
    if close_now < 1.0:
        t = min(1.0, (1.0 - close_now) / 0.12)
        front = y < 0
        marks = {R_SEAMA: 0.10, R_SEAMB: 0.27 if front else 0.16, R_DOOR: 0.44 if front else 0.34, R_CREASE: 0.60 if front else 0.54}
        for k, f in marks.items():
            x, z = pts[k]
            pts[k] = (x, z + (bottom + side * f - z) * t)
    # The pickup's bed: the top of the grid sinks to the floor of the load space.
    if s["bed"] and s["bed"][0] < y < s["bed"][1]:
        floor = s["bed"][2]; inner = w - 0.075
        pts[R_WIN1] = (inner, belt + 0.03)
        pts[R_RAIL] = (inner - 0.002, floor)
        pts[R_INNER] = (inner * 0.5, floor)
        pts[R_TOP] = (0.0, floor)
    close = closure(s, y)
    return [(x * close, z) for x, z in pts]

def cell(s, y0, y1, k, xf):
    """Which part and surface one cell of the grid is: (part, surface). Between stations y0, y1; rings k, k+1; xf = its share of the half-width at the end rows."""
    mid = (y0 + y1) * 0.5
    front, rear = -s["L"] / 2 + s["rows_front"][-1][1], s["L"] / 2 - s["rows_rear"][-1][1]
    in_door = None
    if s["door0"] < mid < s["doorB"]: in_door = "F"
    elif s["doorB"] < mid < s["door1"]: in_door = "R"
    seam_v = min(abs(mid - s["door0"]), abs(mid - s["doorB"]), abs(mid - s["door1"])) < 0.0036
    arch_near = arch_height(s, y0) > 0 or arch_height(s, y1) > 0
    # Underneath and inside the arches.
    if k < R_LIP: return ("Body", "Under")
    if k == R_LIP: return ("Body", "Trim" if s.get("clad") or "side_steps" in s["extras"] else "Under")
    # The ends: bumpers, grille, lamps, plates, by height and width.
    if mid < front:
        chrome = "chrome_bumpers" in s["extras"]
        if k == R_SEAMA: return ("Body", "Trim")
        if k == R_SEAMB: return ("Body", "Grille") if xf < 0.72 else ("Body", "Trim" if s["lamp"] == "square" else "Paint")
        if k == R_DOOR:
            if xf < 0.20: return ("Body", "Plate")
            return ("Body", "Chrome" if chrome else "Paint")
        if k == R_CREASE:
            if xf < 0.60: return ("Body", "Grille")
            if xf < 0.97: return ("Body", "HeadLamp")
            return ("Body", "Paint")
        if k == R_BELT or k == R_WIN0: return ("Body", "Paint")
        return ("Body", "Paint")
    if mid > rear:
        chrome = "chrome_bumpers" in s["extras"]
        if k == R_SEAMA: return ("Body", "Trim")
        if k == R_SEAMB: return ("Body", "Chrome" if chrome else "Trim")
        if k == R_DOOR:
            if xf < 0.22: return ("Body", "Plate")
            return ("Body", "Paint")
        if k == R_CREASE:
            if 0.44 < xf < 0.985: return ("Body", "TailLamp" if xf < 0.90 else "Amber")
            return ("Body", "Paint")
        return ("Body", "Paint")
    # The pickup's bed.
    if s["bed"] and s["bed"][0] < mid < s["bed"][1] and k >= R_WIN1: return ("Body", "Under" if k > R_WIN1 else "Trim")
    # Glass in the greenhouse. The screens span from the pillar to the centre line.
    if k >= R_RAIL and s["cowl"] < mid < s["roofF"]: return ("Glass_Front", "Glass")
    if k >= R_RAIL and s["roofR"] < mid < s["deck"]: return ("Glass_Rear", "Glass")
    greenhouse = s["cowl"] < mid < s["deck"]
    if k == R_WIN0 and greenhouse:
        pillar = abs(mid - s["doorB"]) < 0.07
        if s["win0"] < mid < s["winC"] and not pillar:
            door = "F" if mid < s["doorB"] else "R"
            if s["doors"] == 2 and door == "R": return ("Body", "Trim")
            return (f"Glass_{door}", "Glass")
        return ("Body", "Trim" if pillar or mid < s["win0"] else "Paint")
    if k == R_BELT and greenhouse: return (door_part(in_door), "Trim") if in_door else ("Body", "Trim")
    if k == R_WIN1 and greenhouse:
        if abs(mid - s["doorB"]) < 0.07: return ("Body", "Trim")
        return (door_part(in_door), "Paint") if in_door and s["win0"] < mid < s["winC"] else ("Body", "Paint")
    # Door cut lines.
    if seam_v and R_SEAMA <= k <= R_WIN0: return ("Body", "Seam")
    if in_door and k == R_SEAMA and not arch_near: return ("Body", "Seam")
    if in_door and R_SEAMB <= k <= R_CREASE: return (door_part(in_door), "Paint")
    # Bonnet and lid outlines.
    if abs(mid - (s["cowl"] - 0.025)) < 0.026 and k >= R_WIN0: return ("Body", "Seam")
    if abs(mid - (s["deck"] - 0.025)) < 0.026 and k >= R_WIN0 and not s["bed"]: return ("Body", "Paint")
    if (mid < s["cowl"] or mid > s["deck"]) and k == R_WIN0: return ("Body", "Seam")
    return ("Body", "Paint")

def door_part(which): return "Door_" + which + "*"          # * = this side, filled in when the cell is mirrored

# ============================================================================ atlas helpers
def cell_uv(name, u=0.5, v=0.5):
    index = CELLS[name][0]; cx, cy = index % GRID, index // GRID
    # Image row 0 is the top; UV v runs up.
    margin = 0.02
    uu = (cx + margin + (1 - 2 * margin) * u) / GRID
    vv = (GRID - 1 - cy + margin + (1 - 2 * margin) * v) / GRID
    return (uu, vv)

# ============================================================================ building surfaces
def build_surface(s, levels):
    """Returns evaluated mesh data: verts, normals, faces [(indices, part, surface)]."""
    ys = stations(s)
    rows = [section(s, y) for y in ys]
    bm = bmesh.new()
    index_right = {}; index_left = {}
    def vert(si, k, side):
        x, z = rows[si][k]; y = ys[si]
        if abs(x) < 1e-5:                       # on the centre line both sides share it
            key = (si, k)
            v = index_right.get(key)
            if v is None: v = index_right[key] = bm.verts.new((0.0, y, z))
            return v
        table = index_right if side > 0 else index_left
        key = (si, k)
        v = table.get(key)
        if v is None: v = table[key] = bm.verts.new((x * side, y, z))
        return v
    slots = {}
    def slot(part, surface):
        name = part + "|" + surface
        if name not in slots: slots[name] = len(slots)
        return slots[name]
    crease = bm.edges.layers.float.new("crease_edge")
    for si in range(len(ys) - 1):
        for side in (1, -1):
            for k in range(RINGS - 1):
                a, b, c, d = vert(si, k, side), vert(si + 1, k, side), vert(si + 1, k + 1, side), vert(si, k + 1, side)
                if len({a, b, c, d}) < 3: continue
                vs = [a, b, c, d]
                unique = []
                for v in vs:
                    if v not in unique: unique.append(v)
                if len(unique) < 3: continue
                try: f = bm.faces.new(unique)
                except ValueError: continue
                xf = (abs(rows[si][k][0]) + abs(rows[si + 1][k][0])) * 0.5 / max(0.01, curve(s["halfw"], (ys[si] + ys[si + 1]) * 0.5))
                part, surface = cell(s, ys[si], ys[si + 1], k, xf)
                # Each side's own door and window.
                part = part.replace("*", "L" if side > 0 else "R")
                if part in ("Glass_F", "Glass_R"): part += "L" if side > 0 else "R"
                f.material_index = slot(part, surface)
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # Hard lines: the arch lip, the shoulder crease, the belt, round the window glass.
    for e in bm.edges:
        cut = 0.0
        for table in (index_right, index_left):
            pass
        faces = e.link_faces
        if len(faces) == 2 and faces[0].material_index != faces[1].material_index: cut = 0.65
        e[crease] = cut
    for table in (index_right, index_left):
        for (si, k), v in table.items():
            if k in (R_LIP, R_CREASE):
                other = table.get((si + 1, k))
                if other is None: continue
                e = bm.edges.get((v, other))
                if e: e[crease] = max(e[crease], 0.9 if k == R_LIP else 0.5)
    me = bpy.data.meshes.new("cage")
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("cage", me); bpy.context.scene.collection.objects.link(ob)
    if levels > 0:
        mod = ob.modifiers.new("sub", "SUBSURF"); mod.levels = levels; mod.render_levels = levels; mod.boundary_smooth = "ALL"
    # material_index is only kept through the modifier if the slots exist.
    for name in slots:
        me.materials.append(bpy.data.materials.get(name) or bpy.data.materials.new(name))
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    em = bpy.data.meshes.new_from_object(ev)
    names = {i: n for n, i in slots.items()}
    normals = [tuple(v.normal) for v in em.vertices]
    verts = [tuple(v.co) for v in em.vertices]
    faces = [(list(p.vertices), names[p.material_index]) for p in em.polygons]
    bpy.data.objects.remove(ob, do_unlink=True); bpy.data.meshes.remove(me); bpy.data.meshes.remove(em)
    return verts, normals, faces

class Part:
    """Geometry gathered for one object: faces with a material slot and per-corner normals and UVs."""
    def __init__(self, name, origin=(0, 0, 0), local=False):
        self.name, self.origin, self.local = name, Vector(origin), local     # local: its points are already about its origin (wheels, the steering wheel, needles)
        self.verts, self.polys, self.uvs, self.norms, self.mats = [], [], [], [], []
    def poly(self, pts, normals, uvs, mat):
        base = len(self.verts)
        self.verts.extend(pts); self.polys.append(list(range(base, base + len(pts))))
        self.uvs.extend(uvs); self.norms.extend(normals); self.mats.append(mat)

SLOTS = ["Paint", "Atlas", "Glass"]

def surface_uv(surface, p, bb):
    if surface == "Paint": return (p[1] / 2.5, p[2] / 2.5)
    if surface == "Glass": return (p[1], p[2])
    # A patterned cell maps over the piece's own width and height (bb is that surface's bounds on the car); a flat one to the cell's middle.
    if surface in ("Plate", "Grille", "HeadLamp", "TailLamp", "Taxi"):
        x0, x1, z0, z1 = bb
        return cell_uv(surface, min(1, max(0, (abs(p[0]) - x0) / max(0.01, x1 - x0))), min(1, max(0, (p[2] - z0) / max(0.01, z1 - z0))))
    return cell_uv(surface)

def add_surface(parts, verts, normals, faces, prefix=""):
    bounds = {}
    for vs, name in faces:
        _, surface = name.split("|")
        b = bounds.get(surface)
        for i in vs:
            x, _, z = verts[i]; x = abs(x)
            if b is None: b = bounds[surface] = [x, x, z, z]
            b[0] = min(b[0], x); b[1] = max(b[1], x); b[2] = min(b[2], z); b[3] = max(b[3], z)
    for vs, name in faces:
        part, surface = name.split("|")
        key = prefix + part
        if key not in parts: parts[key] = Part(key)
        pts = [verts[i] for i in vs]
        mat = 0 if surface == "Paint" else 2 if surface == "Glass" else 1
        parts[key].poly(pts, [normals[i] for i in vs], [surface_uv(surface, p, bounds[surface]) for p in pts], mat)

def add_shell(part, verts, normals, faces, s):
    """The inside of the skin, 3 cm in, facing the cabin: door cards, pillars, headliner, floor. Only inside the cabin's length."""
    for vs, name in faces:
        p, surface = name.split("|")
        if surface == "Glass": continue
        mid_y = sum(verts[i][1] for i in vs) / len(vs)
        if not (s["cowl"] - 0.10 < mid_y < (s["deck"] + 0.02 if not s["bed"] else s["roofR"] + 0.1)): continue
        mid_z = sum(verts[i][2] for i in vs) / len(vs)
        belt = curve(s["belt"], mid_y); top = curve(s["top"], mid_y)
        if mid_z > belt + 0.03: atlas = "Liner"
        elif mid_z < curve(s["bottom"], mid_y) + 0.07: atlas = "Carpet"
        else: atlas = "SeatTan" if s["seat_tan"] and mid_z > belt - 0.32 else "Card"
        pts = []; ns = []
        for i in reversed(vs):
            n = normals[i]; v = verts[i]
            pts.append((v[0] - n[0] * 0.03, v[1] - n[1] * 0.03, v[2] - n[2] * 0.03)); ns.append((-n[0], -n[1], -n[2]))
        target = part
        if p.startswith("Door_"): target = parts_card(part, p)
        target.poly(pts, ns, [cell_uv(atlas)] * len(pts), 1)

_cards = {}
def parts_card(fallback, door):
    return _cards.get(door, fallback)

# ---------------------------------------------------------------------------- bits and pieces, each a smooth-shaded solid in the atlas
def box(part, c, s, cell_name, bevel=0.012, seg=2, tilt=None, mat=1):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=s, verts=bm.verts)
    if bevel > 0: bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(s) * 0.45), segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)
    if tilt: bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=tilt, verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(c), verts=bm.verts)
    flush(bm, part, cell_name, mat); return part

def cyl(part, c, r, h, cell_name, seg=20, axis="Y", r2=None, tilt=None, mat=1):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
    if axis != "Z": bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X" if axis == "Y" else "Y"), verts=bm.verts)
    if tilt: bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=tilt, verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(c), verts=bm.verts)
    flush(bm, part, cell_name, mat, sharp=0.6); return part

def lathe(part, profile, cell_names, seg=36, axis_x=True, offset=(0, 0, 0), rim_mirror=1):
    """Turned about X (a wheel's axle). profile: (radius, x) in order; one cell name per segment of the profile."""
    bm = bmesh.new(); rings = []
    for r, x in profile:
        rings.append([bm.verts.new((x * rim_mirror, r * math.cos(k * math.tau / seg), r * math.sin(k * math.tau / seg))) for k in range(seg)])
    tags = {}
    for i in range(len(rings) - 1):
        for k in range(seg):
            f = bm.faces.new((rings[i][k], rings[i][(k + 1) % seg], rings[i + 1][(k + 1) % seg], rings[i + 1][k]))
            tags[f] = cell_names[min(i, len(cell_names) - 1)]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    for f in bm.faces:
        name = tags[f]
        corners = [v.co + Vector(offset) for v in f.verts]
        ns = [tuple(v.normal) for v in f.verts]
        if name == "Tread":
            uv = [cell_uv("Tread", k % 2, min(1.0, max(0.0, 0.5 + v.co.x * 2))) for k, v in enumerate(f.verts)]
        else: uv = [cell_uv(name)] * len(corners)
        part.poly([tuple(c) for c in corners], ns, uv, 1)
    bm.free()

def flush(bm, part, cell_name, mat=1, sharp=0.5):
    bm.normal_update()
    for f in bm.faces:
        ns = []
        for loop in f.loops:
            # Smooth within a face angle; a hard turn stays hard.
            n = Vector((0, 0, 0))
            for other in loop.vert.link_faces:
                if other.normal.dot(f.normal) > math.cos(sharp): n += other.normal * other.calc_area()
            ns.append(tuple(n.normalized() if n.length > 1e-8 else f.normal))
        part.poly([tuple(l.vert.co) for l in f.loops], ns, [cell_uv(cell_name)] * len(f.loops), mat)
    bm.free()

def loft(part, sections, cell_name, closed=True, mat=1):
    """A solid through a list of closed rings of equal length (a seat's bolster, the dash's top)."""
    bm = bmesh.new()
    rings = [[bm.verts.new(p) for p in ring] for ring in sections]
    n = len(rings[0])
    for i in range(len(rings) - 1):
        for k in range(n):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % n], rings[i + 1][(k + 1) % n], rings[i + 1][k]))
    if closed: bm.faces.new(rings[0]); bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    flush(bm, part, cell_name, mat, sharp=0.75)

def tube(part, points, r, cell_name, seg=10, mat=1):
    rings = []
    for i, p in enumerate(points):
        p = Vector(p); a = Vector(points[max(i - 1, 0)]); b = Vector(points[min(i + 1, len(points) - 1)])
        t = (b - a).normalized(); n = t.orthogonal().normalized(); bi = t.cross(n)
        rings.append([tuple(p + (n * math.cos(k * math.tau / seg) + bi * math.sin(k * math.tau / seg)) * r) for k in range(seg)])
    loft(part, rings, cell_name, closed=True, mat=mat)

# ---------------------------------------------------------------------------- wheels
def wheel(name, s, side, axle_y, simple=False):
    """Origin on the axle. The outer face towards +X on the car's +X side."""
    r = s["tyre"]; w = s["tyre_w"]; rim = r * 0.64
    z = r
    x = curve(s["halfw"], axle_y) - w * 0.5 + 0.012
    part = Part(name, (x * side, axle_y, z), local=True)
    seg = 14 if simple else 40
    out = side
    # Tyre: tread with a rounded shoulder, then the wall in to the rim's lip.
    prof = [(rim, -w * 0.5), (r * 0.92, -w * 0.5), (r * 0.985, -w * 0.46), (r, -w * 0.38), (r, w * 0.38), (r * 0.985, w * 0.46), (r * 0.92, w * 0.5), (rim, w * 0.5)]
    lathe(part, prof, ["Sidewall", "Sidewall", "Sidewall", "Tread", "Sidewall", "Sidewall", "Sidewall"], seg=seg, rim_mirror=out)
    if simple:
        lathe(part, [(rim, w * 0.5), (rim * 0.5, w * 0.4), (0.0001, w * 0.42)], ["Hubcap" if s["hubcap"] else "Alloy"], seg=seg, rim_mirror=out)
        return part
    if s["hubcap"]:
        # A pressed steel wheel and a dished plastic cap.
        lathe(part, [(rim, w * 0.5), (rim * 0.96, w * 0.52), (rim * 0.90, w * 0.34), (rim * 0.86, w * 0.30), (rim * 0.75, w * 0.43), (rim * 0.3, w * 0.48), (rim * 0.24, w * 0.46), (0.0001, w * 0.47)],
              ["Hubcap", "Hubcap", "Trim", "Hubcap", "Hubcap", "Trim", "Hubcap"], seg=seg, rim_mirror=out)
    else:
        lathe(part, [(rim, w * 0.5), (rim * 1.03, w * 0.53), (rim * 0.94, w * 0.5), (rim * 0.92, -w * 0.3), (0.0001, -w * 0.3)], ["Alloy", "Alloy", "Trim", "Trim"], seg=seg, rim_mirror=out)
        lathe(part, [(rim * 0.22, w * 0.34), (rim * 0.22, w * 0.42), (rim * 0.14, w * 0.44), (0.0001, w * 0.44)], ["Alloy", "Alloy", "Trim"], seg=20, rim_mirror=out)
        n = s["spokes"]
        for i in range(n):
            a = i * math.tau / n
            for d in (-0.12, 0.12):                            # each spoke forks towards the rim
                p0 = (out * w * 0.36, math.cos(a) * rim * 0.16, math.sin(a) * rim * 0.16)
                p1 = (out * w * 0.42, math.cos(a + d) * rim * 0.62, math.sin(a + d) * rim * 0.62)
                p2 = (out * w * 0.48, math.cos(a + d * 1.5) * rim * 0.95, math.sin(a + d * 1.5) * rim * 0.95)
                tube(part, [p0, p1, p2], 0.012, "Alloy", seg=6)
        cyl(part, (out * w * 0.05, 0, 0), rim * 0.68, 0.016, "Steel", seg=28, axis="X")     # the brake disc behind it
    return part

# ---------------------------------------------------------------------------- inside
def interior(s, inner):
    cowl, roofF = s["cowl"], s["roofF"]
    belt0 = curve(s["belt"], cowl + 0.25); floor = curve(s["bottom"], 0.0) + 0.06
    w = curve(s["halfw"], 0.0)
    dash_y = cowl + 0.34 if s["cowl"] > -1.5 else cowl + 0.42     # the dash's rear face
    dash_top = belt0 + 0.03
    seat_x = w * 0.43
    seat_y = dash_y + 0.84                                         # hips
    seat_z = floor + 0.26 + s.get("seat_up", 0.0)
    trim = "DashTan" if s["seat_tan"] else "Dash"
    # The dash: a soft top that runs from door to door and into the screen's foot, a lower face over the knees, a hooded binnacle.
    tilt = lambda deg: Matrix.Rotation(math.radians(deg), 3, "X")
    def ring(y, z0, z1, half):
        return [(half, y, z0), (half, y, z1 - 0.02), (half - 0.03, y, z1), (-half + 0.03, y, z1), (-half, y, z1 - 0.02), (-half, y, z0)]
    half = w - 0.05
    loft(inner, [ring(dash_y - 0.42, floor + 0.28, dash_top - 0.01, half), ring(dash_y - 0.18, floor + 0.26, dash_top + 0.015, half), ring(dash_y - 0.03, floor + 0.28, dash_top, half), ring(dash_y + 0.02, floor + 0.30, dash_top - 0.055, half)], trim)
    # Under the dash: the knee roll and firewall, carpeted.
    box(inner, (0, dash_y - 0.32, floor + 0.13), (half * 2, 0.08, 0.34), "Carpet", bevel=0.01, seg=1)
    if s["wood"]:
        box(inner, (0, dash_y + 0.024, dash_top - 0.095), (half * 1.96, 0.012, 0.05), "Wood", bevel=0.004, seg=1)
    # Binnacle hood over the dials, in front of the driver.
    dial_z = dash_top - 0.052
    box(inner, (seat_x, dash_y - 0.035, dash_top + 0.01), (0.34, 0.20, 0.09), trim, bevel=0.03, seg=3)
    box(inner, (seat_x, dash_y + 0.015, dial_z), (0.31, 0.012, 0.115), "Trim", bevel=0.003, seg=1)
    # The dial face, the width of two cells' dials, at the driver: a single quad mapped on the Dials cell.
    a, b = 0.145, 0.0725
    y = dash_y + 0.0235
    p = [(seat_x + a, y, dial_z - b), (seat_x - a, y, dial_z - b), (seat_x - a, y, dial_z + b), (seat_x + a, y, dial_z + b)]
    inner.poly(p, [(0, 1, 0)] * 4, [cell_uv("Dials", 0, 0.25), cell_uv("Dials", 1, 0.25), cell_uv("Dials", 1, 0.75), cell_uv("Dials", 0, 0.75)], 1)
    # Vents and a centre stack.
    for vx in (w * 0.76, -w * 0.76, 0.09, -0.09):
        box(inner, (vx, dash_y + 0.022, dash_top - 0.052), (0.10, 0.012, 0.045), "Trim", bevel=0.003, seg=1)
    box(inner, (0, dash_y + 0.03, dash_top - 0.15), (0.21, 0.03, 0.17), "Trim", bevel=0.006, seg=1)
    p = [(0.092, dash_y + 0.046, dash_top - 0.225), (-0.092, dash_y + 0.046, dash_top - 0.225), (-0.092, dash_y + 0.046, dash_top - 0.075), (0.092, dash_y + 0.046, dash_top - 0.075)]
    inner.poly(p, [(0, 1, 0)] * 4, [cell_uv("Radio", 0, 0), cell_uv("Radio", 1, 0), cell_uv("Radio", 1, 1), cell_uv("Radio", 0, 1)], 1)
    # The tunnel, the lever and its boot.
    box(inner, (0, dash_y + 0.38, floor + 0.12), (0.22, 0.86, 0.26), "Trim", bevel=0.03, seg=2)
    tube(inner, [(0, dash_y + 0.18, floor + 0.25), (0, dash_y + 0.20, floor + 0.36), (0, dash_y + 0.225, floor + 0.47)], 0.011, "Chrome", seg=8)
    cyl(inner, (0, dash_y + 0.227, floor + 0.49), 0.024, 0.05, "Leather", seg=12, axis="Z")
    cyl(inner, (0, dash_y + 0.18, floor + 0.255), 0.05, 0.03, "Leather", seg=12, axis="Z", r2=0.025)
    # Pedals.
    for px, wide in ((seat_x - 0.10, 0.08), (seat_x - 0.24, 0.06), (seat_x + 0.08, 0.04)):
        box(inner, (px, dash_y - 0.10, floor + 0.09), (wide, 0.012, 0.09), "Steel", bevel=0.004, seg=1, tilt=tilt(-30))
    # Seats: a cushion and a back for each front seat, a bench behind.
    cloth = "SeatTan" if s["seat_tan"] else "SeatDark"
    for sx in (seat_x, -seat_x):
        box(inner, (sx, seat_y, seat_z - 0.05), (0.48, 0.50, 0.13), cloth, bevel=0.05, seg=3, tilt=tilt(6))
        box(inner, (sx, seat_y + 0.31, seat_z + 0.27), (0.46, 0.13, 0.62), cloth, bevel=0.05, seg=3, tilt=tilt(-14))
        box(inner, (sx, seat_y + 0.41, seat_z + 0.67), (0.25, 0.10, 0.17), cloth, bevel=0.04, seg=3, tilt=tilt(-8))
        box(inner, (sx, seat_y, seat_z - 0.17), (0.40, 0.44, 0.12), "Trim", bevel=0.01, seg=1)
    if s["deck"] - seat_y > 0.95 and not s["bed"] or (s["bed"] and s["doorB"] < 0.2):
        by = seat_y + 0.78
        if by < (s["bed"][0] - 0.24 if s["bed"] else s["deck"] - 0.2):
            box(inner, (0, by, seat_z - 0.05), (w * 1.7, 0.46, 0.15), cloth, bevel=0.05, seg=3)
            box(inner, (0, by + 0.27, seat_z + 0.25), (w * 1.7, 0.13, 0.55), cloth, bevel=0.05, seg=3, tilt=tilt(-12))
    # Rear-view mirror and the sun visors, at the top of the screen.
    roof_z = curve(s["top"], roofF) - 0.06
    box(inner, (0, roofF + 0.09, roof_z - 0.05), (0.26, 0.018, 0.07), "Trim", bevel=0.008, seg=1, tilt=tilt(-8))
    box(inner, (0, roofF + 0.101, roof_z - 0.05), (0.235, 0.002, 0.05), "Mirror", bevel=0, tilt=tilt(-8))
    for sx in (seat_x, -seat_x):
        box(inner, (sx, roofF + 0.16, roof_z + 0.035), (0.34, 0.10, 0.014), "Liner", bevel=0.006, seg=1)
    eye = (seat_x, seat_y + 0.10, seat_z + 0.70)
    return dict(eye=eye, seat_x=seat_x, dash_y=dash_y, dash_top=dash_top, floor=floor, seat_y=seat_y, seat_z=seat_z, dial_z=dial_z, dial_y=y)

def steer_wheel(s, ins):
    """Its own object, origin on the hub, turned on the column's axis. Rim radius 0.185 m."""
    hub = Vector((ins["seat_x"], ins["dash_y"] + 0.22, ins["dash_top"] - 0.075))
    axis = Vector((0, 1, 0.32)).normalized()                      # the column rakes up towards the driver
    part = Part("SteerWheel", hub, local=True)
    q = axis.to_track_quat("Z", "Y")
    def on_wheel(x, y, z=0.0):
        return tuple(q @ Vector((x, y, z)))
    r = 0.185; seg = 36; tube_r = 0.0145
    # The rim.
    rings = []
    for i in range(seg + 1):
        a = i / seg * math.tau; c = Vector((math.cos(a) * r, math.sin(a) * r, 0)); out = c.normalized()
        rings.append([tuple(q @ (c + out * math.cos(k / 10 * math.tau) * tube_r + Vector((0, 0, 1)) * math.sin(k / 10 * math.tau) * tube_r)) for k in range(10)])
    loft(part, rings, "Leather", closed=False)
    # The boss and three spokes.
    bm_center = (0, 0, -0.012)
    box(part, tuple(q @ Vector(bm_center)), (0.13, 0.11, 0.05), "Leather", bevel=0.018, seg=2, tilt=q.to_matrix())
    for a in (0, 180, 270):
        c, sn = math.cos(math.radians(a)), math.sin(math.radians(a))
        tube(part, [on_wheel(c * 0.06, sn * 0.06, -0.012), on_wheel(c * 0.12, sn * 0.12, -0.01), on_wheel(c * (r - 0.006), sn * (r - 0.006), -0.002)], 0.012, "Trim", seg=8)
    part.grip_l = hub + q @ Vector((r * math.cos(math.radians(10)), r * math.sin(math.radians(10)), 0))    # +X = the driver's left
    part.grip_r = hub + q @ Vector((-r * math.cos(math.radians(10)), r * math.sin(math.radians(10)), 0))
    part.axis = axis
    # The column behind it, fixed.
    return part, hub, axis

def needle(name, centre, y):
    part = Part(name, (centre[0], y, centre[1]), local=True)
    box(part, (0, 0, 0.024), (0.0035, 0.004, 0.052), "Needle", bevel=0)
    cyl(part, (0, 0.002, 0), 0.008, 0.005, "Trim", seg=10, axis="Y")
    return part

# ---------------------------------------------------------------------------- outside
def outside(s, parts):
    body = parts["Body"]
    w0 = curve(s["halfw"], s["cowl"])
    belt = curve(s["belt"], s["cowl"] + 0.2)
    # Door mirrors on both sides, on a stalk off the door by the A pillar.
    for side in (1, -1):
        y = s["win0"] + 0.02
        x = curve(s["halfw"], y) * 0.955
        c = Vector((side * (x + 0.10), y - 0.02, belt + 0.09))
        tube(body, [(side * x, y, belt + 0.03), (side * (x + 0.05), y, belt + 0.06), tuple(c)], 0.012, "Trim", seg=6)
        box(body, tuple(c), (0.19, 0.08, 0.115), "Trim", bevel=0.03, seg=3)
        box(body, tuple(c + Vector((0, 0.041, 0))), (0.165, 0.002, 0.09), "Mirror", bevel=0)
    # Door handles.
    for part_name, y in (("Door_FL", s["doorB"] - 0.13), ("Door_FR", s["doorB"] - 0.13), ("Door_RL", s["door1"] - 0.13), ("Door_RR", s["door1"] - 0.13)):
        side = 1 if part_name.endswith("L") else -1
        if part_name not in parts: continue
        x = curve(s["halfw"], y) + 0.012; z = curve(s["belt"], y) - 0.10
        box(parts[part_name], (side * x, y, z), (0.022, 0.15, 0.028), "Chrome" if "chrome_bumpers" in s["extras"] else "Trim", bevel=0.009, seg=2)
    # Wipers at the screen's foot.
    z = curve(s["top"], s["cowl"]) + 0.012
    for sx, ang in ((0.33, 0.14), (-0.22, 0.10)):
        tube(body, [(sx, s["cowl"] + 0.02, z), (sx - 0.26, s["cowl"] + 0.04 + ang * 0.1, z + 0.02), (sx - 0.50, s["cowl"] + 0.07 + ang * 0.2, z + 0.045)], 0.005, "Trim", seg=5)
    # An exhaust pipe at the rear.
    tail = s["L"] / 2
    z = curve(s["bottom"], tail - 0.25) + 0.015
    cyl(body, (0.48, tail - 0.08, z), 0.032, 0.20, "Steel", seg=10, axis="Y")
    # An aerial on the roof.
    tube(body, [(0, s["roofR"] - 0.05, curve(s["top"], s["roofR"] - 0.05) - 0.005), (0, s["roofR"] + 0.1, curve(s["top"], s["roofR"]) + 0.18)], 0.0035, "Trim", seg=4)
    if "roof_rack" in s["extras"]:
        y0, y1 = s["roofF"] + 0.35, s["roofR"] - 0.15; z = curve(s["top"], 0.4) + 0.075
        rx = curve(s["roofw"], 0.4) - 0.03
        for side in (1, -1):
            tube(body, [(side * rx, y0, z), (side * rx, y1, z)], 0.014, "Rack", seg=6)
            for y in (y0, (y0 + y1) / 2, y1):
                box(body, (side * rx, y, z - 0.04), (0.03, 0.05, 0.08), "Rack", bevel=0.005, seg=1)
        y = y0
        while y <= y1:
            tube(body, [(rx, y, z), (-rx, y, z)], 0.01, "Rack", seg=6); y += 0.3
    if "side_steps" in s["extras"]:
        for side in (1, -1):
            x = curve(s["halfw"], 0) + 0.035
            box(body, (side * x, (s["fa"] + s["ra"]) / 2, curve(s["bottom"], 0) + 0.01), (0.13, s["ra"] - s["fa"] - 1.05, 0.035), "Rack", bevel=0.008, seg=1)
    if "tow_bar" in s["extras"]:
        cyl(body, (0, s["L"] / 2 + 0.07, curve(s["bottom"], s["L"] / 2) + 0.08), 0.02, 0.2, "Steel", seg=8, axis="Y")
        box(body, (0, s["L"] / 2 + 0.0, curve(s["belt"], s["L"] / 2) - 0.24), (1.6, 0.12, 0.11), "Chrome", bevel=0.02, seg=2)
    if "taxi_sign" in s["extras"]:
        sign = parts["Taxi_Sign"] = Part("Taxi_Sign")
        y = (s["roofF"] + s["roofR"]) / 2; z = curve(s["top"], y) + 0.055
        box(sign, (0, y, z), (0.46, 0.13, 0.12), "Trim", bevel=0.012, seg=1)
        hw, hh = 0.21, 0.046
        for fy, order in ((y - 0.067, 1), (y + 0.067, -1)):
            p = [(hw * order, fy, z - hh), (-hw * order, fy, z - hh), (-hw * order, fy, z + hh), (hw * order, fy, z + hh)]
            n = [(0, -1 if order > 0 else 1, 0)] * 4
            if order > 0: p = [p[1], p[0], p[3], p[2]]
            sign.poly(p, n, [cell_uv("Taxi", 0, 0.28), cell_uv("Taxi", 1, 0.28), cell_uv("Taxi", 1, 0.72), cell_uv("Taxi", 0, 0.72)], 1)
    if s["bed"]:
        y0, y1, floor = s["bed"]
        # The headboard and the ribs on the load floor.
        box(body, (0, y0 - 0.005, (floor + curve(s["belt"], y0)) / 2), (curve(s["halfw"], y0) * 1.9, 0.03, curve(s["belt"], y0) - floor), "Under", bevel=0)
        x = -0.6
        while x <= 0.6:
            box(body, (x, (y0 + y1) / 2, floor + 0.008), (0.05, y1 - y0 - 0.06, 0.016), "Under", bevel=0.004, seg=1); x += 0.3

def glow_parts(s, verts, normals, faces):
    """Lamps that light: the same faces 2 mm out, shown only while lit (DrivableCar, TrafficCar)."""
    out = {}
    for vs, name in faces:
        _, surface = name.split("|")
        if surface == "TailLamp": target = "Glow_Brake"
        elif surface == "Amber": target = "Glow_Reverse"
        elif surface == "HeadLamp": target = "Glow_Head"
        else: continue
        part = out.setdefault(target, Part(target))
        pts = [(verts[i][0] + normals[i][0] * 0.002, verts[i][1] + normals[i][1] * 0.002, verts[i][2] + normals[i][2] * 0.002) for i in vs]
        part.poly(pts, [normals[i] for i in vs], [cell_uv("White")] * len(pts), 1)
    return out

# ============================================================================ out to Blender objects
def materials():
    mats = []
    paint = bpy.data.materials.get("Paint") or bpy.data.materials.new("Paint")
    atlas = bpy.data.materials.get("Atlas") or bpy.data.materials.new("Atlas")
    glass = bpy.data.materials.get("Glass") or bpy.data.materials.new("Glass")
    return [paint, atlas, glass]

def in_slot_order(part):
    """Faces by material: Unity numbers a mesh's submeshes by which material it meets first, and FPSKitCars reads them as Paint, Atlas, Glass."""
    order = sorted(range(len(part.polys)), key=lambda i: part.mats[i])
    verts, polys, uvs, norms, mats = [], [], [], [], []
    for i in order:
        base = len(verts)
        for k in part.polys[i]:
            verts.append(part.verts[k]); uvs.append(part.uvs[k]); norms.append(part.norms[k])
        polys.append(list(range(base, base + len(part.polys[i])))); mats.append(part.mats[i])
    part.verts, part.polys, part.uvs, part.norms, part.mats = verts, polys, uvs, norms, mats

def make_object(part, mats):
    if not part.verts: return None
    in_slot_order(part)
    origin = part.origin
    me = bpy.data.meshes.new(part.name)
    co = list(part.verts) if part.local else [(v[0] - origin.x, v[1] - origin.y, v[2] - origin.z) for v in part.verts]
    me.from_pydata(co, [], part.polys)
    for m in mats: me.materials.append(m)
    uv = me.uv_layers.new(name="UVMap")
    for i, u in enumerate(part.uvs): uv.data[i].uv = u
    for p, m in zip(me.polygons, part.mats): p.material_index = m
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    me.update()
    me.normals_split_custom_set(part.norms)
    ob = bpy.data.objects.new(part.name, me)
    ob.location = origin
    bpy.context.scene.collection.objects.link(ob)
    return ob

def empty(name, at):
    ob = bpy.data.objects.new(name, None); ob.location = at; ob.empty_display_size = 0.05
    bpy.context.scene.collection.objects.link(ob); return ob

HINGES = {"Door_FL": 1, "Door_FR": -1, "Door_RL": 1, "Door_RR": -1}

def build_car(name, s):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = materials()
    # The surface at three levels of detail.
    v2, n2, f2 = build_surface(s, 2)
    v1, n1, f1 = build_surface(s, 1)
    v0, n0, f0 = build_surface(s, 0)

    parts = {}
    add_surface(parts, v2, n2, f2)
    # Hinges: each door's origin on its front edge at the belt, set before cards attach.
    for door, side in HINGES.items():
        if door not in parts: continue
        front = s["door0"] if door[5] == "F" else s["doorB"]
        hinge = (side * (curve(s["halfw"], front) - 0.01), front, curve(s["belt"], front) - 0.18)
        parts[door].origin = Vector(hinge)
        _cards[door] = parts["Card_" + door[5:]] = Part("Card_" + door[5:], hinge)
    inner = Part("Interior")
    add_shell(inner, v2, n2, f2, s)
    ins = interior(s, inner)
    parts["Interior"] = inner
    outside(s, parts)
    wheel_at = {"FL": (1, s["fa"]), "FR": (-1, s["fa"]), "RL": (1, s["ra"]), "RR": (-1, s["ra"])}
    for k, (side, y) in wheel_at.items():
        parts["Wheel_" + k] = wheel("Wheel_" + k, s, side, y)
    steer, hub, axis = steer_wheel(s, ins)
    parts["SteerWheel"] = steer
    # Dial pivots: the two dials on the Dials cell sit at a quarter and three quarters of its width.
    dx = 0.145 * 0.5
    parts["Needle_Speed"] = needle("Needle_Speed", (ins["seat_x"] + dx, ins["dial_z"]), ins["dial_y"] + 0.002)
    parts["Needle_Rpm"] = needle("Needle_Rpm", (ins["seat_x"] - dx, ins["dial_z"]), ins["dial_y"] + 0.002)
    parts.update(glow_parts(s, v2, n2, f2))

    # Lower detail: everything merged but paint, atlas and glass kept apart as slots. Level 1 has real wheels of few sides and no cockpit; level 2, the cage alone.
    l1 = {}
    add_surface(l1, v1, n1, f1)
    merged1 = Part("LOD1_All")
    for p in l1.values(): absorb(merged1, p)
    for k, (side, y) in wheel_at.items(): absorb(merged1, wheel("w", s, side, y, simple=True))
    blackout(merged1, s)
    l2 = {}
    add_surface(l2, v0, n0, f0)
    merged2 = Part("LOD2_All")
    for p in l2.values(): absorb(merged2, p)
    for k, (side, y) in wheel_at.items():
        w = Part("w", (0, 0, 0)); r = s["tyre"]; hw = s["tyre_w"] * 0.5; x = side * (curve(s["halfw"], y) - hw + 0.012)
        lathe(w, [(0.0001, -hw), (r, -hw), (r, hw), (0.0001, hw)], ["Sidewall", "Tread", "Hubcap"], seg=10, offset=(x, y, r), rim_mirror=side)
        absorb(merged2, w)

    objects = {}
    for key, part in parts.items():
        ob = make_object(part, mats)
        if ob: objects[key] = ob
    make_object(merged1, mats); make_object(merged2, mats)
    grips = [empty("Seat_Eye", ins["eye"]), empty("Grip_L", steer.grip_l), empty("Grip_R", steer.grip_r),
             empty("Steer_Axis", hub + axis), empty("Exhaust", (0.48, s["L"] / 2 + 0.03, curve(s["bottom"], s["L"] / 2 - 0.25) + 0.015)),
             empty("Exit", (curve(s["halfw"], 0) + 0.9, s["door0"] + 0.5, 0.0))]
    tris = sum(len(p.polys) for p in parts.values())
    print(f"[cars] {name}: {tris} polys LOD0 (surface {len(f2)}), LOD1 {len(merged1.polys)}, LOD2 {len(merged2.polys)}")
    return objects, ins

def absorb(into, part):
    o = part.origin
    base = len(into.verts)
    into.verts.extend((v[0] + o.x, v[1] + o.y, v[2] + o.z) if part.local else tuple(v) for v in part.verts)
    for poly in part.polys: into.polys.append([i + base for i in poly])
    into.uvs.extend(part.uvs); into.norms.extend(part.norms); into.mats.extend(part.mats)

def blackout(part, s):
    """Lower detail has no cabin: a dark shape behind the glass, so it reads as an interior from across a street."""
    for y0, y1 in ((s["cowl"] + 0.2, s["deck"] - 0.25 if not s["bed"] else s["roofR"] - 0.05),):
        z0 = curve(s["belt"], y0) - 0.05; z1 = curve(s["top"], (y0 + y1) / 2) - 0.08; hw = curve(s["roofw"], (y0 + y1) / 2) - 0.04
        box(part, (0, (y0 + y1) / 2, (z0 + z1) / 2), (hw * 2, y1 - y0, z1 - z0), "Under", bevel=0)

def export(name):
    for ob in bpy.context.scene.objects: ob.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             mesh_smooth_type="OFF", use_custom_props=False, use_tspace=False, add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=False)

# ============================================================================ preview
PREVIEW_COLOURS = {"Saloon": (0.55, 0.02, 0.02), "OldSaloon": (0.72, 0.5, 0.05), "Hatch": (0.02, 0.2, 0.55), "OffRoader": (0.7, 0.7, 0.68), "Pickup": (0.06, 0.25, 0.16), "Minibus": (0.75, 0.74, 0.68)}

def preview_setup(name):
    tex_dir = os.path.join(OUT, "Textures")
    paint, atlas, glass = bpy.data.materials["Paint"], bpy.data.materials["Atlas"], bpy.data.materials["Glass"]
    paint.use_nodes = True; atlas.use_nodes = True; glass.use_nodes = True
    b = paint.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*PREVIEW_COLOURS.get(name, (0.5, 0.5, 0.5)), 1); b.inputs["Roughness"].default_value = 0.22
    b.inputs["Coat Weight"].default_value = 1.0; b.inputs["Coat Roughness"].default_value = 0.05
    b = atlas.node_tree.nodes["Principled BSDF"]
    img = atlas.node_tree.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(os.path.join(tex_dir, "CarAtlas.png"))
    atlas.node_tree.links.new(img.outputs["Color"], b.inputs["Base Color"]); b.inputs["Roughness"].default_value = 0.45
    b = glass.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.03, 0.04, 0.04, 1); b.inputs["Alpha"].default_value = 0.3; b.inputs["Roughness"].default_value = 0.03
    w = bpy.data.worlds.new("w"); bpy.context.scene.world = w; w.use_nodes = True
    sky = w.node_tree.nodes.new("ShaderNodeTexSky")
    w.node_tree.links.new(sky.outputs[0], w.node_tree.nodes["Background"].inputs[0]); w.node_tree.nodes["Background"].inputs[1].default_value = 0.22
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
    ground = bpy.context.active_object; m = bpy.data.materials.new("ground"); m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.36, 0.28, 1); ground.data.materials.append(m)
    scene = bpy.context.scene; scene.render.engine = "CYCLES"; scene.view_settings.view_transform = "Standard"; scene.cycles.samples = 24; scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 720, 405; scene.cycles.device = "CPU"; scene.render.threads = 6
    for ob in bpy.data.objects:
        if ob.name.startswith("LOD") or ob.name.startswith("Glow"): ob.hide_render = True

def shoot(name, label, at, look, fov=40):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.get("c") or bpy.data.cameras.new("c")
    cam = bpy.data.objects.get("c") or bpy.data.objects.new("c", cam_data)
    if cam.name not in scene.collection.objects: scene.collection.objects.link(cam)
    cam.location = at; cam_data.angle = math.radians(fov); cam_data.clip_start = 0.02
    cam.rotation_euler = (Vector(look) - Vector(at)).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = os.path.join(RENDER, f"{name}_{label}.png")
    bpy.ops.render.render(write_still=True)

for name, s in CARS.items():
    if ONLY and name not in ONLY: continue
    objects, ins = build_car(name, s)
    if not RENDER:
        export(name)
    else:
        os.makedirs(RENDER, exist_ok=True)
        preview_setup(name)
        h = curve(s["top"], 0.3)
        shoot(name, "q", (5.2, -6.0, 1.9), (0, 0, 0.75))
        shoot(name, "r", (-5.0, 5.8, 2.1), (0, 0, 0.75))
        shoot(name, "s", (9.0, 0, 1.0), (0, 0, 0.8), 30)
        e = Vector(ins["eye"])
        shoot(name, "cockpit", tuple(e), tuple(e + Vector((-0.05, -1, -0.1))), 72)
        shoot(name, "dials", (ins["seat_x"], ins["dial_y"] + 0.24, ins["dial_z"] + 0.03), (ins["seat_x"], ins["dial_y"], ins["dial_z"]), 50)
print("[cars] done")
