"""
The enemy's side of the head grab, keyed by hand in Blender (Ishaan's briefs of 2026-10-22; Docs/DESIGN_NOTES.md "The grab
and punch"). The first version followed the film Master by eye -- bent over by the hand on his head, limp -- and played
badly ("I can't feel that I hold the enemy, the enemy don't try to run or punch"), so the hold is now a fight:

  Enemy_HeadGrabbed_Held     (loop, 1.6 s)  the struggle: hips back and leaning away from the hand that has his head, the
                                            neck bowed against it, the trunk wrenching side to side, the feet back-pedalling
                                            (each planted foot dragged forward, each swing stepping back), both hands up on
                                            the holder's wrist and forearm, pulling
  Enemy_HeadGrabbed_Impact   (0.5 s)        a hook to his left cheek: the face knocked round to his right, the knees going,
                                            the hands jolted off the arm and back on it
  Enemy_HeadGrabbed_ImpactB  (0.5 s)        a straight or an uppercut: the head knocked back, the chin up
  Enemy_HeadGrabbed_Counter  (0.9 s)        his own punch: the left hand off the forearm and cocked by the shoulder, the
                                            trunk coiled (0.45 s, the player's window), the hook swung round onto the
                                            holder's face (arrives at 0.55 s), back onto the arm
  Enemy_HeadGrabbed_Wrench   (0.6 s)        breaking free: both hands thrown up at the arm, the head ripped back and away,
                                            the trunk turning, a step back

The holder is placed as the game holds him: his eye 0.68 m in front of the enemy's feet at 1.65 m, the hand on the right
side of the enemy's head behind the ear (MeleeGrapple.GripOn), the wrist and forearm coming forward and down from it.
Feet are held on the ground (flat) and the hands put where they belong by IK (Blender's, on a copy of the skeleton
imported with automatic bone orientation, so each bone points down its limb); the spine, the neck and the head are keyed.
Everything is then baked to plain keys on every frame (by reading the posed bones, not through the UI's bake operator, so
it runs the same headless) and exported as one FBX with a take per clip, for Unity's Humanoid import (FPSKitRiggedSoldier
puts them on the Action layer; EnemyBody lays the hands on the real wrist and steers the punch on top).

  ~/Programs/blender/blender-launcher --background --python Tools/blender/enemy_grabbed_clips.py
  CLIP_PREVIEWS=<folder> renders frames of each from the side, the front and the holder's eye.
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
    # Scale too: each turn set through a bone's matrix leaves a hair of scale on it, and on the fingers (turned every frame)
    # it built up over the clips until the hands blew apart.
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()


# Where the holder is, in the enemy's frame (his feet at the origin, facing -Y): his eye; under the wrist of the hand that
# has the enemy's head (on its right side, behind the ear), where the enemy's right hand takes it; and the forearm near the
# holder's elbow, where the enemy's left hand takes it -- low, so that from the holder's eye neither arm is across the
# face (with both hands up at the wrist they covered it; at mid-forearm the left arm still crossed the mouth).
# MeleeGrapple.WristOf and EnemyBody.GrappleHands place the same two points in the game.
HOLDER_EYE = Vector((0.0, -0.68, 1.65))
MY_WRIST = Vector((-0.16, -0.07, 1.50))
MY_FOREARM = Vector((-0.17, -0.36, 1.47))


def smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


def pulse(f, peak, rise, fall):
    """0 to 1 at frame peak and back: a quick rise, a slower fall (a blow's snap, a sag)."""
    return math.exp(-((f - peak) / rise) ** 2) if f <= peak else math.exp(-((f - peak) / fall) ** 2)


def flat_foot(arm, side, pitch=0.0):
    """The foot at its rest turn (flat on the ground, pointing ahead) wherever the IK put the ankle, the heel lifted (toes
    down) by pitch degrees, as a foot stepping back lifts it."""
    pb = arm.pose.bones[f"foot_{side}"]
    m = arm.data.bones[f"foot_{side}"].matrix_local.copy()
    m.translation = pb.matrix.translation.copy()
    pb.matrix = m
    bpy.context.view_layer.update()
    if pitch:
        turn(arm, f"foot_{side}", LEFT, pitch)


def grip_hand(arm, side, amount):
    """The fingers closed round what the hand holds (or into a fist at 1.3): each joint turned towards the palm, which is
    found from the bones (the side the thumb is on), not trusted to the rig's rolls. Below zero opens them again."""
    if amount == 0:
        return
    hand = bone_world_posed(arm, f"hand_{side}")
    fwd = (bone_world_posed(arm, f"middle_01_{side}") - hand).normalized()
    across = (bone_world_posed(arm, f"index_01_{side}") - bone_world_posed(arm, f"pinky_01_{side}")).normalized()
    palm = fwd.cross(across).normalized()
    if (bone_world_posed(arm, f"thumb_01_{side}") - hand).dot(palm) < 0:
        palm = -palm
    axis = fwd.cross(palm).normalized()
    for finger in ("index", "middle", "ring", "pinky"):
        for j, deg in ((1, 48), (2, 62), (3, 40)):
            turn(arm, f"{finger}_0{j}_{side}", axis, deg * amount)
    for j, deg in ((2, 18), (3, 22)):
        turn(arm, f"thumb_0{j}_{side}", axis, deg * amount)


def held_pose(arm, t, k, rest_pos):
    """The struggle at phase k (0..1 round the loop)."""
    rest(arm)
    for name in ("elbow_l", "elbow_r", "knee_l", "knee_r"):
        t[name].location = rest_pos[name]
    tug = 0.5 - 0.5 * math.cos(k * math.tau * 2.0)     # two hauls back a loop (the AI's tug is 0.8 s)
    twist = math.sin(k * math.tau)                      # one wrench to each side a loop
    jit = math.sin(k * math.tau * 6.0 + 0.7)            # effort
    # Hips back and down, leaning away from the hand.
    move(arm, "pelvis", UP * (-0.06 - 0.025 * tug) + FWD * (-0.05 - 0.035 * tug) + LEFT * (0.025 * twist))
    turn(arm, "pelvis", UP, 7 * twist)
    turn(arm, "pelvis", LEFT, -3 - 3 * tug)
    turn(arm, "spine_01", LEFT, -3 - 2 * tug)
    turn(arm, "spine_02", LEFT, -2)
    turn(arm, "spine_03", UP, 9 * twist + 1.2 * jit)
    turn(arm, "spine_03", LEFT, -2 - 2 * tug)
    # The head held where the hand has it: the neck bowed forward against the lean, the face to the man holding him,
    # turned against the trunk's wrench.
    turn(arm, "neck_01", LEFT, 9 + 5 * tug)
    turn(arm, "head", LEFT, 3)
    turn(arm, "head", UP, -11 * twist)
    turn(arm, "head", FWD, 5 * twist + 1.5 * jit)
    # Both hands on the arm that holds him, hauling it towards his chest.
    haul = FWD * (-0.03 * tug)
    t["hand_r"].location = MY_WRIST + haul + UP * (0.006 * jit)
    t["hand_l"].location = MY_FOREARM + haul
    # The feet back-pedalling: each planted foot dragged forward by the hold, each swing stepping back.
    for side, ph in (("l", 0.0), ("r", 0.5)):
        p = (k * 2.0 + ph) % 1.0
        stride = 0.15
        if p < 0.6:
            dy, lift = stride * 0.5 - stride * (p / 0.6), 0.0
        else:
            q = (p - 0.6) / 0.4
            dy, lift = -stride * 0.5 + stride * smooth(q), 0.07 * math.sin(math.pi * q)
        t[f"foot_{side}"].location = rest_pos[f"foot_{side}"] + Vector((0.0, 0.07 + dy, lift))
    bpy.context.view_layer.update()
    phases = {}
    for side, ph in (("l", 0.0), ("r", 0.5)):
        p = (k * 2.0 + ph) % 1.0
        phases[side] = 12 * math.sin(math.pi * (p - 0.6) / 0.4) if p >= 0.6 else 0.0
    for side in ("l", "r"):
        flat_foot(arm, side, phases[side])
    grip_hand(arm, "l", 0.8)
    grip_hand(arm, "r", 0.8)


def bone_world_posed(arm, name):
    return arm.matrix_world @ arm.pose.bones[name].head


def impact_pose(arm, t, f, rest_pos, hook):
    """A punch to the face, f frames in (0..30): the snap by frame 3, the knees going by 9, back into the struggle by 30."""
    held_pose(arm, t, 0.0, rest_pos)
    # The snap is three quarters of what looked right alone: the game adds the snap every blow to the head gets (EnemyBody),
    # and coming out of the struggle's wrench the two together turned the head 75 degrees in two frames (VerifyGrapple G3).
    snap = 0.75 * pulse(f, 3, 2.5, 8.0)
    sag = pulse(f, 9, 5.0, 10.0)
    if hook:
        # Into his left cheek: the face knocked round to his right and a little back.
        turn(arm, "neck_01", UP, -8 * snap)
        turn(arm, "head", UP, -13 * snap)
        turn(arm, "head", FWD, -7 * snap)
        turn(arm, "head", LEFT, -5 * snap)
        turn(arm, "spine_03", UP, -6 * snap)
    else:
        # Straight back, the chin up.
        turn(arm, "neck_01", LEFT, -11 * snap)
        turn(arm, "head", LEFT, -13 * snap)
        turn(arm, "spine_03", LEFT, -6 * snap)
    move(arm, "pelvis", UP * (-0.07 * sag) + FWD * (-0.03 * snap))
    turn(arm, "spine_01", LEFT, 5 * sag)
    t["hand_r"].location += UP * (-0.06 * snap) + FWD * (0.05 * snap) + LEFT * (-0.05 * snap)
    t["hand_l"].location += UP * (-0.10 * snap) + LEFT * (0.08 * snap)
    bpy.context.view_layer.update()
    for side in ("l", "r"):
        flat_foot(arm, side)


COUNTER_WIND, COUNTER_HIT, COUNTER_END = 27, 33, 54   # frames at 60 fps: 0.45 s, 0.55 s, 0.9 s


def counter_pose(arm, t, f, rest_pos):
    """His punch, f frames in: the wind-up to COUNTER_WIND, the hook arriving at COUNTER_HIT, back on the arm by the end."""
    held_pose(arm, t, 0.0, rest_pos)
    wind = smooth(f / COUNTER_WIND)
    swing = min(1.0, max(0.0, (f - COUNTER_WIND) / (COUNTER_HIT - COUNTER_WIND)))
    ret = smooth((f - COUNTER_HIT - 3) / (COUNTER_END - COUNTER_HIT - 3))
    coil = wind * (1 - swing)
    through = swing * (1 - ret)
    amount = 22 * coil - 16 * through
    turn(arm, "pelvis", UP, 0.3 * amount)
    turn(arm, "spine_01", UP, 0.25 * amount)
    turn(arm, "spine_03", UP, 0.6 * amount)
    turn(arm, "head", UP, -0.45 * amount)                       # the eyes stay on the man he is hitting
    move(arm, "pelvis", FWD * (-0.03 * coil + 0.05 * through))  # back onto the rear foot, then into it
    shoulder = bone_world_posed(arm, "upperarm_l")
    cocked = shoulder + LEFT * 0.07 + UP * 0.08 + FWD * 0.02
    face = HOLDER_EYE + Vector((0.0, 0.17, -0.03))             # just short of the eye
    if f < COUNTER_WIND:
        pos = MY_FOREARM.lerp(cocked, wind)
    elif f <= COUNTER_HIT:
        e = swing * swing
        mid = cocked + FWD * 0.22 + LEFT * 0.14
        pos = cocked.lerp(mid, e).lerp(mid.lerp(face, e), e)  # round, not straight
    else:
        pos = face.lerp(MY_FOREARM, ret)
    t["hand_l"].location = pos
    out = max(coil, through)
    t["elbow_l"].location = rest_pos["elbow_l"].lerp(shoulder + LEFT * 0.45 + UP * 0.2 - FWD * 0.15, out)
    bpy.context.view_layer.update()
    for side in ("l", "r"):
        flat_foot(arm, side)
    grip_hand(arm, "l", 0.5 * smooth(f / 8.0) * (1 - ret))   # the grip closed into a fist


def wrench_pose(arm, t, f, rest_pos):
    """Breaking free, f frames in (0..36): both hands thrown at the arm, the head ripped back and away, a step back."""
    held_pose(arm, t, 0.0, rest_pos)
    a = smooth(f / 12.0)
    b = smooth((f - 10) / 26.0)
    turn(arm, "neck_01", LEFT, -14 * a)
    turn(arm, "head", LEFT, -8 * a)
    turn(arm, "head", UP, 18 * a)
    turn(arm, "spine_03", UP, 16 * a + 10 * b)
    turn(arm, "pelvis", UP, 14 * b)
    move(arm, "pelvis", FWD * (-0.07 * a - 0.12 * b))
    t["hand_r"].location += UP * (0.12 * a) + LEFT * (-0.08 * a) + Vector((-0.14, 0.25, -0.38)) * b
    t["hand_l"].location += UP * (0.10 * a) + FWD * (0.05 * a) + Vector((0.18, 0.22, -0.35)) * b
    t["foot_r"].location += Vector((0.0, 0.30 * b, 0.08 * math.sin(math.pi * b)))
    t["foot_l"].location += Vector((0.0, 0.10 * b, 0.0))
    bpy.context.view_layer.update()
    flat_foot(arm, "l")
    flat_foot(arm, "r", 10 * math.sin(math.pi * b))
    grip_hand(arm, "l", -0.7 * a)   # open hands thrown at the arm
    grip_hand(arm, "r", -0.7 * a)


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
        if o.type == "EMPTY" or "FPS" in o.name:
            o.hide_render = True    # (the pack's first-person arms ride the same finger bones from far off)
    arm.animation_data.action = act
    # The baked keys alone: with the IK on, it re-solved every frame to wherever the last clip left its targets.
    live = [c for pb in arm.pose.bones for c in pb.constraints if c.name.startswith("FPSKit_") and not c.mute]
    for c in live:
        c.mute = True
    written = []
    for view, pos, look, lens in (("side", (2.6, -0.4, 1.0), (0, 0, 0.95), 45), ("front", (0.6, -2.6, 1.15), (0, 0, 0.95), 45),
                                  ("eye", tuple(HOLDER_EYE), (0, -0.05, 1.6), 18)):
        cam.location = Vector(pos)
        cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        cam.data.clip_start = 0.02
        for f in frames:
            scn.frame_set(f)
            scn.render.filepath = os.path.join(PREVIEWS, f"{tag}_{view}_{f:02d}.png")
            bpy.ops.render.render(write_still=True)
            written.append(scn.render.filepath)
    for c in live:
        c.mute = False
    return written


def export(arm, objs):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    arm.select_set(True)
    for o in objs:
        if o.type == "MESH" and "FPS" not in o.name:
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
    clips = [
        bake(scn, arm, "Enemy_HeadGrabbed_Held", 97, lambda f: held_pose(arm, t, f / 96.0, rest_pos)),
        bake(scn, arm, "Enemy_HeadGrabbed_Impact", 31, lambda f: impact_pose(arm, t, f, rest_pos, True)),
        bake(scn, arm, "Enemy_HeadGrabbed_ImpactB", 31, lambda f: impact_pose(arm, t, f, rest_pos, False)),
        bake(scn, arm, "Enemy_HeadGrabbed_Counter", COUNTER_END + 1, lambda f: counter_pose(arm, t, f, rest_pos)),
        bake(scn, arm, "Enemy_HeadGrabbed_Wrench", 37, lambda f: wrench_pose(arm, t, f, rest_pos)),
    ]
    previews = (render(scn, arm, clips[0], (1, 13, 25, 37, 49), "held") + render(scn, arm, clips[1], (1, 4, 10, 24), "impact")
                + render(scn, arm, clips[2], (4,), "impactB") + render(scn, arm, clips[3], (1, 14, 28, 31, 34, 45), "counter")
                + render(scn, arm, clips[4], (1, 12, 24, 37), "wrench"))
    # One NLA track per clip, so the exporter writes each as its own take.
    arm.animation_data.action = None
    for tr in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(tr)
    for act in clips:
        tr = arm.animation_data.nla_tracks.new()
        tr.name = act.name
        tr.strips.new(act.name, 1, act)
    export(arm, objs)
    print("enemy grabbed clips ->", OUT, previews)
    return {"out": OUT, "previews": previews}


if __name__ == "__main__":
    main()
