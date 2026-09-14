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

if pgrep -f "$UNITY" >/dev/null 2>&1; then
  echo "The Unity editor is already running and holds the project lock." >&2
  echo "Close it before running a batch job." >&2
  exit 1
fi

mkdir -p "$(dirname "$LOG")"
echo "unity-batch: $METHOD  (log: $LOG)"

set +e
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
         -executeMethod "$METHOD" -logFile "$LOG" "$@"
CODE=$?
set -e

# Batchmode is quiet on success, so surface the lines that actually matter.
grep -aE "\[FPSKitBatch\]|\[FPSKit\]|error CS|FAILED|Aborting" "$LOG" | tail -30 || true

echo "unity-batch: exit $CODE"
exit $CODE
