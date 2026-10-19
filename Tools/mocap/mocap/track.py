"""
Step 1 of a take: the body (and, where they are clearly seen, the hands) tracked on every frame of the video.

MediaPipe's Pose Landmarker (the heavy model, the most accurate) runs in VIDEO mode, so it follows the body from
frame to frame; it gives 33 points twice: in the picture (x, y as fractions of the frame) and in metres about the
hips ("world" landmarks, which carry the 3D shape). The Hand Landmarker runs on a crop round each wrist, sized from
the forearm, because a hand is too small in a full-body picture for its own detector to find; a hand is kept only
when the model is sure of it and its wrist lands on the body's wrist.

Nothing is cleaned here: every frame is stored as tracked, with its confidences, in landmarks.npz. solve.py does
the rest. Memory stays flat: one frame is decoded, tracked and dropped at a time.

    python -m mocap.track <video> <out.npz>
"""
import argparse
import json
import sys
import time

import numpy as np
import imageio_ffmpeg
import mediapipe as mp
from mediapipe.tasks.python import BaseOptions
from mediapipe.tasks.python import vision

from .common import MODELS, POSE, HAND_JOINTS

HAND_MIN_SCORE = 0.7       # the hand model's own confidence that it is looking at a hand
HAND_MIN_WRIST_VIS = 0.6   # the body's wrist must be seen this surely before its hand is looked for


def probe(video):
    """Frame rate, size and frame count, read from the file."""
    reader = imageio_ffmpeg.read_frames(video, pix_fmt="rgb24")
    meta = reader.__next__()
    reader.close()
    n, _ = imageio_ffmpeg.count_frames_and_secs(video)
    return meta, n


def hand_crop(frame, wrist, elbow, index, w, h):
    """A square of the full-resolution picture round one hand, and where it sits (x0, y0, size) in pixels."""
    wx, wy = wrist[0] * w, wrist[1] * h
    ex, ey = elbow[0] * w, elbow[1] * h
    ix, iy = index[0] * w, index[1] * h
    forearm = max(np.hypot(wx - ex, wy - ey), 1.0)
    size = int(max(1.7 * forearm, 96))
    cx, cy = (wx + ix) * 0.5, (wy + iy) * 0.5
    x0, y0 = int(cx - size / 2), int(cy - size / 2)
    crop = np.zeros((size, size, 3), np.uint8)
    sx0, sy0 = max(x0, 0), max(y0, 0)
    sx1, sy1 = min(x0 + size, w), min(y0 + size, h)
    if sx1 <= sx0 or sy1 <= sy0:
        return None, None
    crop[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = frame[sy0:sy1, sx0:sx1]
    return crop, (x0, y0, size)


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("video")
    ap.add_argument("out")
    ap.add_argument("--no-hands", action="store_true")
    a = ap.parse_args(argv)

    meta, count = probe(a.video)
    fps = float(meta.get("fps") or 60.0)
    w, h = meta["size"]
    rotate = int(meta.get("rotate") or 0)
    if rotate in (90, 270):        # ffmpeg turns a phone's portrait video upright while decoding
        w, h = h, w

    pose = vision.PoseLandmarker.create_from_options(vision.PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=str(MODELS / "pose_landmarker_heavy.task")),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=1,
        min_pose_detection_confidence=0.5,
        min_pose_presence_confidence=0.5,
        min_tracking_confidence=0.5))
    hands = None if a.no_hands else vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=str(MODELS / "hand_landmarker.task")),
        running_mode=vision.RunningMode.IMAGE,
        num_hands=1,
        min_hand_detection_confidence=0.5,
        min_hand_presence_confidence=0.5))

    cap = count + 16
    world = np.full((cap, 33, 3), np.nan, np.float32)
    image = np.full((cap, 33, 3), np.nan, np.float32)
    vis = np.zeros((cap, 33), np.float32)
    found = np.zeros(cap, bool)
    hand_world = np.full((cap, 2, 21, 3), np.nan, np.float32)   # [frame, left/right, joint, xyz]
    hand_score = np.zeros((cap, 2), np.float32)

    # Constant frame rate at the file's own rate: a phone's variable-rate file is evened out, never resampled.
    reader = imageio_ffmpeg.read_frames(a.video, pix_fmt="rgb24", output_params=["-fps_mode", "cfr", "-r", f"{fps:.6f}"])
    reader.__next__()
    started = time.time()
    n = 0
    for raw in reader:
        if n >= cap:
            break
        frame = np.frombuffer(raw, np.uint8)
        if frame.size != w * h * 3:
            w, h = h, w
        frame = frame.reshape(h, w, 3)
        img = mp.Image(image_format=mp.ImageFormat.SRGB, data=frame)
        res = pose.detect_for_video(img, int(round(n * 1000.0 / fps)))
        if res.pose_world_landmarks:
            found[n] = True
            for j, (lw, li) in enumerate(zip(res.pose_world_landmarks[0], res.pose_landmarks[0])):
                world[n, j] = (lw.x, lw.y, lw.z)
                image[n, j] = (li.x, li.y, li.z)
                vis[n, j] = min(li.visibility or 0.0, li.presence or 1.0)

            if hands is not None:
                for side, (wr, el, ix) in enumerate(((POSE["l_wrist"], POSE["l_elbow"], POSE["l_index"]),
                                                     (POSE["r_wrist"], POSE["r_elbow"], POSE["r_index"]))):
                    if vis[n, wr] < HAND_MIN_WRIST_VIS:
                        continue
                    crop, box = hand_crop(frame, image[n, wr], image[n, el], image[n, ix], w, h)
                    if crop is None:
                        continue
                    hr = hands.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(crop)))
                    if not hr.hand_landmarks:
                        continue
                    score = hr.handedness[0][0].score if hr.handedness else 0.0
                    if score < HAND_MIN_SCORE:
                        continue
                    # Its wrist must be the body's wrist (not the other hand straying into the crop).
                    x0, y0, size = box
                    hx = (x0 + hr.hand_landmarks[0][0].x * size) / w
                    hy = (y0 + hr.hand_landmarks[0][0].y * size) / h
                    forearm = np.hypot((image[n, wr, 0] - image[n, el, 0]) * w, (image[n, wr, 1] - image[n, el, 1]) * h)
                    if np.hypot((hx - image[n, wr, 0]) * w, (hy - image[n, wr, 1]) * h) > 0.35 * max(forearm, 1.0):
                        continue
                    hand_world[n, side] = [(p.x, p.y, p.z) for p in hr.hand_world_landmarks[0]]
                    hand_score[n, side] = score
        n += 1
        if n % 60 == 0:
            rate = n / max(time.time() - started, 1e-3)
            print(f"track: {n}/{count} frames ({rate:.1f} a second)", file=sys.stderr, flush=True)

    pose.close()
    if hands is not None:
        hands.close()

    np.savez_compressed(a.out, fps=fps, width=w, height=h, frames=n,
                        world=world[:n], image=image[:n], vis=vis[:n], found=found[:n],
                        hand_world=hand_world[:n], hand_score=hand_score[:n],
                        source=json.dumps({"video": a.video, "mediapipe": mp.__version__,
                                           "pose_model": "pose_landmarker_heavy (float16)",
                                           "hand_model": None if a.no_hands else "hand_landmarker (float16)"}))
    print(json.dumps({"frames": n, "fps": fps, "size": [w, h], "found": int(found[:n].sum()),
                      "hands": [int((hand_score[:n, 0] > 0).sum()), int((hand_score[:n, 1] > 0).sum())],
                      "seconds": round(time.time() - started, 1)}))


if __name__ == "__main__":
    main()
