#!/usr/bin/env bash
# Heat and power for the long jobs (sourced by unity-batch.sh, walk-bench.sh, cost-bench.sh). The laptop has switched
# itself off with no shutdown in its log (2026-10-03, 2026-10-30): the firmware's thermal protection, by the look of it.
# The CPU package reached 100-107 C in benchmarks, and two board sensors (thermal zones SEN2, SEN3) have a critical
# limit of 80 C. So:
#   heat_hot / heat_cool   a job pauses at 95 C on the package or 75 C on a board sensor, and goes on below 80 / 68.
#                          HEAT_HOT, HEAT_COOL, HEAT_SEN_HOT, HEAT_SEN_COOL override.
#   heat_log_start/stop    every 10 s the package, the board sensors and the load go to Build/Perf/heat.log, so the
#                          next switch-off shows what was hot (the last line before it).
#   power_balanced/restore the power profile is "balanced" while a job runs (cooler, slower) and goes back after.
#                          HEAT_POWER=keep leaves it alone.
HEAT_HOT="${HEAT_HOT:-95}"; HEAT_COOL="${HEAT_COOL:-80}"
HEAT_SEN_HOT="${HEAT_SEN_HOT:-75}"; HEAT_SEN_COOL="${HEAT_SEN_COOL:-68}"
HEAT_LOG="${HEAT_LOG:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/Build/Perf/heat.log}"

heat_pkg() { sensors 2>/dev/null | grep 'Package id' | grep -o '+[0-9]*' | head -1 | tr -d + || true; }

# The hottest of the board sensors (thermal zones named SEN*), in whole degrees.
heat_sen() {
  local max=0 z t
  for z in /sys/class/thermal/thermal_zone*; do
    case "$(cat "$z/type" 2>/dev/null)" in SEN*) t=$(( $(cat "$z/temp" 2>/dev/null || echo 0) / 1000 )); [ "$t" -gt "$max" ] && max=$t ;; esac
  done
  echo "$max"
}

heat_hot()  { local p s; p="$(heat_pkg)"; s="$(heat_sen)"; { [ -n "$p" ] && [ "$p" -ge "$HEAT_HOT" ]; } || [ "$s" -ge "$HEAT_SEN_HOT" ]; }
heat_cool() { local p s; p="$(heat_pkg)"; s="$(heat_sen)"; { [ -z "$p" ] || [ "$p" -le "$HEAT_COOL" ]; } && [ "$s" -le "$HEAT_SEN_COOL" ]; }
heat_now()  { echo "package $(heat_pkg) C, board $(heat_sen) C"; }

heat_log_start() {
  mkdir -p "$(dirname "$HEAT_LOG")"
  echo "# $(date '+%F %T') start: ${1:-job}" >> "$HEAT_LOG"
  (
    while true; do
      local line z
      line="$(date '+%T') pkg $(heat_pkg)"
      for z in /sys/class/thermal/thermal_zone*; do
        case "$(cat "$z/type" 2>/dev/null)" in SEN*|acpitz|TCPU) line="$line $(cat "$z/type")=$(( $(cat "$z/temp" 2>/dev/null || echo 0) / 1000 ))" ;; esac
      done
      echo "$line load $(cut -d' ' -f1 /proc/loadavg) power $(cat /sys/class/power_supply/ADP1/online 2>/dev/null)" >> "$HEAT_LOG"
      sync -f "$HEAT_LOG" 2>/dev/null || true    # on disk at once: a switch-off loses what is only in the cache
      sleep 10
    done
  ) &
  HEAT_LOG_PID=$!
}
heat_log_stop() { [ -n "${HEAT_LOG_PID:-}" ] && kill "$HEAT_LOG_PID" 2>/dev/null; echo "# $(date '+%F %T') end" >> "$HEAT_LOG"; HEAT_LOG_PID=""; }

power_balanced() {
  [ "${HEAT_POWER:-}" = "keep" ] && return 0
  command -v powerprofilesctl >/dev/null || return 0
  HEAT_POWER_WAS="$(powerprofilesctl get 2>/dev/null)"
  [ -n "$HEAT_POWER_WAS" ] && [ "$HEAT_POWER_WAS" != "balanced" ] && powerprofilesctl set balanced 2>/dev/null && echo "heat: power profile balanced (was $HEAT_POWER_WAS)"
  return 0
}
power_restore() {
  [ -n "${HEAT_POWER_WAS:-}" ] && [ "$HEAT_POWER_WAS" != "balanced" ] && powerprofilesctl set "$HEAT_POWER_WAS" 2>/dev/null && echo "heat: power profile back to $HEAT_POWER_WAS"
  HEAT_POWER_WAS=""
}
