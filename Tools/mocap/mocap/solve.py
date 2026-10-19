"""
Step 2 of a take: the tracked landmarks made into clean motion, in metres, standing on the ground, ready for
Unity to put on our rig (FPSKitMocap.Bake).

  1. Frames the tracker was unsure of are thrown out, point by point (low confidence, or a jump no body makes),
     and filled by interpolation from the frames either side. The number filled is reported.
  2. Where the body is in the room: MediaPipe's 3D points sit about the hips, so the hips' own travel is found
     by fitting those points onto where the same points are in the picture, through the phone's lens.
  3. Scaled to the performer: from the calibration clip (stand, arms out, turn) the height gives the scale and
     every bone's length is measured once; each frame's bones are held to those lengths.
  4. Smoothed with the One Euro filter run forwards and backwards (no lag), per joint: strong on the torso,
     light on the hands and feet, so a punch keeps its snap.
  5. The ground: "up" from the performer standing straight in the neutral pose at each end of the take, refined
     by the plane under the planted feet; the lowest planted foot is height zero. Facing is +Z at the start.
  6. Foot contacts: a foot is planted while it is on the ground and not moving. Unity locks it there.

    python -m mocap.solve <landmarks.npz> <motion.json> [--settings <asset>] [--calibration <calibration.json>]
    python -m mocap.solve <landmarks.npz> <calibration.json> --make-calibration
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np

from .common import POSE, POSE_NAMES, GROUP, FINGERS, actor, one_euro, read_settings

# Defaults for every setting the per-clip asset can override (CapturedClipSettings has the same names).
DEFAULTS = {
    "minVisibility": 0.35,     # a point the tracker gives less than this is thrown out and filled
    "maxJump": 0.18,           # metres a point may stray from its neighbours' path in one frame
    "maxSpeed": 15.0,          # metres a second no hand or foot moves faster than (a jump faster is a tracking glitch)
    "focal35": 26.0,           # the phone lens, 35 mm equivalent (main camera, 1x: about 24-28)
    "torsoCutoff": 1.2, "torsoBeta": 0.4,    # One Euro: Hz at rest, and how fast it opens up with speed
    "limbCutoff": 2.0, "limbBeta": 0.5,
    "endCutoff": 3.0, "endBeta": 0.5,        # hands and feet: lightest, so fast moves stay sharp
    "headCutoff": 1.5, "headBeta": 0.5,
    "depthCutoff": 0.6,        # the body's distance from the phone: the noisiest number, smoothed hardest
    "contactHeight": 0.07,     # metres above the ground a planted foot may be
    "contactSpeed": 0.45,      # metres a second a planted foot may drift
    "levelPhone": 0,           # 1: take the phone as level and its up as the world's; 0: up from the performer standing straight and the planted feet
}

LIMB_CHAINS = [  # (parent, child): held to the calibrated length, outward from the torso
    ("l_shoulder", "l_elbow"), ("l_elbow", "l_wrist"), ("r_shoulder", "r_elbow"), ("r_elbow", "r_wrist"),
    ("l_hip", "l_knee"), ("l_knee", "l_ankle"), ("r_hip", "r_knee"), ("r_knee", "r_ankle"),
]
END_POINTS = {  # carried with their joint, at their own measured offset
    "l_wrist": ["l_pinky", "l_index", "l_thumb"], "r_wrist": ["r_pinky", "r_index", "r_thumb"],
    "l_ankle": ["l_heel", "l_foot"], "r_ankle": ["r_heel", "r_foot"],
}
CORE = [POSE[n] for n in ("l_shoulder", "r_shoulder", "l_hip", "r_hip")]
BODY = [POSE[n] for n in ("l_shoulder", "r_shoulder", "l_elbow", "r_elbow", "l_wrist", "r_wrist", "l_hip", "r_hip",
                          "l_knee", "r_knee", "l_ankle", "r_ankle", "l_heel", "r_heel", "l_foot", "r_foot")]
KEY = [POSE[n] for n in ("l_shoulder", "r_shoulder", "l_elbow", "r_elbow", "l_wrist", "r_wrist", "l_hip", "r_hip",
                         "l_knee", "r_knee", "l_ankle", "r_ankle")]

# Standing eye height over the heel point, as a share of stature (adult anthropometry: eyes at 0.936 of height,
# MediaPipe's heel point about 2 cm off the floor).
EYE_OVER_HEEL = 0.925


def mid(p, a, b):
    return 0.5 * (p[..., POSE[a], :] + p[..., POSE[b], :])


def unit(v):
    n = np.linalg.norm(v, axis=-1, keepdims=True)
    return v / np.maximum(n, 1e-9)


# ---- 1. reject and fill ---------------------------------------------------------------------------------------------

def reject_and_fill(world, image, vis, found, s, fps=60.0):
    n = len(world)
    ok = np.repeat(found[:, None], 33, axis=1) & (vis >= s["minVisibility"])
    frame_ok = found & (vis[:, CORE].mean(axis=1) >= 0.5)
    ok &= frame_ok[:, None]

    # A glitch: a hidden arm or leg the tracker loses for a few frames, guessed somewhere else, then found again.
    # It shows as a jump faster than a hand or foot can move (a hard punch is 8-12 m/s) and, within a quarter
    # of a second, a jump back to near where it was. The frames between are thrown out and filled.
    for j, name in enumerate(POSE_NAMES):
        g = GROUP[name]
        if g not in ("end", "limb"):
            continue
        limit = s["maxSpeed"] / fps * (1.0 if g == "end" else 0.75)
        p = world[:, j]
        step = np.r_[0, np.linalg.norm(np.diff(p, axis=0), axis=-1)]
        for i in np.where(step > limit)[0]:
            if i < 1 or not ok[i - 1, j]:
                continue
            base = p[i - 1]
            for k in range(i + 1, min(n, i + int(fps * 0.25) + 1)):
                if np.linalg.norm(p[k] - base) < 0.5 * step[i]:
                    ok[i:k, j] = False
                    break

    # A jump: the point far from the median of its neighbours (two each side) in the hip-centred metres.
    for j in range(33):
        p = world[:, j]
        for i in range(2, n - 2):
            if not ok[i, j]:
                continue
            around = [k for k in (i - 2, i - 1, i + 1, i + 2) if ok[k, j]]
            if len(around) >= 3 and np.linalg.norm(p[i] - np.median(p[around], axis=0)) > s["maxJump"]:
                ok[i, j] = False

    filled_points = 0
    t = np.arange(n)
    for j in range(33):
        good = ok[:, j]
        if good.all():
            continue
        if good.sum() < 2:
            ok[:, j] = False
            continue
        filled_points += int((~good).sum())
        for arr in (world, image):
            for k in range(3):
                arr[:, j, k] = np.interp(t, t[good], arr[good, j, k])
    filled_frames = int((~ok[:, KEY]).any(axis=1).sum())
    return ok, filled_frames, filled_points, int((~frame_ok).sum())


# ---- 1b. left and right mixed up -------------------------------------------------------------------------------

SWAP_GROUPS = [
    [("l_elbow", "r_elbow"), ("l_wrist", "r_wrist"), ("l_pinky", "r_pinky"), ("l_index", "r_index"), ("l_thumb", "r_thumb")],
    [("l_knee", "r_knee"), ("l_ankle", "r_ankle"), ("l_heel", "r_heel"), ("l_foot", "r_foot")],
]


def unswap(world, image, vis):
    """
    Seen from the side, the tracker now and then calls the far arm (or leg) the near one for a few frames: the two
    swap places in one frame, a jump no body makes. Each frame's arms, then legs, are swapped back when the swapped
    labels follow on from the frame before far better than the given ones. Returns how many frames were mended.
    """
    mended = 0
    for group in SWAP_GROUPS:
        a = [POSE[l] for l, _ in group]
        b = [POSE[r] for _, r in group]
        for i in range(1, len(world)):
            keep = np.linalg.norm(world[i, a] - world[i - 1, a], axis=-1).sum() + np.linalg.norm(world[i, b] - world[i - 1, b], axis=-1).sum()
            swap = np.linalg.norm(world[i, b] - world[i - 1, a], axis=-1).sum() + np.linalg.norm(world[i, a] - world[i - 1, b], axis=-1).sum()
            if keep > 0.12 * len(a) and swap < 0.5 * keep:
                for arr in (world, image, vis):
                    arr[i, a], arr[i, b] = arr[i, b].copy(), arr[i, a].copy()
                mended += 1
    return mended


# ---- 2. where the body is -----------------------------------------------------------------------------------------

def solve_translation(world, image, vis, ok, w, h, f):
    """Per frame, the hips' place in front of the camera (camera axes: x right, y down, z away), MediaPipe metres."""
    cx, cy = w * 0.5, h * 0.5
    n = len(world)
    t = np.zeros((n, 3))
    last = np.array([0.0, 0.0, 3.0])
    for i in range(n):
        use = [j for j in BODY if ok[i, j]]
        if len(use) < 6:
            t[i] = last
            continue
        X, Y, Z = world[i, use, 0], world[i, use, 1], world[i, use, 2]
        u, v = image[i, use, 0] * w, image[i, use, 1] * h
        wt = np.sqrt(np.clip(vis[i, use], 0.05, 1.0))
        A = np.zeros((2 * len(use), 3))
        b = np.zeros(2 * len(use))
        A[0::2, 0] = f; A[0::2, 2] = cx - u; b[0::2] = (u - cx) * Z - f * X
        A[1::2, 1] = f; A[1::2, 2] = cy - v; b[1::2] = (v - cy) * Z - f * Y
        A *= np.repeat(wt, 2)[:, None]
        b *= np.repeat(wt, 2)
        sol, *_ = np.linalg.lstsq(A, b, rcond=None)
        if sol[2] > 0.3:
            last = sol
        t[i] = last
    return t


