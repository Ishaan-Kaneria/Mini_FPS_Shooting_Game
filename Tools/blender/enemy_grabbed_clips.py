"""
The enemy's side of the head grab, keyed by hand in Blender after the fight in the film Master (Ishaan's brief of
2026-10-22; Docs/DESIGN_NOTES.md "Pain, face damage and the head grab"). The film itself could not be motion-captured
(cuts, a moving camera, the legs never in shot, the punching arm never tracked above 0.6 confidence), so the poses are set
from its frames by eye:

  Enemy_HeadGrabbed_Held    (loop, 1.2 s)  bent over from the waist by the hand on his head, knees giving, his left hand up
                                           on the wrist that holds him, his right pressed to his belly, struggling
  Enemy_HeadGrabbed_Impact  (0.6 s)        a fist to the face: the head knocked back and round, the knees buckling, the
                                           hands thrown, then back into the hold

Feet are held on the ground and the hands put where they belong by IK (Blender's, on a copy of the skeleton imported with
automatic bone orientation, so each bone points down its limb); the spine, the neck and the head are keyed. Everything is
then baked to plain keys on every frame (by reading the posed bones, not through the UI's bake operator, so it runs the
same headless) and exported as one FBX with a take per clip, for Unity's Humanoid import (FPSKitRiggedSoldier puts them on
the Action layer).

  ~/Programs/blender/blender-launcher --background --python Tools/blender/enemy_grabbed_clips.py
  (or in a connected Blender through the MCP; it works in its own scene, "FPSKit_EnemyClips")
"""
import math
import os

import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

PROJECT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCE = os.path.join(PROJECT, "Assets/Survivalist/Basemesh/SK_Military_Survivalist.fbx")
OUT = os.path.join(PROJECT, "Assets/Models/Enemy/Enemy_Execute_HeadGrabbed_Impact.fbx")
PREVIEWS = os.environ.get("CLIP_PREVIEWS", "")
FPS = 60

# The soldier faces -Y, up +Z, his left +X (as imported). A turn about +X by a positive angle tips a bone that points up
# forward (a bow); one that points down backward.
FWD, UP, LEFT = Vector((0, -1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0))


def load():
    scn = bpy.data.scenes.get("FPSKit_EnemyClips")
    if scn is not None:
        for o in list(scn.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        scn = bpy.data.scenes.new("FPSKit_EnemyClips")
    if bpy.context.window is not None:
        bpy.context.window.scene = scn
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=SOURCE, use_anim=False, automatic_bone_orientation=True)
    # After the import: it sets the scene's rate from the file's (24), and the export writes the clips at the scene's rate
    # (a first export came out 2.5 times too slow).
    scn.render.fps, scn.render.fps_base = FPS, 1.0
    objs = [o for o in bpy.data.objects if o not in before]
    for o in objs:
        if o.name not in scn.objects:
            scn.collection.objects.link(o)
    arm = next(o for o in objs if o.type == "ARMATURE")
    return scn, arm, objs


def empty(scn, name, at):
    e = bpy.data.objects.get(name)
    if e is None:
        e = bpy.data.objects.new(name, None)
        scn.collection.objects.link(e)
    e.location = at
    e.empty_display_size = 0.05
    return e


def bone_world(arm, name, head=True):
    b = arm.data.bones[name]
    return arm.matrix_world @ (b.head_local if head else b.tail_local)


def setup_ik(scn, arm):
    t = {}
    for side, s in (("l", 1), ("r", -1)):
        foot = bone_world(arm, f"foot_{side}")
        t[f"foot_{side}"] = empty(scn, f"IK_Foot_{side}", foot)
        t[f"knee_{side}"] = empty(scn, f"IK_Knee_{side}", bone_world(arm, f"calf_{side}") + FWD * 0.6)
        t[f"hand_{side}"] = empty(scn, f"IK_Hand_{side}", bone_world(arm, f"hand_{side}"))
        t[f"elbow_{side}"] = empty(scn, f"IK_Elbow_{side}", bone_world(arm, f"lowerarm_{side}") + LEFT * (0.5 * s) - FWD * 0.3 - UP * 0.2)
        for bone, target, pole in ((f"calf_{side}", t[f"foot_{side}"], t[f"knee_{side}"]),
                                   (f"lowerarm_{side}", t[f"hand_{side}"], t[f"elbow_{side}"])):
            pb = arm.pose.bones[bone]
            c = pb.constraints.get("FPSKit_IK") or pb.constraints.new("IK")
            c.name = "FPSKit_IK"
            c.target = target
            c.pole_target = pole
            c.chain_count = 2
            c.use_tail = True
        # The foot keeps its own turn on the ground.
        pb = arm.pose.bones[f"foot_{side}"]
        c = pb.constraints.get("FPSKit_Foot") or pb.constraints.new("COPY_ROTATION")
        c.name = "FPSKit_Foot"
        c.target = arm
        c.subtarget = "root"
        c.mix_mode = "BEFORE"
        c.influence = 0.0
    return t


