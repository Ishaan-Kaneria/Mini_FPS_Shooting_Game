"""
Frames filmed by FPSKitPlayerShots (fp_NNNNN.jpg and side_NNNNN.jpg, one pair per game frame at 30 fps) into an mp4 with
the player's eyes on the left and the side camera on the right, and contact sheets of every Nth frame for reading the
motion as stills.

  Tools/mocap/.venv/bin/python Tools/shots/frames_to_video.py Build/PlayerShots/desert_video [--every 3] [--from 1 --to 999]

Writes <folder>.mp4, <folder>_sheet_NN.png (fp and side frames in pairs of rows, numbered).
"""
import argparse
import glob
import os

import cv2
import numpy as np


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--every", type=int, default=3)
    ap.add_argument("--start", type=int, default=1)
    ap.add_argument("--end", type=int, default=10 ** 6)
    ap.add_argument("--cols", type=int, default=6)
    ap.add_argument("--fps", type=float, default=30.0)
    a = ap.parse_args()

    fps_files = sorted(glob.glob(os.path.join(a.folder, "fp_*.jpg")))
    pairs = []
    for f in fps_files:
        n = int(os.path.basename(f)[3:8])
        side = os.path.join(a.folder, f"side_{n:05d}.jpg")
        if os.path.exists(side):
            pairs.append((n, f, side))
    if not pairs:
        raise SystemExit("no frames in " + a.folder)

    first = cv2.imread(pairs[0][1])
    h, w = first.shape[:2]
    out = a.folder.rstrip("/") + ".mp4"
    writer = cv2.VideoWriter(out, cv2.VideoWriter_fourcc(*"mp4v"), a.fps, (w * 2, h))
    for n, f, s in pairs:
        frame = np.hstack([cv2.imread(f), cv2.imread(s)])
        cv2.putText(frame, str(n), (8, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
        writer.write(frame)
    writer.release()

    chosen = [p for p in pairs if a.start <= p[0] <= a.end and (p[0] - a.start) % a.every == 0]
    tw, th = w // 3, h // 3
    per_sheet = a.cols * 4
    sheets = []
    for i in range(0, len(chosen), per_sheet):
        block = chosen[i:i + per_sheet]
        rows = (len(block) + a.cols - 1) // a.cols
        sheet = np.zeros((rows * th * 2, a.cols * tw, 3), np.uint8)
        for j, (n, f, s) in enumerate(block):
            r, c = divmod(j, a.cols)
            for k, path in enumerate((f, s)):
                img = cv2.resize(cv2.imread(path), (tw, th), interpolation=cv2.INTER_AREA)
                cv2.putText(img, str(n), (4, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 0), 1)
                sheet[(r * 2 + k) * th:(r * 2 + k + 1) * th, c * tw:(c + 1) * tw] = img
        name = f"{a.folder.rstrip('/')}_sheet_{len(sheets) + 1:02d}.png"
        cv2.imwrite(name, sheet)
        sheets.append(name)
    print(out, len(pairs), "frames;", ", ".join(sheets))


if __name__ == "__main__":
    main()
