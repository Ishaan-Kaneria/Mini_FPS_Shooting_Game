# Captured animation: licences of every tool

Everything the capture pipeline (`Tools/mocap`, `FPSKitMocap`) uses, and what its licence allows. The rule
for this pipeline: free and open source, usable in a commercial game, and **nothing built on the SMPL body
model or any other model or data whose licence forbids commercial use**.

Checked 2026-10-19 from each package's own metadata (`Tools/mocap/.venv`) and the publishers' pages. Update this
file whenever `Tools/mocap/requirements.txt`, the model files in `Tools/mocap/setup.sh` or the Unity packages change.

## What turns the video into motion

| Tool | Version | Licence | Commercial use | Notes |
|---|---|---|---|---|
| Google MediaPipe (`mediapipe`, Python) | 1.1.1 | Apache 2.0 | Yes | The Pose Landmarker and Hand Landmarker tasks. |
| Pose Landmarker model, heavy (`pose_landmarker_heavy.task`) | float16, "latest" on 2026-10-19 (sha256 `64437af8…`) | Apache 2.0 | Yes | BlazePose GHUM 3D: Google's own body model (GHUM), **not SMPL**. The most accurate of the three pose models. |
| Hand Landmarker model (`hand_landmarker.task`) | float16, "latest" on 2026-10-19 (sha256 `fbc2a300…`) | Apache 2.0 | Yes | 21 points per hand. |
| NumPy | 2.5.4 | BSD-3-Clause (with 0BSD, MIT, Zlib, CC0 parts) | Yes | |
| OpenCV (`opencv-contrib-python`, pulled in by MediaPipe) | 5.0.0.93 | Apache 2.0 | Yes | Not called directly. |
| Python | 3.12 (installed by uv into `Tools/mocap/.venv`) | PSF License | Yes | Not system-wide. |
| uv (installs the Python above) | already on the machine | MIT / Apache 2.0 | Yes | |

MediaPipe's own dependencies, installed with it and not called by our code: absl-py (Apache 2.0), flatbuffers
(Apache 2.0), matplotlib (PSF-style matplotlib licence), contourpy (BSD-3), cycler (BSD), kiwisolver (BSD),
fonttools (MIT), pyparsing (MIT), python-dateutil (BSD / Apache 2.0), six (MIT), packaging (Apache 2.0 / BSD-2),
sounddevice (MIT), cffi (MIT-0), pycparser (BSD-3), certifi (MPL 2.0). All allow commercial use.

## Video handling and the preview

| Tool | Version | Licence | Commercial use | Notes |
|---|---|---|---|---|
| imageio-ffmpeg | 0.6.0 | BSD-2-Clause | Yes | Ships a static ffmpeg, so nothing is installed system-wide. |
| ffmpeg (the static build inside imageio-ffmpeg) | 7.0.2 | GPL v3 (this build includes libx264) | Yes, as a tool | It runs on Ishaan's machine to read videos and write previews. Nothing of it goes into the game, and the clips it helps make are our own data, not covered by its licence. |
| Pillow | 13.0.0 | MIT-CMU (HPND) | Yes | Draws the preview's labels. |
| Unity Recorder (`com.unity.recorder`) | 5.1.7 | Unity Companion License | Yes, with Unity | Installed as asked. Note: it only records from a running editor (Play Mode with a Game view); in a closed-editor batch run it records nothing, so the pipeline's own previews are rendered by `FPSKitMocap.Preview` and encoded by the ffmpeg above. |
| Unity Timeline, OpenImageIO bindings (pulled in by the Recorder) | 1.8.7 / 1.0.2 | Unity Companion License | Yes, with Unity | |

## Turning the motion into game clips

All our own code: `Tools/mocap/mocap/*.py` (cleaning, scaling, ground, foot contacts), `FPSKitMocap` (retarget onto
the Survivalist Humanoid rig, foot locking, joint limits, first-person version). Retargeting uses Unity's own
Humanoid system (`HumanPoseHandler`), part of the engine. **Blender is not used:** Unity can write Humanoid clips
(`.anim`) directly, which is shorter and cleaner than going through FBX.

The rig the clips are baked on is the Survivalist pack's soldier already in the game (its own licence, unchanged).

## Not used, on purpose

- Anything SMPL-, SMPL-X- or SMPL-H-based (VIBE, HMR/HMR 2.0, 4DHumans, ROMP/BEV, WHAM, TRACE, PARE, CLIFF,
  FrankMocap, EasyMocap's SMPL path and the like): the SMPL family's licence forbids commercial use.
- Datasets licensed for research only (AMASS, Human3.6M, 3DPW, …): no model trained on them is used.
- Cloud capture services (DeepMotion, Rokoko Video, Move.ai, Plask): not free and open source, and the videos
  would leave the machine.