# ---- 3. lengths ---------------------------------------------------------------------------------------------------

def measure_lengths(p, ok):
    """Median length of each bone over the frames where both its ends were seen."""
    lengths = {}
    for a, b in LIMB_CHAINS + [(j, e) for j, ends in END_POINTS.items() for e in ends]:
        both = ok[:, POSE[a]] & ok[:, POSE[b]]
        d = np.linalg.norm(p[:, POSE[b]] - p[:, POSE[a]], axis=-1)
        lengths[f"{a}>{b}"] = float(np.median(d[both] if both.sum() > 5 else d))
    # Left and right are the same body: each pair is given their mean.
    for k in list(lengths):
        if k.startswith("l_"):
            other = "r_" + k[2:].replace(">l_", ">r_")
            if other in lengths:
                m = 0.5 * (lengths[k] + lengths[other])
                lengths[k] = lengths[other] = m
    return lengths


def hold_lengths(p, lengths):
    """Each limb rebuilt outward from the torso along its tracked directions, at its calibrated length."""
    q = p.copy()
    for a, b in LIMB_CHAINS:                  # parents before children
        d = unit(p[:, POSE[b]] - p[:, POSE[a]])
        moved = q[:, POSE[a]] + d * lengths[f"{a}>{b}"]
        shift = moved - q[:, POSE[b]]
        q[:, POSE[b]] = moved
        for e in END_POINTS.get(b, []):       # the hand's and foot's points go with their joint
            q[:, POSE[e]] += shift
    return q


