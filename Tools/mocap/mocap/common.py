"""Shared by the capture pipeline's steps: landmark names, paths, the One Euro filter and the per-clip settings."""
import json
import os
import re
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent.parent          # Tools/mocap
PROJECT = HERE.parent.parent
MODELS = HERE / "models"
WORK = HERE / "work"
VIDEOS = Path(os.environ.get("MOCAP_VIDEOS", Path.home() / "MocapVideos"))

# MediaPipe Pose's 33 points (the subject's own left and right).
POSE_NAMES = ["nose", "l_eye_inner", "l_eye", "l_eye_outer", "r_eye_inner", "r_eye", "r_eye_outer", "l_ear", "r_ear",
              "mouth_l", "mouth_r", "l_shoulder", "r_shoulder", "l_elbow", "r_elbow", "l_wrist", "r_wrist",
              "l_pinky", "r_pinky", "l_index", "r_index", "l_thumb", "r_thumb", "l_hip", "r_hip", "l_knee", "r_knee",
              "l_ankle", "r_ankle", "l_heel", "r_heel", "l_foot", "r_foot"]
POSE = {n: i for i, n in enumerate(POSE_NAMES)}

# MediaPipe Hands' 21 points: the wrist, then four per finger from the base out.
HAND_JOINTS = ["wrist", "thumb_cmc", "thumb_mcp", "thumb_ip", "thumb_tip", "index_mcp", "index_pip", "index_dip",
               "index_tip", "middle_mcp", "middle_pip", "middle_dip", "middle_tip", "ring_mcp", "ring_pip", "ring_dip",
               "ring_tip", "pinky_mcp", "pinky_pip", "pinky_dip", "pinky_tip"]
FINGERS = {"thumb": [1, 2, 3, 4], "index": [5, 6, 7, 8], "middle": [9, 10, 11, 12], "ring": [13, 14, 15, 16],
           "little": [17, 18, 19, 20]}

# Which smoothing each point gets: the torso is slow and heavy, the hands and feet fast and light.
GROUP = {}
for n in POSE_NAMES:
    if n.endswith(("shoulder", "hip")):
        GROUP[n] = "torso"
    elif n.endswith(("elbow", "knee")):
        GROUP[n] = "limb"
    elif n.endswith(("wrist", "pinky", "index", "thumb", "ankle", "heel", "foot")):
        GROUP[n] = "end"
    else:
        GROUP[n] = "head"


def actor():
    """The performer: height in metres (Tools/mocap/actor.json, 1.75 for Ishaan)."""
    path = HERE / "actor.json"
    data = json.loads(path.read_text()) if path.exists() else {}
    return {"height": float(data.get("height_m", 1.75))}


# ---- the One Euro filter -----------------------------------------------------------------------------------------

def _alpha(cutoff, fps):
    tau = 1.0 / (2.0 * np.pi * cutoff)
    return 1.0 / (1.0 + tau * fps)


def one_euro_pass(x, fps, min_cutoff, beta, d_cutoff=1.0, trust=None):
    """
    One pass of the One Euro filter (Casiez et al. 2012) over x (frames, dims), cutoff from the point's speed. With
    trust (frames,), 0..1, the cutoff is lowered where the tracker was unsure of the point: a hidden arm is
    smoothed harder than one in plain sight.
    """
    out = np.empty_like(x)
    out[0] = x[0]
    dx_hat = np.zeros(x.shape[1])
    a_d = _alpha(d_cutoff, fps)
    for i in range(1, len(x)):
        dx = (x[i] - out[i - 1]) * fps
        dx_hat = dx_hat + a_d * (dx - dx_hat)
        cutoff = min_cutoff + beta * np.linalg.norm(dx_hat)
        if trust is not None:
            cutoff *= trust[i]
        a = _alpha(cutoff, fps)
        out[i] = out[i - 1] + a * (x[i] - out[i - 1])
    return out


def one_euro(x, fps, min_cutoff, beta, d_cutoff=1.0, trust=None):
    """
    The filter run forwards and backwards in time and the two averaged. Each pass alone trails the motion a
    little (a low-pass filter always lags); run both ways the lags are equal and opposite and cancel, so a fast
    punch keeps its timing and its full reach while the jitter at rest is still taken out. Offline only.
    """
    f = one_euro_pass(x, fps, min_cutoff, beta, d_cutoff, trust)
    b = one_euro_pass(x[::-1], fps, min_cutoff, beta, d_cutoff, None if trust is None else trust[::-1])[::-1]
    return 0.5 * (f + b)


# ---- the per-clip settings asset ------------------------------------------------------------------------------------

def read_settings(path):
    """
    The flat fields of a CapturedClipSettings asset (Unity YAML), as a dict; {} when there is none yet. Only plain
    numbers and strings are read: the asset is kept flat on purpose so this stays a few lines.
    """
    path = Path(path)
    if not path.exists():
        return {}
    out = {}
    inside = False
    for line in path.read_text().splitlines():
        if line.startswith("MonoBehaviour:"):
            inside = True
            continue
        if not inside:
            continue
        m = re.match(r"^  (\w+): ?(.*)$", line)
        if not m:
            continue
        key, value = m.group(1), m.group(2).strip()
        if key.startswith("m_"):
            continue
        try:
            out[key] = float(value)
        except ValueError:
            out[key] = value
    return out


def parse_take(name):
    """'player_punch_01' -> ('Player', 'Punch', 1); 'enemy_hit_left_02' -> ('Enemy', 'HitLeft', 2)."""
    stem = Path(name).stem
    parts = stem.split("_")
    if len(parts) < 3 or not parts[-1].isdigit():
        raise SystemExit(f"take '{stem}' is not named who_move_NN (e.g. player_punch_01)")
    who = parts[0].lower()
    move = "".join(p.capitalize() for p in parts[1:-1])
    return ("Player" if who == "player" else "Enemy" if who == "enemy" else who.capitalize()), move, int(parts[-1])
