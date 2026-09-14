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

set +e
"$UNITY" -batchmode "${GFX[@]}" -projectPath "$PROJECT" \
         -executeMethod "$METHOD" -logFile "$LOG" "$@"
CODE=$?
set -e

# Batchmode is quiet on success, so surface the lines that actually matter.
grep -aE "\[FPSKitBatch\]|\[FPSKit\]|error CS|FAILED|Aborting" "$LOG" | tail -30 || true

echo "unity-batch: exit $CODE"
exit $CODE