# ---- 4. smoothing -------------------------------------------------------------------------------------------------

def trust_of(vis, ok):
    """How far each point is believed, 0.3..1: its visibility (0.8 and up is full), and 0.3 where it was filled in."""
    t = np.clip(vis / 0.8, 0.3, 1.0)
    return np.where(ok, t, 0.3)


def smooth(p, fps, s, trust):
    q = p.copy()
    for j, name in enumerate(POSE_NAMES):
        g = GROUP[name]
        q[:, j] = one_euro(p[:, j], fps, s[g + "Cutoff"], s[g + "Beta"], trust=trust[:, j])
    return q


def worst_jerk(p, fps, joints):
    """The worst frame-to-frame jerk (third difference, m/s^3) over the given points."""
    if len(p) < 5:
        return 0.0
    j = np.diff(p[:, joints], n=3, axis=0) * fps ** 3
    return float(np.linalg.norm(j, axis=-1).max())


# ---- 5. the ground --------------------------------------------------------------------------------------------------

def neutral_frames(p, fps, ok):
    """The first and last 0.4 s the body was well seen: the take's neutral pose (the recording guide asks for it)."""
    good = np.where(ok[:, CORE].all(axis=1))[0]
    k = max(3, int(0.4 * fps))
    return np.concatenate([good[:k], good[-k:]]) if len(good) > 2 * k else good


