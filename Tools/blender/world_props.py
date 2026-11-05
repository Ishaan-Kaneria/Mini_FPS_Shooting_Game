"""The real objects of the town, made in Blender: hard-surface models with bevelled edges, panel seams and working parts, not boxes.
Each is one FBX, feet at the origin, front towards -Y (Unity +Z), material slots named by what they are (FPSKitWorldModels assigns them).
UVs: world metres on every part, so the town's tiling materials sit at their real scale; 0-1 on the drum and the pump's display.
    ~/Programs/blender/blender --background --factory-startup --python Tools/blender/world_props.py -- OUT_DIR [Name ...]"""
import math, os, sys
import bmesh, bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0]); ONLY = set(argv[1:])
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

class Prop:
    def __init__(self, name): self.name, self.parts = name, []
    def add(self, bm, slot, uv="metres"):
        if uv == "metres": metre_uvs(bm)
        self.parts.append((bm, slot))
    def box(self, slot, c, s, bevel=0.006, seg=2, taper=None):
        """A box centre c, size s, its edges rolled: nothing real has a knife edge."""
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bm.verts.ensure_lookup_table()
        for v in bm.verts:
            v.co.x *= s[0]; v.co.y *= s[1]; v.co.z *= s[2]
            if taper and v.co.z < 0: v.co.x *= taper; v.co.y *= taper
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)
        bmesh.ops.translate(bm, vec=Vector(c), verts=bm.verts)
        self.add(bm, slot); return self
    def cushion(self, slot, c, s, soft=0.04):
        return self.box(slot, c, s, bevel=min(soft, min(s) * 0.45), seg=4)
    def cyl(self, slot, c, r, h, seg=16, axis="Z", r2=None, bevel=0.0):
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=[e for e in bm.edges if e.calc_face_angle(0) > 0.6], offset=bevel, segments=2, profile=0.5, affect="EDGES", clamp_overlap=True)
        if axis != "Z":
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "Y" if axis == "X" else "X"), verts=bm.verts)
        bmesh.ops.translate(bm, vec=Vector(c), verts=bm.verts)
        self.add(bm, slot); return self
    def tube(self, slot, points, r, seg=8):
        """A round tube through a list of points (a hose, a rail, a pipe)."""
        bm = bmesh.new(); rings = []
        for i, p in enumerate(points):
            p = Vector(p); a = Vector(points[max(i - 1, 0)]); b = Vector(points[min(i + 1, len(points) - 1)])
            t = (b - a).normalized(); n = t.orthogonal().normalized(); bi = t.cross(n)
            rings.append([bm.verts.new(p + (n * math.cos(k * math.tau / seg) + bi * math.sin(k * math.tau / seg)) * r) for k in range(seg)])
        for i in range(len(rings) - 1):
            for k in range(seg):
                bm.faces.new((rings[i][k], rings[i][(k + 1) % seg], rings[i + 1][(k + 1) % seg], rings[i + 1][k]))
        bm.faces.new(rings[0]); bm.faces.new(reversed(rings[-1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        self.add(bm, slot); return self
    def lathe(self, slot, profile, seg=32, uv=None):
        """Turned about Z: profile is (radius, height) from bottom to top."""
        bm = bmesh.new(); rings = []
        for r, z in profile:
            rings.append([bm.verts.new((r * math.cos(k * math.tau / seg), r * math.sin(k * math.tau / seg), z)) for k in range(seg)])
        for i in range(len(rings) - 1):
            for k in range(seg):
                bm.faces.new((rings[i][k], rings[i][(k + 1) % seg], rings[i + 1][(k + 1) % seg], rings[i + 1][k]))
        if profile[0][0] > 0.001: bm.faces.new(reversed(rings[0]))
        if profile[-1][0] > 0.001: bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        self.add(bm, slot, uv="none" if uv else "metres")
        if uv: uv(bm)
        return self
    def write(self):
        if ONLY and self.name not in ONLY: return
        bpy.ops.object.select_all(action="DESELECT") if bpy.context.view_layer.objects else None
        slots = []; me = bpy.data.meshes.new(self.name); total = bmesh.new(); uv_layer = total.loops.layers.uv.new("UVMap")
        for bm, slot in self.parts:
            if slot not in slots: slots.append(slot)
            src_uv = bm.loops.layers.uv.active
            vmap = {}
            for v in bm.verts: vmap[v] = total.verts.new(v.co)
            for f in bm.faces:
                try: nf = total.faces.new([vmap[v] for v in f.verts])
                except ValueError: continue
                nf.material_index = slots.index(slot)
                nf.smooth = True
                if src_uv:
                    for lo, ln in zip(f.loops, nf.loops): ln[uv_layer].uv = lo[src_uv].uv
            bm.free()
        total.to_mesh(me); tris = sum(len(p.vertices) - 2 for p in me.polygons); total.free()
        me.shade_smooth(); me.set_sharp_from_angle(angle=math.radians(34))
        for s in slots:
            me.materials.append(bpy.data.materials.get(s) or bpy.data.materials.new(s))
        ob = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(ob)
        ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, self.name + ".fbx"), use_selection=True, object_types={"MESH"},
                                 axis_forward="-Z", axis_up="Y", bake_space_transform=True, apply_unit_scale=True,
                                 apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE", use_tspace=True, add_leaf_bones=False)
        bpy.data.objects.remove(ob, do_unlink=True)
        print(f"[world] {self.name}: {tris} triangles, slots {slots}")

def metre_uvs(bm):
    """Each face mapped by its dominant axis at one UV per metre."""
    layer = bm.loops.layers.uv.active or bm.loops.layers.uv.new("UVMap")
    bm.normal_update()
    for f in bm.faces:
        n = f.normal; a = max(range(3), key=lambda i: abs(n[i]))
        for lo in f.loops:
            p = lo.vert.co
            lo[layer].uv = (p.y, p.z) if a == 0 else (p.x, p.z) if a == 1 else (p.x, p.y)

# ---- The steel drum ---------------------------------------------------------------------------------------------------------
def drum():
    R, H = 0.286, 0.88
    prof = [(0.0, 0.016), (R - 0.018, 0.016), (R - 0.010, 0.022), (R - 0.006, 0.0), (R + 0.006, 0.0), (R + 0.009, 0.008), (R + 0.004, 0.018), (R, 0.026)]
    for hoop in (H * 0.34, H * 0.66):       # rolled hoops, what lets a drum be rolled
        prof += [(R, hoop - 0.022), (R + 0.006, hoop - 0.012), (R + 0.012, hoop - 0.004), (R + 0.012, hoop + 0.004), (R + 0.006, hoop + 0.012), (R, hoop + 0.022)]
    for z in (H * 0.17, H * 0.5, H * 0.83): prof.append((R, z))
    prof = sorted(prof[8:], key=lambda p: p[1]) and prof[:8] + sorted(prof[8:], key=lambda p: p[1])
    prof += [(R, H - 0.026), (R + 0.004, H - 0.018), (R + 0.009, H - 0.008), (R + 0.006, H), (R - 0.006, H), (R - 0.010, H - 0.022), (R - 0.018, H - 0.016), (0.0, H - 0.016)]
    def uv(bm):
        layer = bm.loops.layers.uv.new("UVMap"); bm.normal_update()
        for f in bm.faces:
            lid = abs(f.normal.z) > 0.9 and (f.calc_center_median().z < 0.03 or f.calc_center_median().z > H - 0.03)
            us = [(math.atan2(lo.vert.co.y, lo.vert.co.x) / math.tau + 0.5) for lo in f.loops]
            if max(us) - min(us) > 0.5: us = [u + 1 if u < 0.5 else u for u in us]
            for lo, u in zip(f.loops, us):
                p = lo.vert.co
                lo[layer].uv = (0.82 + 0.09 + p.x * 0.15, 0.5 + p.y * 0.3) if lid else (u * 0.8, p.z / H)
    p = Prop("Drum_Steel")
    p.lathe("DrumPaint", prof, seg=32, uv=uv)
    # The bungs on the lid and a dent ring round them.
    p.cyl("DrumCap", (0.17, 0.04, H - 0.010), 0.035, 0.016, seg=10, bevel=0.002)
    p.cyl("DrumCap", (-0.18, -0.02, H - 0.010), 0.021, 0.014, seg=8, bevel=0.002)
    p.write()

# ---- Furniture ----------------------------------------------------------------------------------------------------------------
def wardrobe():
    W, D, H = 1.30, 0.58, 2.04
    p = Prop("Wardrobe")
    for s in (-1, 1): p.box("Wood", (s * (W / 2 - 0.012), 0, H / 2 + 0.05), (0.024, D, H - 0.1))      # sides
    p.box("Wood", (0, 0.27, H / 2 + 0.05), (W, 0.012, H - 0.1), bevel=0)                               # back
    p.box("Wood", (0, 0, 0.05), (W + 0.01, D - 0.02, 0.1), bevel=0.003)                                # plinth
    p.box("Wood", (0, 0, H - 0.022), (W + 0.05, D + 0.05, 0.044), bevel=0.008)                         # cornice
    for s in (-1, 1):                                                                                  # doors: a frame round a sunk panel
        cx = s * W / 4; dw = W / 2 - 0.022; y = -D / 2 - 0.006; h0 = 0.11; dh = H - 0.2
        p.box("Wood", (cx, y, h0 + 0.05), (dw, 0.024, 0.1)).box("Wood", (cx, y, h0 + dh - 0.05), (dw, 0.024, 0.1))
        p.box("Wood", (cx, y, h0 + dh * 0.5), (dw, 0.024, 0.06))
        for e in (-1, 1): p.box("Wood", (cx + e * (dw / 2 - 0.045), y, h0 + dh / 2), (0.09, 0.024, dh))
        for z0, z1 in ((h0 + 0.1, h0 + dh * 0.5 - 0.03), (h0 + dh * 0.5 + 0.03, h0 + dh - 0.1)):
            p.box("Wood", (cx, y + 0.006, (z0 + z1) / 2), (dw - 0.18, 0.012, z1 - z0), bevel=0.002)
        p.cyl("Brass", (s * 0.045, y - 0.02, h0 + dh * 0.5), 0.011, 0.11, seg=8, bevel=0.002)
    p.write()

def bed():
    p = Prop("Bed_Double")
    W, L = 1.66, 2.06
    for x, y in ((-W / 2 + 0.05, -L / 2 + 0.05), (W / 2 - 0.05, -L / 2 + 0.05), (-W / 2 + 0.05, L / 2 - 0.05), (W / 2 - 0.05, L / 2 - 0.05)):
        p.box("Wood", (x, y, 0.12), (0.08, 0.08, 0.24), bevel=0.006, taper=0.8)
    for s in (-1, 1): p.box("Wood", (s * (W / 2 - 0.02), 0, 0.27), (0.04, L, 0.17), bevel=0.006)
    p.box("Wood", (0, -L / 2 + 0.02, 0.27), (W, 0.04, 0.17), bevel=0.006)
    p.box("Wood", (0, L / 2 + 0.02, 0.56), (W + 0.04, 0.05, 1.12), bevel=0.012)                        # headboard
    p.box("Wood", (0, L / 2 - 0.005, 0.72), (W - 0.24, 0.02, 0.52), bevel=0.004)                       # its sunk panel
    for x in (-0.5, -0.25, 0, 0.25, 0.5): p.box("Wood", (x, 0, 0.2), (0.07, L - 0.08, 0.02), bevel=0)  # slats under it
    p.cushion("Mattress", (0, 0, 0.43), (W - 0.1, L - 0.1, 0.23), soft=0.055)
    p.cushion("Blanket", (0, -0.30, 0.565), (W - 0.03, L - 0.72, 0.055), soft=0.024)                   # thrown over, hanging a little
    for s in (-1, 1):
        p.box("Blanket", (s * (W / 2 - 0.01), -0.30, 0.47), (0.03, L - 0.76, 0.17), bevel=0.012)
        p.cushion("Mattress", (s * 0.38, 0.73, 0.595), (0.64, 0.38, 0.13), soft=0.055)                 # pillows
    p.write()

def bedside():
    p = Prop("BedsideTable")
    S, H = 0.46, 0.52
    for x in (-1, 1):
        for y in (-1, 1): p.box("Wood", (x * (S / 2 - 0.03), y * (S / 2 - 0.03), 0.08), (0.035, 0.035, 0.16), taper=0.7)
    p.box("Wood", (0, 0, 0.33), (S - 0.02, S - 0.02, 0.33), bevel=0.005)
    p.box("Wood", (0, 0, H - 0.012), (S + 0.015, S + 0.015, 0.024), bevel=0.006)
    p.box("Wood", (0, -S / 2 + 0.006, 0.40), (S - 0.05, 0.016, 0.15), bevel=0.004)                     # the drawer
    p.box("Wood", (0, -S / 2 + 0.006, 0.24), (S - 0.05, 0.016, 0.15), bevel=0.004)
    for z in (0.40, 0.24): p.cyl("Brass", (0, -S / 2 - 0.012, z), 0.012, 0.022, seg=10, axis="Y", bevel=0.003)
    p.write()

def armchair():
    p = Prop("Armchair")
    W, D = 0.84, 0.84
    for x in (-1, 1):
        for y in (-1, 1): p.box("WoodDark", (x * (W / 2 - 0.06), y * (D / 2 - 0.06), 0.06), (0.05, 0.05, 0.12), taper=0.75)
    p.cushion("Cloth", (0, 0.02, 0.21), (W, D, 0.2), soft=0.03)                                        # the frame, upholstered
    p.cushion("Cloth", (0, 0.37, 0.56), (W, 0.17, 0.78), soft=0.06)                                    # the back
    for s in (-1, 1): p.cushion("Cloth", (s * (W / 2 - 0.075), 0.03, 0.46), (0.15, D - 0.03, 0.34), soft=0.06)
    p.cushion("ClothLight", (0, -0.05, 0.40), (W - 0.32, D - 0.25, 0.16), soft=0.06)                   # seat cushion
    p.cushion("ClothLight", (0, 0.24, 0.66), (W - 0.34, 0.15, 0.42), soft=0.06)                        # back cushion
    p.write()

def kitchen_base():
    """One run of base cupboards a metre wide, worktop and all; the builder lays as many as fit."""
    p = Prop("KitchenBase")
    W, D, H = 1.0, 0.6, 0.9
    p.box("Plinth", (0, 0.03, 0.05), (W, D - 0.06, 0.1), bevel=0.002)
    p.box("Cabinet", (0, 0, 0.47), (W - 0.004, D - 0.03, 0.74), bevel=0.002)
    for s in (-1, 1):
        cx = s * W / 4
        p.box("Cabinet", (cx, -D / 2 - 0.002, 0.47), (W / 2 - 0.012, 0.02, 0.73), bevel=0.003)
        p.box("Cabinet", (cx, -D / 2 - 0.012, 0.47), (W / 2 - 0.11, 0.006, 0.61), bevel=0.002)         # the door's raised panel
        p.tube("Steel", [(s * 0.06, -D / 2 - 0.03, 0.62), (s * 0.06, -D / 2 - 0.038, 0.66), (s * 0.06, -D / 2 - 0.038, 0.74), (s * 0.06, -D / 2 - 0.03, 0.78)], 0.006, seg=6)
    p.box("Worktop", (0, -0.012, H - 0.02), (W, D + 0.03, 0.04), bevel=0.006)
    p.box("Worktop", (0, D / 2 - 0.01, H + 0.03), (W, 0.02, 0.06), bevel=0.004)                        # upstand
    p.write()

# ---- The gas station ------------------------------------------------------------------------------------------------------------
def fuel_pump():
    p = Prop("FuelPump")
    W, D, H = 0.92, 0.56, 1.90
    p.box("Concrete", (0, 0, 0.05), (W + 0.12, D + 0.12, 0.1), bevel=0.01)                             # plinth on the island
    p.box("PaintWhite", (0, 0, 0.56), (W, D, 0.92), bevel=0.012)                                       # lower cabinet
    for s in (-1, 1):                                                                                  # access panels, with their seams
        p.box("PaintWhite", (0, s * (D / 2 + 0.004), 0.56), (W - 0.1, 0.008, 0.78), bevel=0.003)
        for z in (0.24, 0.36, 0.48, 0.60): p.box("Vent", (0, s * (D / 2 + 0.010), z), (W - 0.34, 0.004, 0.018), bevel=0)
        p.cyl("Steel", (W / 2 - 0.12, s * (D / 2 + 0.012), 0.86), 0.012, 0.01, seg=8, axis="Y")
    p.box("PaintWhite", (0, 0, 1.28), (W - 0.04, D - 0.06, 0.52), bevel=0.014)                         # head
    p.box("PaintGreen", (0, 0, 1.72), (W + 0.02, D + 0.02, 0.36), bevel=0.018)                         # brand panel
    for s in (-1, 1):
        # Display bezel, the screen in it, and the price keys under it.
        p.box("Plastic", (0, s * (D / 2 - 0.022), 1.32), (W - 0.2, 0.012, 0.36), bevel=0.006)
        # The screen's quad: 0-1 over the texture.
        bm = bmesh.new(); y = s * (D / 2 - 0.0135); xs = (-0.29, 0.29) if s < 0 else (0.29, -0.29)
        a = bm.verts.new((xs[0], y, 1.17)); b = bm.verts.new((xs[1], y, 1.17)); c = bm.verts.new((xs[1], y, 1.47)); d = bm.verts.new((xs[0], y, 1.47))
        f = bm.faces.new((a, b, c, d)); uv = bm.loops.layers.uv.new("UVMap")
        for lo, q in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))): lo[uv].uv = q
        bm.normal_update()
        if (f.normal.y > 0) != (s > 0): f.normal_flip()
        p.parts.append((bm, "Screen"))
        p.box("Plastic", (0, s * (D / 2 + 0.006), 0.99), (0.34, 0.02, 0.1), bevel=0.005)               # key pad
        for k in range(3):
            for n in range(4): p.box("Steel", (-0.1 + n * 0.066, s * (D / 2 + 0.018), 0.96 + k * 0.03), (0.05, 0.006, 0.02), bevel=0.001)
    for side in (-1, 1):
        # The nozzle in its boot on the end, and the hose: up to the retractor and hanging back in a slack curve.
        x = side * (W / 2 + 0.01)
        p.box("Plastic", (x + side * 0.03, 0.0, 1.04), (0.07, 0.18, 0.2), bevel=0.012)
        p.tube("Steel", [(x + side * 0.03, -0.015, 1.02), (x + side * 0.08, -0.03, 1.08), (x + side * 0.11, -0.06, 1.12), (x + side * 0.15, -0.14, 1.11)], 0.012, seg=8)   # spout
        p.box("Rubber", (x + side * 0.05, 0.02, 1.13), (0.04, 0.05, 0.13), bevel=0.012)                # grip
        p.tube("Steel", [(x + side * 0.03, 0.03, 1.08), (x + side * 0.06, 0.05, 1.05), (x + side * 0.08, 0.02, 1.04)], 0.005, seg=6)                                       # lever guard
        pts = []
        for k in range(15):
            t = k / 14
            # The hose hangs in a catenary from the top of the head to the nozzle's grip.
            pts.append((x + side * (0.05 + 0.2 * math.sin(t * math.pi)), 0.04 + 0.03 * t, 1.86 * (1 - t) + 1.18 * t - 0.72 * math.sin(t * math.pi) ** 1.3))
        p.tube("Rubber", pts, 0.013, seg=8)
    p.write()