def best_pole_angles(arm, scn):
    """Each IK chain's pole angle chosen so the knee bends forward and the elbow out and back (the bone roll decides it)."""
    for side, s in (("l", 1), ("r", -1)):
        for bone, want in ((f"calf_{side}", FWD), (f"lowerarm_{side}", LEFT * s - FWD * 0.5)):
            c = arm.pose.bones[bone].constraints["FPSKit_IK"]
            best, best_score = 0.0, -9.0
            for deg in (-180, -135, -90, -45, 0, 45, 90, 135):
                c.pole_angle = math.radians(deg)
                bpy.context.view_layer.update()
                mid = arm.matrix_world @ arm.pose.bones[bone].head
                root = arm.matrix_world @ arm.pose.bones[bone].parent.head
                tip = arm.matrix_world @ arm.pose.bones[bone].tail
                line = (tip - root).normalized()
                off = (mid - root) - line * (mid - root).dot(line)
                score = off.normalized().dot(want.normalized()) if off.length > 1e-4 else -1
                if score > best_score:
                    best, best_score = deg, score
            c.pole_angle = math.radians(best)


def turn(arm, bone, axis, deg):
    """Turns a pose bone about a world axis, on top of its current pose."""
    pb = arm.pose.bones[bone]
    aw = arm.matrix_world.to_3x3().normalized()
    axis_arm = (aw.inverted() @ axis).normalized()
    m = pb.matrix.copy()
    r = Matrix.Rotation(math.radians(deg), 4, axis_arm)
    head = m.translation.copy()
    m = r @ m
    m.translation = head
    pb.matrix = m
    bpy.context.view_layer.update()


def move(arm, bone, world_offset):
    pb = arm.pose.bones[bone]
    aw = arm.matrix_world.to_3x3()
    m = pb.matrix.copy()
    m.translation = m.translation + aw.inverted() @ world_offset
    pb.matrix = m
    bpy.context.view_layer.update()


def rest(arm):
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
    bpy.context.view_layer.update()


def held_pose(arm, t, k, rest_pos):
    """The hold at phase k (0..1 round the loop): bent, knees giving, struggling a little."""
    rest(arm)
    wob = math.sin(k * math.tau * 2.0)
    wob2 = math.sin(k * math.tau * 2.0 + 1.3)
    move(arm, "pelvis", UP * -0.11 + FWD * -0.06 + LEFT * (0.012 * wob2))
    turn(arm, "pelvis", LEFT, 12 + 1.5 * wob)
    turn(arm, "spine_01", LEFT, 12 + 1.0 * wob)
    turn(arm, "spine_02", LEFT, 10)
    turn(arm, "spine_03", LEFT, 8 - 1.5 * wob)
    turn(arm, "spine_03", UP, 3.0 * wob2)
    turn(arm, "neck_01", LEFT, 6)
    turn(arm, "head", LEFT, -6 + 3 * wob)          # the face turned up a little to the man holding him
    head = bone_world_posed(arm, "head")
    t["hand_l"].location = head + FWD * 0.16 + UP * 0.14 + LEFT * 0.04 + Vector((0, 0, 0.015 * wob))
    t["hand_r"].location = bone_world_posed(arm, "spine_01") + FWD * 0.2 + LEFT * -0.06 + UP * -0.04
    for side in ("l", "r"):
        t[f"foot_{side}"].location = rest_pos[f"foot_{side}"]
        t[f"knee_{side}"].location = rest_pos[f"knee_{side}"]
    bpy.context.view_layer.update()


def bone_world_posed(arm, name):
    return arm.matrix_world @ arm.pose.bones[name].head


def impact_pose(arm, t, f, rest_pos):
    """A fist to the face, f frames in (0..36): snap back and round by frame 4, knees buckle by 10, back by 36."""
    held_pose(arm, t, 0.0, rest_pos)
    snap = math.exp(-((f - 4) / 3.0) ** 2) if f <= 4 else math.exp(-((f - 4) / 9.0) ** 2)
    sag = math.exp(-((f - 10) / 6.0) ** 2) if f <= 10 else math.exp(-((f - 10) / 12.0) ** 2)
    # Half what looked right on its own: the game adds the snap every blow to the head gets (EnemyBody), and the two
    # together wrenched the head 75 degrees in two frames.
    turn(arm, "neck_01", LEFT, -9 * snap)
    turn(arm, "head", LEFT, -8 * snap)
    turn(arm, "head", UP, 10 * snap)
    turn(arm, "spine_03", LEFT, -7 * snap)
    move(arm, "pelvis", UP * (-0.09 * sag))
    turn(arm, "spine_01", LEFT, 6 * sag)
    t["hand_l"].location += UP * (0.05 * snap) - FWD * (0.04 * snap)
    t["hand_r"].location += LEFT * (-0.12 * sag) + UP * (0.06 * sag)
    bpy.context.view_layer.update()


