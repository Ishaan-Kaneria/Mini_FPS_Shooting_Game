#!/usr/bin/env bash
#
# Captured animation: Ishaan's phone videos made into game clips (Docs/DESIGN_NOTES.md "Captured animation").
# One video at a time, on the efficient cores, paused when the CPU runs hot (cool-run.sh, unity-batch.sh).
# Unity must be closed for the steps that bake (Tools/unity-batch.sh refuses otherwise).
#
#   Tools/mocap/mocap.sh setup                       # once: Python env + MediaPipe models into Tools/mocap
#   Tools/mocap/mocap.sh calibrate calibration_01    # stand, arms out, turn: scale and bone lengths
#   Tools/mocap/mocap.sh process player_punch_01     # track, clean, bake the clip, render the preview
#   Tools/mocap/mocap.sh preview player_punch_01     # the preview again (after changing nothing but the picture)
#   Tools/mocap/mocap.sh test                        # the whole pipeline on a synthetic take, checked against the truth
#
# Videos are read from ~/MocapVideos (MOCAP_VIDEOS to change it), named who_move_NN.mp4, and never copied in.
# Re-tracking is skipped when the video has not changed (--retrack forces it). Settings: the clip's
# Assets/Animations/Captured/<Who>/<Move>/<take>_settings.asset; change a number there and run "process" again.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$(cd "$HERE/../.." && pwd)"
PY="$HERE/.venv/bin/python"
VIDEOS="${MOCAP_VIDEOS:-$HOME/MocapVideos}"
WORK="$HERE/work"
CALFILE="${MOCAP_CALIBRATION:-$WORK/calibration.json}"   # the calibration every take is scaled by
COOL="$HERE/cool-run.sh"
# Python runs on four of the efficient cores (12-15): eight still went over 95 C sixteen times tracking 950 frames.
export UNITY_CPUS_PY="${MOCAP_CPUS:-12-15}"
UNITY="$PROJECT/Tools/unity-batch.sh"

cmd="${1:-}"; shift || true
retrack=0
args=()
for a in "$@"; do [ "$a" = "--retrack" ] && retrack=1 || args+=("$a"); done
set -- "${args[@]+"${args[@]}"}"

need_env() { [ -x "$PY" ] && [ -f "$HERE/models/pose_landmarker_heavy.task" ] || { echo "run: Tools/mocap/mocap.sh setup" >&2; exit 1; }; }
ffmpeg_bin() { "$PY" -c 'import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())'; }

video_for() {  # take -> its video file
  local f
  for f in "$VIDEOS/$1".{mp4,MP4,mov,MOV,m4v}; do [ -f "$f" ] && { echo "$f"; return; }; done
  echo "no video for $1 in $VIDEOS (who_move_NN.mp4)" >&2; exit 1
}

track() {  # take -> work/<take>/landmarks.npz (skipped when the video is older)
  local take="$1" video; video="$(video_for "$take")"
  mkdir -p "$WORK/$take"
  if [ "$retrack" = 0 ] && [ -f "$WORK/$take/landmarks.npz" ] && [ "$WORK/$take/landmarks.npz" -nt "$video" ]; then
    echo "mocap: $take already tracked (--retrack to redo)"; return
  fi
  echo "mocap: tracking $video"
  (cd "$HERE" && UNITY_CPUS="$UNITY_CPUS_PY" "$COOL" "$PY" -m mocap.track "$video" "$WORK/$take/landmarks.npz")
}

settings_for() {  # take -> its settings asset (may not exist yet)
  "$PY" -c "import sys; sys.path.insert(0, '$HERE'); from mocap.common import parse_take; w, m, n = parse_take('$1'); print(f'$PROJECT/Assets/Animations/Captured/{w}/{m}/$1_settings.asset')"
}

compose() {  # take -> Build/Mocap/<take>_preview.mp4
  local take="$1"
  (cd "$HERE" && UNITY_CPUS="$UNITY_CPUS_PY" "$COOL" "$PY" -m mocap.compose "$take" "$(video_for "$take")" "$WORK/$take" "$PROJECT/Build/Mocap/${take}_preview.mp4" "$(settings_for "$take")")
}

case "$cmd" in
  setup)
    "$HERE/setup.sh" ;;

  calibrate)
    need_env
    take="${1:?calibrate <calibration_NN>}"
    track "$take"
    (cd "$HERE" && UNITY_CPUS="$UNITY_CPUS_PY" "$COOL" "$PY" -m mocap.solve "$WORK/$take/landmarks.npz" "$WORK/$take/calibration.json" --make-calibration)
    cp "$WORK/$take/calibration.json" "$CALFILE"
    echo "mocap: $take is now the calibration every take uses" ;;

  process)
    need_env
    take="${1:?process <who_move_NN>}"
    track "$take"
    cal=()
    [ -f "$CALFILE" ] && cal=(--calibration "$CALFILE") || echo "mocap: no calibration yet: scaling from the take's own standing height"
    st="$(settings_for "$take")"
    sarg=(); [ -f "$st" ] && sarg=(--settings "$st")
    (cd "$HERE" && UNITY_CPUS="$UNITY_CPUS_PY" "$COOL" "$PY" -m mocap.solve "$WORK/$take/landmarks.npz" "$WORK/$take/motion.json" "${sarg[@]+"${sarg[@]}"}" "${cal[@]+"${cal[@]}"}")
    UNITY_GRAPHICS=1 "$UNITY" FPSKit.EditorTools.FPSKitMocap.ProcessBatch -mocapTake "$take" -mocapFfmpeg "$(ffmpeg_bin)"
    compose "$take"
    cat "$WORK/$take/quality.json" ;;

  preview)
    need_env
    take="${1:?preview <who_move_NN>}"
    UNITY_GRAPHICS=1 "$UNITY" FPSKit.EditorTools.FPSKitMocap.PreviewBatch -mocapTake "$take" -mocapFfmpeg "$(ffmpeg_bin)"
    compose "$take" ;;

  test)
    # Our own soldier acting the player's punch, filmed as the guide says to film Ishaan, run through everything,
    # and the result held against the clip it was filmed from.
    need_env
    export MOCAP_VIDEOS="$WORK/synthetic" MOCAP_CALIBRATION="$WORK/synthetic/calibration.json"
    VIDEOS="$MOCAP_VIDEOS"
    UNITY_GRAPHICS=1 "$UNITY" FPSKit.EditorTools.FPSKitMocap.SyntheticBatch -mocapOut "$VIDEOS" -mocapFfmpeg "$(ffmpeg_bin)"
    "$0" calibrate calibration_00 --retrack
    "$0" process player_punch_00 --retrack
    "$UNITY" FPSKit.EditorTools.FPSKitMocap.CheckSyntheticBatch -mocapTake player_punch_00
    cat "$WORK/player_punch_00/check.txt"; echo ;;

  *)
    sed -n '2,20p' "$0"; exit 1 ;;
esac
