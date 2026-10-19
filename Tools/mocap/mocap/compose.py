"""
The preview video of a take: Ishaan's own video in the top-left quarter, Unity's three panels (panels.mp4, from
FPSKitMocap.Preview) in the other three, a label on each, 1920 x 1080 at 60 frames a second, H.264.

    python -m mocap.compose <take> <video> <work dir> <out.mp4> <settings.asset>
"""
import json
import subprocess
import sys
from pathlib import Path

import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFont

from .common import read_settings, parse_take

W, H = 1920, 1080
PW, PH = 960, 540


def font(size):
    for f in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf"):
        if Path(f).exists():
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def labels(path, texts):
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    f = font(26)
    for (x, y), text in texts:
        box = d.textbbox((0, 0), text, font=f)
        w, h = box[2] - box[0], box[3] - box[1]
        d.rounded_rectangle((x + 14, y + 14, x + 14 + w + 24, y + 14 + h + 18), radius=8, fill=(16, 16, 18, 190))
        d.text((x + 26, y + 20 - box[1] + 2), text, font=f, fill=(255, 255, 255, 255))
    # The quarters' edges.
    d.line((PW, 0, PW, H), fill=(16, 16, 18, 255), width=4)
    d.line((0, PH, W, PH), fill=(16, 16, 18, 255), width=4)
    img.save(path)


def main(argv=None):
    take, video, work, out, settings_path = (argv or sys.argv[1:])[:5]
    work = Path(work)
    q = json.loads((work / "quality.json").read_text())
    s = read_settings(settings_path)
    speed = float(s.get("speed", 1.0)) or 1.0
    who, move, number = parse_take(take)
    first_person = who == "Player" and bool(s.get("firstPerson", 1))
    current = q.get("currentClip") or "nothing yet"

    label_png = work / "labels.png"
    labels(label_png, [
        ((0, 0), f"Your video: {take}" + (f"  (x{speed:g})" if abs(speed - 1) > 1e-3 else "")),
        ((PW, 0), "Captured, on our soldier"),
        ((0, PH), f"Now: {current}"),
        ((PW, PH), "Captured, first-person view" if first_person else "Captured, from the front"),
    ])

    start, end = float(q["trimStart"]), float(q["trimEnd"])
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    graph = (f"[1:v]setpts=(PTS-STARTPTS)/{speed},fps=60,scale={PW}:{PH}:force_original_aspect_ratio=decrease,"
             f"pad={PW}:{PH}:(ow-iw)/2:(oh-ih)/2:color=0x141416[v];"
             f"[0:v][v]overlay=0:0:eof_action=pass[a];[a][2:v]overlay=0:0:shortest=1")
    cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error",
           "-i", str(work / "panels.mp4"),
           "-ss", f"{start:.3f}", "-t", f"{(end - start):.3f}", "-i", video,
           "-loop", "1", "-i", str(label_png),
           "-filter_complex", graph,
           "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-r", "60", "-threads", "4", "-an", out]
    subprocess.run(cmd, check=True)
    print(f"preview: {out}")


if __name__ == "__main__":
    main()
