#!/usr/bin/env bash
# Where Desert Outpost's frame time goes (FrameCostProbe): fixed views along the walk, each drawn whole and with one thing
# taken away at a time, plus what is drawn and, in the development build, every render pass's GPU time.
#   usage: Tools/cost-bench.sh <tag> [release|dev] [Medium|High ...]   -> Build/Perf/cost/<tag>_<build>_<tier>.txt
set -u
cd "$(dirname "$0")/.."
TAG="${1:?tag}"; KIND="${2:-release}"; shift $(( $# >= 2 ? 2 : 1 ))
TIERS=("${@:-Medium}"); [ $# -eq 0 ] && TIERS=(Medium)
[ "$KIND" = dev ] && BIN="Build/Perf/desertdev/FPSKitDesertDev.x86_64" || BIN="Build/Perf/smooth/FPSKitSmooth.x86_64"
[ -x "$BIN" ] || { echo "no build at $BIN: Tools/build-desert.sh $KIND"; exit 1; }
[ "$(cat /sys/class/power_supply/ADP1/online 2>/dev/null)" = "1" ] || { echo "cost-bench: refused: on battery" >&2; exit 3; }
cpu_temp() { sensors 2>/dev/null | grep 'Package id' | grep -o '+[0-9]*' | head -1 | tr -d + || true; }
mkdir -p Build/Perf/cost
for TIER in "${TIERS[@]}"; do
  OUT="Build/Perf/cost/${TAG}_${KIND}_${TIER}.txt"; rm -f "$OUT"
  while [ "$(cpu_temp)" != "" ] && [ "$(cpu_temp)" -ge 70 ]; do sleep 10; done
  echo "cost-bench: $TAG $KIND $TIER (cpu $(cpu_temp) C)"
  "$BIN" -screen-width 1920 -screen-height 1080 -screen-fullscreen 1 -force-vulkan -logFile "Build/Perf/cost/${TAG}_${KIND}_${TIER}.log" \
         -fpskitcost "$OUT" -fpskitquality "$TIER" &
  PID=$!; MAX=0
  for i in $(seq 1 600); do
    [ -f "$OUT" ] && break; kill -0 $PID 2>/dev/null || break
    t="$(cpu_temp)"; [ -n "$t" ] && [ "$t" -gt "$MAX" ] && MAX=$t
    sleep 2
  done
  sleep 2; kill $PID 2>/dev/null || true
  if [ -f "$OUT" ]; then echo "cpu package max ${MAX} C during the run, on mains" >> "$OUT"; head -40 "$OUT"; else echo "no report; see Build/Perf/cost/${TAG}_${KIND}_${TIER}.log"; fi
done
