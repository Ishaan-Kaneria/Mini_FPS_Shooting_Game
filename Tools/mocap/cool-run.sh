#!/usr/bin/env bash
#
# Runs one command under the same heat rules as Tools/unity-batch.sh: on the efficient cores only, started
# once the CPU is below 80 C, frozen (SIGSTOP, with everything it started) above 95 C and let go again
# below 80 C. A frozen job loses nothing; it only takes longer.
#
#   Tools/mocap/cool-run.sh <command> [args...]
#
# Same overrides as unity-batch.sh: UNITY_CPUS (default 8-15, "all" for none), UNITY_START_BELOW,
# UNITY_HOT, UNITY_COOL.
set -uo pipefail
cpu_temp() { sensors 2>/dev/null | grep 'Package id' | grep -o '+[0-9]*' | head -1 | tr -d + || true; }
CPUS="${UNITY_CPUS:-8-15}"
START_BELOW="${UNITY_START_BELOW:-80}"
HOT="${UNITY_HOT:-95}"
COOL="${UNITY_COOL:-80}"

if command -v sensors >/dev/null; then
  t="$(cpu_temp)"
  if [ -n "$t" ] && [ "$t" -ge "$START_BELOW" ]; then
    echo "cool-run: waiting for the CPU to cool below $START_BELOW C (now $t C)" >&2
    while [ "$(cpu_temp)" -ge "$START_BELOW" ]; do sleep 10; done
  fi
fi

PIN=()
if [ "$CPUS" != "all" ] && command -v taskset >/dev/null; then PIN=(taskset -c "$CPUS"); fi
# MediaPipe's and numpy's thread pools: no more threads than the cores they may use.
n=$(( $(echo "$CPUS" | tr ',' '\n' | awk -F- '{ s += ($2 == "" ? 1 : $2 - $1 + 1) } END { print s }') ))
[ "$CPUS" = "all" ] && n=$(nproc)
export OMP_NUM_THREADS="$n" OPENBLAS_NUM_THREADS="$n" MKL_NUM_THREADS="$n"

tree() { local p; for p in "$@"; do echo "$p"; tree $(pgrep -P "$p" || true); done; }

"${PIN[@]}" "$@" &
PID=$!
if command -v sensors >/dev/null; then
  (
    paused=0; pauses=0
    while kill -0 "$PID" 2>/dev/null; do
      t="$(cpu_temp)"
      if [ -n "$t" ]; then
        if [ "$paused" = 0 ] && [ "$t" -ge "$HOT" ]; then kill -STOP $(tree "$PID") 2>/dev/null; paused=1; pauses=$((pauses + 1)); fi
        if [ "$paused" = 1 ] && [ "$t" -le "$COOL" ]; then kill -CONT $(tree "$PID") 2>/dev/null; paused=0; fi
      fi
      sleep 3
    done
    [ "$pauses" -gt 0 ] && echo "cool-run: paused $pauses time(s) for heat (over $HOT C until under $COOL C)" >&2
  ) &
  GUARD=$!
fi
wait "$PID"
CODE=$?
[ -n "${GUARD:-}" ] && wait "$GUARD" 2>/dev/null
exit $CODE