def bake(scn, arm, name, frames, pose):
    """Poses each frame, reads every bone's posed matrix (IK included), and keys those as plain rotations/locations."""
    mats = []
    for f in range(frames):
        pose(f)
        mats.append({pb.name: pb.matrix.copy() for pb in arm.pose.bones})
    act = bpy.data.actions.get(name) or bpy.data.actions.new(name)
    act.fcurves.clear() if hasattr(act, "fcurves") else None
    arm.animation_data_create()
    arm.animation_data.action = act
    mute = []
    for pb in arm.pose.bones:
        for c in pb.constraints:
            if c.name.startswith("FPSKit_"):
                mute.append(c)
                c.mute = True
    for f, m in enumerate(mats):
        for pb in arm.pose.bones:
            pb.matrix = m[pb.name]
            bpy.context.view_layer.update()
        for pb in arm.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=f + 1, group=pb.name)
            if pb.name in ("pelvis", "root"):
                pb.keyframe_insert("location", frame=f + 1, group=pb.name)
    for c in mute:
        c.mute = False
    act.use_fake_user = True
    return act


def render(scn, arm, act, frames, tag):
    if not PREVIEWS:
        return []
    os.makedirs(PREVIEWS, exist_ok=True)
    cam = bpy.data.objects.get("ClipCam") or bpy.data.objects.new("ClipCam", bpy.data.cameras.new("ClipCam"))
    if cam.name not in scn.objects:
        scn.collection.objects.link(cam)
    scn.camera = cam
    scn.render.engine = "BLENDER_WORKBENCH"
    scn.display.shading.light = "STUDIO"
    scn.display.shading.color_type = "MATERIAL"
    scn.render.resolution_x, scn.render.resolution_y = 360, 480
    for o in scn.objects:
        if o.type == "EMPTY":
            o.hide_render = True
    arm.animation_data.action = act
    written = []
    for view, pos in (("side", (2.6, -0.4, 1.0)), ("front", (0.6, -2.6, 1.15))):
        cam.location = Vector(pos)
        cam.rotation_euler = (Vector((0, 0, 0.95)) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = 45
        for f in frames:
            scn.frame_set(f)
            scn.render.filepath = os.path.join(PREVIEWS, f"{tag}_{view}_{f:02d}.png")
            bpy.ops.render.render(write_still=True)
            written.append(scn.render.filepath)
    return written


def export(arm, objs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    arm.select_set(True)
    for o in objs:
        if o.type == "MESH":
            o.select_set(True)      # a body with the skeleton: Unity builds the clip's own Humanoid avatar from it
    bpy.context.view_layer.objects.active = arm
    for pb in arm.pose.bones:
        for c in list(pb.constraints):
            if c.name.startswith("FPSKit_"):
                pb.constraints.remove(c)
    arm.animation_data.action = None
    rest(arm)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, use_mesh_modifiers=False,
                             use_armature_deform_only=True)


def main():
    scn, arm, objs = load()
    rest(arm)
    t = setup_ik(scn, arm)
    best_pole_angles(arm, scn)
    rest_pos = {k: v.location.copy() for k, v in t.items()}
    held = bake(scn, arm, "Enemy_HeadGrabbed_Held", 73, lambda f: held_pose(arm, t, f / 72.0, rest_pos))
    impact = bake(scn, arm, "Enemy_HeadGrabbed_Impact", 37, lambda f: impact_pose(arm, t, f, rest_pos))
    previews = render(scn, arm, held, (1, 37), "held") + render(scn, arm, impact, (1, 4, 10, 20, 36), "impact")
    # One NLA track per clip, so the exporter writes each as its own take.
    arm.animation_data.action = None
    for tr in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(tr)
    for act in (held, impact):
        tr = arm.animation_data.nla_tracks.new()
        tr.name = act.name
        tr.strips.new(act.name, 1, act)
    export(arm, objs)
    print("enemy grabbed clips ->", OUT, previews)
    return {"out": OUT, "previews": previews}


if __name__ == "__main__":
    main()
