#!/usr/bin/env bash
# The realism work's frame-time benchmark: the release build (FPSKitBatch.BuildSmoothPlayer: menu + Desert Outpost, Vulkan)
# jogs WalkRoute for two minutes at each tier and leaves Build/Perf/walk/<tag>_<tier>.txt (WalkProbe), with the CPU
# package temperature through the run appended.   usage: Tools/walk-bench.sh <tag> [Medium High]
set -u
cd "$(dirname "$0")/.."
TAG="${1:?tag (e.g. before, step1)}"; shift
TIERS=("${@:-Medium}"); [ $# -eq 0 ] && TIERS=(Medium High)
BIN="Build/Perf/smooth/FPSKitSmooth.x86_64"
[ -x "$BIN" ] || { echo "no build at $BIN: run Tools/unity-batch.sh FPSKitBatch.BuildSmoothPlayer"; exit 1; }
mkdir -p Build/Perf/walk
cpu_temp() { sensors 2>/dev/null | grep 'Package id' | grep -o '+[0-9]*' | head -1 | tr -d + || true; }
# On battery the chip runs at lower power limits: the step-1 walk ran a third slower on battery with an identical scene
# (2026-10-29). Said loudly, and written into every report.
power() { [ "$(cat /sys/class/power_supply/ADP1/online 2>/dev/null)" = "1" ] && echo "mains" || echo "BATTERY"; }
if [ "$(power)" = "BATTERY" ] && [ "${WALK_ALLOW_BATTERY:-0}" != "1" ]; then
  echo "walk-bench: refused: on battery (the numbers would not compare with a run on mains); plug in, or WALK_ALLOW_BATTERY=1" >&2
  exit 3
fi
EXTRA=(); [ "${WALK_ALLOW_BATTERY:-0}" = "1" ] && EXTRA=(-fpskitallowbattery)
for TIER in "${TIERS[@]}"; do
  OUT="Build/Perf/walk/${TAG}_${TIER}.txt"; rm -f "$OUT"
  # The same start for every run: below 70 C, so throttling does not decide the numbers.
  while [ "$(cpu_temp)" != "" ] && [ "$(cpu_temp)" -ge 70 ]; do sleep 10; done
  echo "walk-bench: $TAG $TIER (cpu $(cpu_temp) C)"
  "$BIN" -screen-width 1920 -screen-height 1080 -screen-fullscreen 1 -force-vulkan -logFile "$PWD/Build/Perf/walk/${TAG}_${TIER}.log" \
         -fpskitwalk "$PWD/$OUT" -fpskitquality "$TIER" "${EXTRA[@]}" &
  PID=$!; MAX=0
  for i in $(seq 1 300); do
    [ -f "$OUT" ] && break; kill -0 $PID 2>/dev/null || break
    t="$(cpu_temp)"; [ -n "$t" ] && [ "$t" -gt "$MAX" ] && MAX=$t
    sleep 2
  done
  kill $PID 2>/dev/null || true
  if [ -f "$OUT" ]; then echo "cpu package max ${MAX} C during the run, on $(power)" >> "$OUT"; cat "$OUT"; else echo "no report; see $PWD/Build/Perf/walk/${TAG}_${TIER}.log"; fi
done
