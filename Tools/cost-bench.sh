#!/usr/bin/env bash
# Where Desert Outpost's frame time goes (FrameCostProbe): fixed views along the walk, each drawn whole and with one thing
# taken away at a time, plus what is drawn and, in the development build, every render pass's GPU time.
#   usage: Tools/cost-bench.sh <tag> [release|dev] [Medium|High ...]   -> Build/Perf/cost/<tag>_<build>_<tier>.txt
set -u
cd "$(dirname "$0")/.."
. Tools/heat.sh
power_balanced
heat_log_start "cost-bench $*"
trap 'heat_log_stop; power_restore' EXIT
TAG="${1:?tag}"; KIND="${2:-release}"; shift $(( $# >= 2 ? 2 : 1 ))
TIERS=("${@:-Medium}"); [ $# -eq 0 ] && TIERS=(Medium)
[ "$KIND" = dev ] && BIN="Build/Perf/desertdev/FPSKitDesertDev.x86_64" || BIN="Build/Perf/smooth/FPSKitSmooth.x86_64"
[ -x "$BIN" ] || { echo "no build at $BIN: Tools/build-desert.sh $KIND"; exit 1; }
EXTRA=()
if [ "$(cat /sys/class/power_supply/ADP1/online 2>/dev/null)" != "1" ]; then
  [ "${ALLOW_BATTERY:-0}" = "1" ] || { echo "cost-bench: refused: on battery (or ALLOW_BATTERY=1)" >&2; exit 3; }
  EXTRA=(-fpskitallowbattery); echo "cost-bench: on battery, as asked; the report says so" >&2
fi
cpu_temp() { sensors 2>/dev/null | grep 'Package id' | grep -o '+[0-9]*' | head -1 | tr -d + || true; }
mkdir -p Build/Perf/cost
for TIER in "${TIERS[@]}"; do
  OUT="Build/Perf/cost/${TAG}_${KIND}_${TIER}.txt"
  for attempt in 1 2; do
    rm -f "$OUT"
    until { [ "$(cpu_temp)" = "" ] || [ "$(cpu_temp)" -lt 70 ]; } && heat_cool; do sleep 10; done
    echo "cost-bench: $TAG $KIND $TIER (cpu $(cpu_temp) C, board $(heat_sen) C)"
    "$BIN" -screen-width 1920 -screen-height 1080 -screen-fullscreen 1 -force-vulkan -logFile "$PWD/Build/Perf/cost/${TAG}_${KIND}_${TIER}.log" \
           -fpskitcost "$PWD/$OUT" -fpskitquality "$TIER" "${EXTRA[@]}" &
    PID=$!; MAX=0; SEN=0; HOT=0; HOTFOR=0
    for i in $(seq 1 600); do
      [ -f "$OUT" ] && break; kill -0 $PID 2>/dev/null || break
      t="$(cpu_temp)"; [ -n "$t" ] && [ "$t" -gt "$MAX" ] && MAX=$t
      b="$(heat_sen)"; [ "$b" -gt "$SEN" ] && SEN=$b
      # A board sensor at its limit freezes it until they cool (the board reached 75 C on its fixed views, 2026-10-31, and
      # every run stopped): each of its numbers is a median of 20 frames, so the one long frame after a pause is left out,
      # where the walk's averages would count it (walk-bench stops instead). HEAT_BENCH_PKG=1: the package stops it too.
      if heat_sen_hot; then
        kill -STOP $PID 2>/dev/null; echo "cost-bench: paused for heat ($(heat_now))"
        until [ "$(heat_sen)" -le "$HEAT_SEN_COOL" ]; do sleep 5; done
        kill -CONT $PID 2>/dev/null
      fi
      if [ "${HEAT_BENCH_PKG:-0}" = 1 ] && heat_pkg_hot; then HOTFOR=$((HOTFOR + 1)); else HOTFOR=0; fi
      if [ "$HOTFOR" -ge 3 ]; then HOT=1; break; fi
      sleep 2
    done
    sleep 2; kill $PID 2>/dev/null || true
    if [ "$HOT" = 1 ]; then rm -f "$OUT"; echo "cost-bench: stopped for heat ($(heat_now)); cooling, then once more"; continue; fi
    break
  done
  if [ -f "$OUT" ]; then echo "cpu package max ${MAX} C, board sensors max ${SEN} C during the run, on $([ "$(cat /sys/class/power_supply/ADP1/online 2>/dev/null)" = "1" ] && echo mains || echo BATTERY)" >> "$OUT"; head -40 "$OUT"; else echo "no report; see Build/Perf/cost/${TAG}_${KIND}_${TIER}.log"; fi
done
