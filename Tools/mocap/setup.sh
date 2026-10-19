#!/usr/bin/env bash
#
# Builds the capture pipeline's Python side in Tools/mocap/.venv (never system-wide) and fetches
# MediaPipe's two model files into Tools/mocap/models. Safe to run again: it only adds what is missing.
#
#   Tools/mocap/setup.sh
#
# Needs uv (https://github.com/astral-sh/uv) for a private Python 3.12; ffmpeg comes as a pip package
# (imageio-ffmpeg's static build), so nothing is installed outside this folder.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$HERE"

if [ ! -x .venv/bin/python ]; then
  uv venv --python 3.12 .venv
fi
uv pip install --python .venv/bin/python -r requirements.txt

mkdir -p models
fetch() {  # url file sha256
  if [ -f "models/$2" ] && echo "$3  models/$2" | sha256sum -c --quiet 2>/dev/null; then return; fi
  curl -sSfL "$1" -o "models/$2"
  echo "$3  models/$2" | sha256sum -c --quiet || { echo "checksum mismatch for $2" >&2; rm -f "models/$2"; exit 1; }
}
# The most accurate pose model (heavy) and the hand model, both Apache 2.0 (Docs/MOCAP_LICENSES.md).
fetch https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_heavy/float16/latest/pose_landmarker_heavy.task \
      pose_landmarker_heavy.task 64437af838a65d18e5ba7a0d39b465540069bc8aae8308de3e318aad31fcbc7b
fetch https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task \
      hand_landmarker.task fbc2a30080c3c557093b5ddfc334698132eb341044ccee322ccf8bcf3607cde1

echo "mocap: ready ($(.venv/bin/python -c 'import mediapipe; print("mediapipe", mediapipe.__version__)'))"
