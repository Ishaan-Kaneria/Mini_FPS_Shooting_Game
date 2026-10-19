#!/usr/bin/env bash
#
# Drives the Unity editor headlessly against this project.
#
#   Tools/unity-batch.sh                                  # compile check
#   Tools/unity-batch.sh FPSKitBatch.BuildAllThemes       # rebuild all scenes
#   Tools/unity-batch.sh FPSKitBatch.BuildTheme -fpskitTheme "Mars Colony"
#
# A bare "FPSKitBatch.X" is expanded to the full namespace, so the common case
# stays short. Unity takes an exclusive lock on the project, so this refuses to
# run while the editor is open rather than letting the two fight over Library/.
#
set -euo pipefail

UNITY="${UNITY_BIN:-$HOME/Unity/Hub/Editor/6000.6.0f1/Editor/Unity}"
PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG="${UNITY_LOG:-$PROJECT/Logs/unity-batch.log}"

METHOD="${1:-FPSKitBatch.CompileCheck}"
[ $# -gt 0 ] && shift
case "$METHOD" in
  FPSKitBatch.*) METHOD="FPSKit.EditorTools.$METHOD" ;;
esac

[ -x "$UNITY" ] || { echo "Unity not found at $UNITY (set UNITY_BIN)" >&2; exit 127; }

# Match on the project path, not on the editor binary. A Hub-launched editor runs as
# "unityhub-unity-editor-<version>", so checking for "$UNITY" misses it entirely and
# the guard silently passes -- Unity then aborts on its own with a lock error instead.
# Import workers carry the same -projectPath and are not a conflict, so they are excluded.
editor_holds_lock() {
  pgrep -af unity 2>/dev/null \
    | grep -iF -- "-projectpath $PROJECT" \
    | grep -viE 'AssetImportWorker|unity-batch' \
    | grep -q .
}

if editor_holds_lock; then
  echo "The Unity editor is already running and holds the project lock." >&2
  echo "Close it before running a batch job." >&2
  exit 1
fi

mkdir -p "$(dirname "$LOG")"

# -nographics gives a null graphics device, which is right for building scenes but
# blind to anything that only goes wrong while actually rendering. UNITY_GRAPHICS=1
# keeps a real device so the play-mode tests exercise shaders, canvases and meshes.
GFX=(-nographics)
if [ "${UNITY_GRAPHICS:-0}" = "1" ]; then
  GFX=()
  echo "unity-batch: real graphics device (UNITY_GRAPHICS=1)"
fi

echo "unity-batch: $METHOD  (log: $LOG)"

# Unity's working directory is the shell's, and every tool writes its output (Build/...) relative
# to it: run from a script folder, the limb test's pictures landed inside Assets/Scripts/Runtime.
cd "$PROJECT"

# Heat. This laptop has shut itself down at 100 C, and a player build or a scene rebuild took it there
# (2026-10-19). Three guards, each overridable:
#   UNITY_CPUS (default 8-15): the processor threads Unity and everything it starts (the shader compilers,
#     the import workers) may use, out of 18; "all" for no limit. 8-15 are the Core Ultra 5 125H's eight
#     efficient cores (0-7 are its four performance cores' threads, 4.5 GHz and the hottest): on 0-7 a
#     compile check still went over 95 C three times. Fewer or cooler threads, less heat, a longer job.
#   UNITY_START_BELOW (default 80): wait for the package to cool below this before starting.
#   UNITY_HOT / UNITY_COOL (default 95 / 80): freeze Unity and its children above UNITY_HOT, let them
#     go on again below UNITY_COOL. A frozen job loses nothing; it only takes longer.
cpu_temp() { sensors 2>/dev/null | grep 'Package id' | grep -o '+[0-9]*' | head -1 | tr -d + || true; }
CPUS="${UNITY_CPUS:-8-15}"
START_BELOW="${UNITY_START_BELOW:-80}"
HOT="${UNITY_HOT:-95}"
COOL="${UNITY_COOL:-80}"
if command -v sensors >/dev/null; then
  t="$(cpu_temp)"
  if [ -n "$t" ] && [ "$t" -ge "$START_BELOW" ]; then
    echo "unity-batch: waiting for the CPU to cool below $START_BELOW C (now $t C)"
    while [ "$(cpu_temp)" -ge "$START_BELOW" ]; do sleep 10; done
  fi
fi
PIN=()
JOBS=()
if [ "$CPUS" != "all" ] && command -v taskset >/dev/null; then
  PIN=(taskset -c "$CPUS")
  n=$(( $(echo "$CPUS" | tr ',' '\n' | awk -F- '{ s += ($2 == "" ? 1 : $2 - $1 + 1) } END { print s }') ))
  JOBS=(-job-worker-count "$n" -background-job-worker-count "$n")
  echo "unity-batch: on processor threads $CPUS ($n workers)"
fi

# Every process under Unity, Unity first.
tree() { local p; for p in "$@"; do echo "$p"; tree $(pgrep -P "$p" || true); done; }

set +e
"${PIN[@]}" "$UNITY" -batchmode "${GFX[@]}" -projectPath "$PROJECT" \
         -executeMethod "$METHOD" -logFile "$LOG" "${JOBS[@]}" "$@" &
UPID=$!
GUARD=""
if command -v sensors >/dev/null; then
  (
    paused=0; pauses=0
    while kill -0 "$UPID" 2>/dev/null; do
      t="$(cpu_temp)"
      if [ -n "$t" ]; then
        if [ "$paused" = 0 ] && [ "$t" -ge "$HOT" ]; then kill -STOP $(tree "$UPID") 2>/dev/null; paused=1; pauses=$((pauses + 1)); fi
        if [ "$paused" = 1 ] && [ "$t" -le "$COOL" ]; then kill -CONT $(tree "$UPID") 2>/dev/null; paused=0; fi
      fi
      sleep 3
    done
    [ "$pauses" -gt 0 ] && echo "unity-batch: paused $pauses time(s) for heat (over $HOT C until under $COOL C)"
  ) &
  GUARD=$!
fi
wait "$UPID"
CODE=$?
[ -n "$GUARD" ] && wait "$GUARD" 2>/dev/null
set -e

# Batchmode is quiet on success, so surface the lines that actually matter.
grep -aE "\[FPSKitBatch\]|\[FPSKit\]|error CS|FAILED|Aborting" "$LOG" | tail -30 || true

echo "unity-batch: exit $CODE"
exit $CODE
