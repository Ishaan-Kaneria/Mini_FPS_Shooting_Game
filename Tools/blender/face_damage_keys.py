"""
The enemy's face damage, authored in Blender: five shape keys on the Survivalist soldier's head mesh, exported as an FBX
that Unity's FPSKitFaceDamage.Bake reads the shapes from (Docs/DESIGN_NOTES.md "Pain, face damage and the head grab").

  ~/Programs/blender/blender-launcher --background --python Tools/blender/face_damage_keys.py
  (or run in a connected Blender through the MCP: it works in its own scene, "FPSKit_FaceDamage")

Keys (each localised by smooth falloffs about landmarks measured from the mesh: the eyes from the eye material):
  Face_Deform_Impact_L / _R   the cheekbone on that side caved in (11 mm), the jaw shifted away from it (7 mm), the mouth
                              corner dropped; nothing within 2-3 cm of the eye (a first version tore the lower lid)
  Eye_Swollen_L / _R          the socket rim puffed out (7 mm) with a bag under it, the lids closing over the eye
  Head_Bruise_Distortion      uneven lumps on the brow, the temple and a cheek (3-5 mm)
The eyeballs never move. The soldier faces -Y in Blender, up +Z, his left +X (right-handed: up x forward = left).

Only the head mesh and the armature are exported: Unity transfers the shapes onto its own import of the head by matching
vertex positions, so the enemy's rig, skinning and bone order are never touched by this round trip.
"""
import math
import os

import bpy
from mathutils import Vector

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCE = os.path.join(PROJECT, "Assets/Survivalist/Basemesh/SK_Military_Survivalist.fbx")
OUT = os.path.join(PROJECT, "Assets/Models/Enemy/SK_Military_Head1_FaceDamage.fbx")
PREVIEWS = os.environ.get("FACE_PREVIEWS", "")
HEAD = "SK_Military_Head1"

F, U, LEFT = Vector((0, -1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0))


def gauss(d, s):
    return math.exp(-(d * d) / (2 * s * s))


def smooth(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def load():
    scn = bpy.data.scenes.get("FPSKit_FaceDamage")
    if scn is not None:
        for o in list(scn.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        scn = bpy.data.scenes.new("FPSKit_FaceDamage")
    bpy.context.window.scene = scn if bpy.context.window else scn
    if not bpy.context.window:
        bpy.context.scene.collection  # background: the import goes into the active scene
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=SOURCE, use_anim=False, automatic_bone_orientation=False)
    return scn, [o for o in bpy.data.objects if o not in before]


def build(head):
    me = head.data
    mw = head.matrix_world
    lin = mw.to_3x3()
    inv = lin.inverted()
    eye_mat = [i for i, m in enumerate(me.materials) if m and "Eye" in m.name][0]
    eyes = set()
    for p in me.polygons:
        if p.material_index == eye_mat:
            eyes.update(p.vertices)
    W = [mw @ v.co for v in me.vertices]
    N = [(lin @ v.normal).normalized() for v in me.vertices]
    eyeL = [W[i] for i in eyes if W[i].x > 0]
    eyeR = [W[i] for i in eyes if W[i].x < 0]
    cL = sum(eyeL, Vector()) / len(eyeL)
    cR = sum(eyeR, Vector()) / len(eyeR)
    centre = sum(W, Vector()) / len(W)
    head_c = Vector((0.0, centre.y + 0.035, centre.z - 0.01))

    if me.shape_keys is None:
        head.shape_key_add(name="Basis", from_mix=False)

    def key(name, disp):
        kb = me.shape_keys.key_blocks.get(name) or head.shape_key_add(name=name, from_mix=False)
        for i, v in enumerate(me.vertices):
            kb.data[i].co = v.co.copy()
        for i, d in disp.items():
            kb.data[i].co = me.vertices[i].co + inv @ d
        return max((d.length for d in disp.values()), default=0.0)

    def impact(s):
        eye_c = cL if s > 0 else cR
        cheek = eye_c + U * -0.044 + LEFT * (s * 0.021) + F * -0.008
        corner = eye_c + U * -0.068 + LEFT * (s * -0.008) + F * 0.012
        disp = {}
        for i, p in enumerate(W):
            if i in eyes:
                continue
            clear = smooth(0.018, 0.032, (p - eye_c).length)
            front = max(0.0, N[i].dot(F) * 0.5 + N[i].dot(LEFT * s) * 0.9)
            w1 = gauss((p - cheek).length, 0.024) * min(1.0, front + 0.2) * clear
            d = -N[i] * (0.011 * w1) + LEFT * (-s * 0.003 * w1)
            low = smooth(1.60, 1.635, p.z) * (1 - smooth(1.685, 1.715, p.z)) * smooth(0.0, -0.05, p.y)
            d += LEFT * (-s * 0.007 * low)
            d += U * (-0.0025 * gauss((p - corner).length, 0.012))
            if d.length > 1e-6:
                disp[i] = d
        return disp

    def swollen(s):
        eye_c = cL if s > 0 else cR
        disp = {}
        for i, p in enumerate(W):
            if i in eyes:
                continue
            r = p - eye_c
            ring = gauss(r.length - 0.019, 0.010)
            under = gauss((p - (eye_c + U * -0.018 + F * 0.004)).length, 0.012)
            d = N[i] * (0.007 * ring + 0.004 * under)
            lid = gauss(r.length, 0.016) * smooth(0.0, 0.004, r.dot(F) + 0.004)
            d += U * (-0.004 * lid if r.dot(U) > 0 else 0.0025 * lid)
            if d.length > 1e-6:
                disp[i] = d
        return disp

    def bruise():
        lumps = [(cL + U * 0.045 + F * 0.01, 0.018, 0.005), (cR + U * -0.03 + LEFT * -0.012, 0.016, 0.004),
                 (cR + U * 0.028 + F * 0.006, 0.012, 0.0035), (cL + U * 0.02 + LEFT * 0.035 + F * -0.02, 0.02, 0.004),
                 (head_c + U * 0.11 + F * 0.03, 0.025, 0.003)]
        disp = {}
        for i, p in enumerate(W):
            if i in eyes:
                continue
            a = sum(h * gauss((p - c).length, s) for c, s, h in lumps)
            if a > 1e-6:
                disp[i] = N[i] * a
        return disp

    made = {}
    made["Face_Deform_Impact_L"] = key("Face_Deform_Impact_L", impact(1))
    made["Face_Deform_Impact_R"] = key("Face_Deform_Impact_R", impact(-1))
    made["Eye_Swollen_L"] = key("Eye_Swollen_L", swollen(1))
    made["Eye_Swollen_R"] = key("Eye_Swollen_R", swollen(-1))
    made["Head_Bruise_Distortion"] = key("Head_Bruise_Distortion", bruise())
    return {k: round(v * 1000, 1) for k, v in made.items()}


def export(scn, objects, head):
    arm = next(o for o in objects if o.type == "ARMATURE")
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    arm.select_set(True)
    head.select_set(True)
    bpy.context.view_layer.objects.active = head
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"},
                             use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
                             mesh_smooth_type="FACE", use_armature_deform_only=True)


def main():
    scn, objects = load()
    head = next(o for o in objects if o.name.startswith(HEAD))
    made = build(head)
    export(scn, objects, head)
    print("face damage keys:", made, "->", OUT)
    return made


if __name__ == "__main__":
    main()