def canopy_light():
    p = Prop("CanopyLight")
    p.box("Steel", (0, 0, -0.03), (0.62, 0.62, 0.06), bevel=0.008)
    bm_z = -0.065
    p.box("LampLens", (0, 0, bm_z), (0.54, 0.54, 0.012), bevel=0.003)
    p.write()

def bollard():
    p = Prop("Bollard")
    p.cyl("PaintYellow", (0, 0, 0.5), 0.07, 1.0, seg=14, bevel=0.012)
    p.cyl("PaintBlack", (0, 0, 0.72), 0.0715, 0.12, seg=14)
    p.cyl("PaintBlack", (0, 0, 0.44), 0.0715, 0.12, seg=14)
    p.cyl("Steel", (0, 0, 0.02), 0.13, 0.04, seg=14, bevel=0.006)
    p.write()

def crash_guard():
    """A hoop of steel pipe at the end of a pump island."""
    p = Prop("CrashGuard")
    pts = []
    for k in range(13):
        t = k / 12
        a = math.pi * t
        pts.append((0.55 * math.cos(a), 0, 0.02 + 0.36 + 0.42 * math.sin(a)) if 0 < k < 12 else (0.55 * math.cos(a), 0, 0.0))
    p.tube("PaintYellow", pts, 0.042, seg=10)
    p.write()

for fn in (drum, wardrobe, bed, bedside, armchair, kitchen_base, fuel_pump, canopy_light, bollard, crash_guard):
    fn()
print("[world] done")