def ground_frame(p, fps, ok, s, up_hint=None):
    """Rotation from camera axes (y up already) to the world (y up, the performer facing +z at the start)."""
    nf = neutral_frames(p, fps, ok)
    feet = 0.5 * (mid(p, "l_ankle", "r_ankle") + mid(p, "l_heel", "r_heel"))
    up = unit(np.mean(mid(p, "l_shoulder", "r_shoulder")[nf] - feet[nf], axis=0)) if up_hint is None else unit(np.array(up_hint))
    if s.get("levelPhone", 0) >= 0.5:
        # Asked for: the phone's own up as the world's. Not the default: MediaPipe's 3D points are not quite square to
        # the camera (a synthetic take came out leaning 15 degrees this way, 19 cm off against 15 from the body).
        up = np.array([0.0, 1.0, 0.0])

    # Refined by the plane under the feet where they are planted (low and still), when they cover enough ground.
    pts = []
    for side in "lr":
        for name in ("heel", "foot"):
            q = p[:, POSE[f"{side}_{name}"]]
            speed = np.r_[0, np.linalg.norm(np.diff(q, axis=0), axis=-1) * fps]
            height = q @ up
            low = height < np.percentile(height, 30) + 0.03
            pts.append(q[low & (speed < s["contactSpeed"])])
    pts = np.concatenate(pts) if pts else np.zeros((0, 3))
    plane_used = False
    if len(pts) > 30 and s.get("levelPhone", 0) < 0.5:
        c = pts - pts.mean(axis=0)
        evals, evecs = np.linalg.eigh(c.T @ c / len(c))
        normal = evecs[:, 0] * np.sign(evecs[:, 0] @ up)
        spread = np.sqrt(evals[1:])
        if spread.min() > 0.08 and np.degrees(np.arccos(np.clip(normal @ up, -1, 1))) < 15:
            up = unit(0.5 * up + 0.5 * normal)
            plane_used = True

    right = mid(p, "r_hip", "r_hip")[nf[: max(1, len(nf) // 2)]] - mid(p, "l_hip", "l_hip")[nf[: max(1, len(nf) // 2)]]
    right = unit(np.mean(right, axis=0))
    right = unit(right - up * (right @ up))
    forward = np.cross(right, up)      # as Unity's Vector3.Cross(right, up): x right, y up, z forward
    R = np.stack([right, up, forward])
    return R, plane_used


def contacts(p, fps, s):
    out = {}
    for side in "lr":
        heel, toe = p[:, POSE[f"{side}_heel"]], p[:, POSE[f"{side}_foot"]]
        h = np.minimum(heel[:, 1], toe[:, 1])
        c = 0.5 * (heel + toe)
        v = np.r_[0, np.linalg.norm(np.diff(c[:, [0, 2]], axis=0), axis=-1) * fps]
        v = np.convolve(v, np.ones(5) / 5, mode="same")
        on = (h < s["contactHeight"]) & (v < s["contactSpeed"])
        # Tidy: gaps of up to 3 frames closed, plants shorter than 4 frames dropped.
        on = _close(on, 3)
        on = _open(on, 4)
        out[side] = on
    return out


def _runs(mask):
    runs, start = [], None
    for i, m in enumerate(mask):
        if m and start is None:
            start = i
        if not m and start is not None:
            runs.append((start, i)); start = None
    if start is not None:
        runs.append((start, len(mask)))
    return runs


def _close(mask, gap):
    mask = mask.copy()
    runs = _runs(mask)
    for (a0, a1), (b0, b1) in zip(runs, runs[1:]):
        if b0 - a1 <= gap:
            mask[a1:b0] = True
    return mask


def _open(mask, shortest):
    mask = mask.copy()
    for a, b in _runs(mask):
        if b - a < shortest:
            mask[a:b] = False
    return mask


# ---- the hands --------------------------------------------------------------------------------------------------

def hand_data(hand_world, hand_score, p_cam_axes, R, fps, s, trust):
    """
    Per hand and frame: the finger bends (degrees at each finger's three joints, and the spread) where the hand
    model saw the hand, and the hand's frame (the way the fingers run, the line across the knuckles) in the world.
    Short gaps in the hand model are filled; long ones are left empty (Unity holds the clip's hand pose there).
    """
    n = len(hand_score)
    out = {}
    for side, s_idx in (("l", 0), ("r", 1)):
        seen = hand_score[:, s_idx] > 0
        bends = np.full((n, 5, 3), np.nan)
        spread = np.full((n, 4), np.nan)
        for i in np.where(seen)[0]:
            hw = hand_world[i, s_idx].astype(float)
            dirs = []
            for f, (fname, idx) in enumerate(FINGERS.items()):
                chain = [0] + idx if fname != "thumb" else idx
                for k in range(3):
                    a, b, c = hw[chain[k]], hw[chain[k + 1]], hw[chain[k + 2]] if k + 2 < len(chain) else None
                    if c is None:
                        break
                    v1, v2 = unit(b - a), unit(c - b)
                    bends[i, f, k] = np.degrees(np.arccos(np.clip(v1 @ v2, -1, 1)))
                dirs.append(unit(hw[idx[1]] - hw[idx[0]]))
            for k in range(4):
                spread[i, k] = np.degrees(np.arccos(np.clip(dirs[k] @ dirs[k + 1], -1, 1)))
        # Fill gaps of up to a third of a second; leave longer ones empty.
        t = np.arange(n)
        good = ~np.isnan(bends[:, 1, 0])
        if good.sum() >= 2:
            for arr in (bends.reshape(n, -1), spread):
                for k in range(arr.shape[1]):
                    filled = np.interp(t, t[good], arr[good, k])
                    gap_ok = _gap_mask(good, int(fps / 3))
                    arr[:, k] = np.where(good | gap_ok, filled, np.nan)

        # The hand's frame: from the hand model where it saw the hand, else from the body's wrist, index and pinky.
        pw = p_cam_axes
        wrist, index, pinky = pw[:, POSE[f"{side}_wrist"]], pw[:, POSE[f"{side}_index"]], pw[:, POSE[f"{side}_pinky"]]
        fingers = unit(0.5 * (index + pinky) - wrist)
        across = unit(index - pinky)
        for i in np.where(seen)[0]:
            hw = hand_world[i, s_idx].astype(float) * np.array([1, -1, 1])   # camera axes, y up
            fingers[i] = unit(hw[9] - hw[0])
            across[i] = unit(hw[5] - hw[17])
        tw = np.maximum(trust[:, POSE[f"{side}_wrist"]], np.where(seen, 1.0, 0.0))
        fingers = unit(one_euro(fingers @ R.T, fps, s["endCutoff"], s["endBeta"], trust=tw))
        across = unit(one_euro(across @ R.T, fps, s["endCutoff"], s["endBeta"], trust=tw))
        out[side] = {"seen": seen, "bends": bends, "spread": spread, "fingers": fingers, "across": across}
    return out


def _gap_mask(good, longest):
    m = np.zeros_like(good)
    for a, b in _runs(~good):
        if b - a <= longest and a > 0 and b < len(good):
            m[a:b] = True
    return m


# ---- auto trim ----------------------------------------------------------------------------------------------------

def auto_trim(p, fps):
    """Where the move starts and ends: the hands, feet and hips moving, with a little held either side."""
    hips = mid(p, "l_hip", "r_hip")
    ends = p[:, [POSE[n] for n in ("l_wrist", "r_wrist", "l_ankle", "r_ankle")]] - hips[:, None]
    speed = np.r_[0, (np.linalg.norm(np.diff(ends, axis=0), axis=-1).max(axis=1)
                      + np.linalg.norm(np.diff(hips, axis=0), axis=-1)) * fps]
    speed = np.convolve(speed, np.ones(7) / 7, mode="same")
    level = 0.15 * np.percentile(speed, 95)
    active = np.where(speed > max(level, 0.25))[0]
    if len(active) == 0:
        return 0.0, (len(p) - 1) / fps
    a = max(0, active[0] - int(0.3 * fps))
    b = min(len(p) - 1, active[-1] + int(0.4 * fps))
    return a / fps, b / fps


# ======================================================================================================================

def load(npz):
    d = np.load(npz)
    return (float(d["fps"]), int(d["width"]), int(d["height"]), d["world"].astype(float), d["image"].astype(float),
            d["vis"].astype(float), d["found"], d["hand_world"], d["hand_score"], json.loads(str(d["source"])))


def make_calibration(npz, out, height):
    fps, w, h, world, image, vis, found, _, _, source = load(npz)
    s = dict(DEFAULTS)
    unswap(world, image, vis)
    ok, filled, filled_points, lost = reject_and_fill(world, image, vis, found, s, fps)
    p = world * np.array([1, -1, 1])           # camera axes with y up
    # The first second the body is still: standing straight.
    speed = np.r_[0, np.linalg.norm(np.diff(p[:, KEY], axis=0), axis=-1).max(axis=1) * fps]
    still = np.where((np.convolve(speed, np.ones(9) / 9, mode="same") < 0.6) & ok[:, CORE].all(axis=1))[0]
    stand = still[: int(1.0 * fps)] if len(still) > int(0.5 * fps) else np.arange(min(len(p), int(fps)))
    feet = mid(p, "l_heel", "r_heel")
    up = unit(np.mean(mid(p, "l_shoulder", "r_shoulder")[stand] - mid(p, "l_ankle", "r_ankle")[stand], axis=0))
    eyes = mid(p, "l_eye", "r_eye")
    eye_height = float(np.median((eyes[stand] - feet[stand]) @ up))
    scale = EYE_OVER_HEEL * height / eye_height
    lengths = {k: v * scale for k, v in measure_lengths(p, ok).items()}
    # Arms out: the widest the two index points get, a check on the scale (an arm span is about a height).
    span = float(np.percentile(np.linalg.norm(p[:, POSE["l_index"]] - p[:, POSE["r_index"]], axis=-1), 97) * scale)
    cal = {"name": Path(npz).parent.name, "source": source, "height": height, "scale": scale, "lengths": lengths, "eye_height_mp": eye_height,
           "arm_span": span, "span_over_height": span / height, "frames": len(p), "filled_frames": filled,
           "stand_frames": int(len(stand))}
    with open(out, "w") as fh:
        json.dump(cal, fh, indent=1)
    print(json.dumps({k: cal[k] for k in ("scale", "arm_span", "span_over_height", "filled_frames")}))


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("npz")
    ap.add_argument("out")
    ap.add_argument("--settings")
    ap.add_argument("--calibration")
    ap.add_argument("--make-calibration", action="store_true")
    ap.add_argument("--height", type=float)
    a = ap.parse_args(argv)
    height = a.height or actor()["height"]
    if a.make_calibration:
        make_calibration(a.npz, a.out, height)
        return

    s = dict(DEFAULTS)
    if a.settings:
        s.update({k: v for k, v in read_settings(a.settings).items() if k in DEFAULTS})

    fps, w, h, world, image, vis, found, hand_world, hand_score, source = load(a.npz)
    n = len(world)
    swapped = unswap(world, image, vis)
    ok, filled_frames, filled_points, lost_frames = reject_and_fill(world, image, vis, found, s, fps)

    f_px = s["focal35"] / 43.267 * np.hypot(w, h)
    t = solve_translation(world, image, vis, ok, w, h, f_px)

    cal = json.load(open(a.calibration)) if a.calibration else None
    if cal:
        scale = cal["scale"]
    else:
        # No calibration: the standing height in the neutral pose gives the scale.
        p0 = world * np.array([1, -1, 1])
        nf = neutral_frames(p0, fps, ok)
        upn = unit(np.mean(mid(p0, "l_shoulder", "r_shoulder")[nf] - mid(p0, "l_ankle", "r_ankle")[nf], axis=0))
        scale = EYE_OVER_HEEL * height / float(np.median((mid(p0, "l_eye", "r_eye")[nf] - mid(p0, "l_heel", "r_heel")[nf]) @ upn))

    # Camera axes with y up (MediaPipe's y runs down the picture), metres of the performer.
    flip = np.array([1, -1, 1])
    t_s = t * scale
    t_s[:, 2] = one_euro(t_s[:, 2:3], fps, s["depthCutoff"], s["torsoBeta"])[:, 0]
    t_s[:, :2] = one_euro(t_s[:, :2], fps, s["torsoCutoff"], s["torsoBeta"])
    body = world * scale
    ends = [POSE[k] for k in ("l_wrist", "r_wrist", "l_ankle", "r_ankle", "nose")]
    jerk_raw = worst_jerk((body + t[:, None, :] * scale) * flip, fps, ends)   # as tracked (gaps filled), before smoothing

    trust = trust_of(vis, ok)
    body = smooth(body, fps, s, trust)
    lengths = {k: v for k, v in cal["lengths"].items()} if cal else measure_lengths(body, ok)
    body = hold_lengths(body, lengths)
    p = (body + t_s[:, None, :]) * flip

    R, plane_used = ground_frame(p, fps, ok, s)
    p = p @ R.T
    # Height zero: the planted feet.
    feet_low = np.minimum(np.minimum(p[:, POSE["l_heel"], 1], p[:, POSE["l_foot"], 1]),
                          np.minimum(p[:, POSE["r_heel"], 1], p[:, POSE["r_foot"], 1]))
    ground = float(np.percentile(feet_low, 5))
    p[..., 1] -= ground
    start = mid(p, "l_hip", "r_hip")[0].copy()
    p[..., 0] -= start[0]
    p[..., 2] -= start[2]

    c = contacts(p, fps, s)
    hands = hand_data(hand_world, hand_score, (world * scale + t_s[:, None, :]) * flip, R, fps, s, trust)
    trim = auto_trim(p, fps)

    jerk = worst_jerk(p, fps, ends)
    motion = {
        "take": source.get("video", ""), "fps": fps, "frames": n, "height": height, "scale": scale,
        "calibrated": bool(cal), "focalPixels": f_px, "groundFromFeet": plane_used,
        "autoStart": trim[0], "autoEnd": trim[1],
        "joints": POSE_NAMES,
        "positions": np.round(p.reshape(-1), 5).tolist(),
        "contactLeft": c["l"].astype(int).tolist(), "contactRight": c["r"].astype(int).tolist(),
        "handLeftSeen": hands["l"]["seen"].astype(int).tolist(), "handRightSeen": hands["r"]["seen"].astype(int).tolist(),
        "handLeftBends": np.nan_to_num(hands["l"]["bends"], nan=-1).round(2).reshape(-1).tolist(),
        "handRightBends": np.nan_to_num(hands["r"]["bends"], nan=-1).round(2).reshape(-1).tolist(),
        "handLeftSpread": np.nan_to_num(hands["l"]["spread"], nan=-1).round(2).reshape(-1).tolist(),
        "handRightSpread": np.nan_to_num(hands["r"]["spread"], nan=-1).round(2).reshape(-1).tolist(),
        "handLeftFrame": np.round(np.concatenate([hands["l"]["fingers"], hands["l"]["across"]], axis=1), 5).reshape(-1).tolist(),
        "handRightFrame": np.round(np.concatenate([hands["r"]["fingers"], hands["r"]["across"]], axis=1), 5).reshape(-1).tolist(),
        "filledFrames": filled_frames, "filledPoints": filled_points, "lostFrames": lost_frames, "swappedFrames": swapped,
        "jerkRaw": jerk_raw, "jerkClean": jerk,
        # Where the phone was, in the same space: the preview films the clip from the same place.
        "cameraPosition": [float(-start[0]), float(-ground), float(-start[2])],
        "cameraForward": (R @ np.array([0.0, 0.0, 1.0])).tolist(), "cameraUp": (R @ np.array([0.0, 1.0, 0.0])).tolist(),
        "cameraFov": float(np.degrees(2 * np.arctan(h / 2 / f_px))), "videoWidth": w, "videoHeight": h,
        "calibrationName": cal.get("name", "") if cal else "",
        "settings": s, "source": source,
    }
    with open(a.out, "w") as fh:
        json.dump(motion, fh)
    print(json.dumps({"frames": n, "filledFrames": filled_frames, "filledPoints": filled_points, "lostFrames": lost_frames,
                      "swapped": swapped, "jerkRaw": round(jerk_raw), "jerkClean": round(jerk), "scale": round(scale, 4),
                      "groundFromFeet": plane_used, "autoTrim": [round(trim[0], 2), round(trim[1], 2)],
                      "planted": [int(c["l"].sum()), int(c["r"].sum())],
                      "handsSeen": [int(hands["l"]["seen"].sum()), int(hands["r"]["seen"].sum())]}))


if __name__ == "__main__":
    main()
