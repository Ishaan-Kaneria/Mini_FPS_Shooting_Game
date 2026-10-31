#!/usr/bin/env bash
# Runs one heavy job and watches the memory while it runs (2026-10-31: a light bake was stopped part way by the session's
# own low-memory guard). Every 3 s: available memory, swap in use, and the job's own memory (every process under it,
# Unity's light baker included) go to Build/Perf/mem.log; the peaks are printed at the end. When available memory falls
# under MEM_STOP_MB (default 1800, above earlyoom's 10 % of 14.5 GB) the job is stopped cleanly (TERM, then KILL after
# 30 s) and the script says so and exits 9, before earlyoom or the session's guard has to.
#   usage: Tools/mem-guard.sh <label> <command...>
set -u
cd "$(dirname "$0")/.."
LABEL="${1:?label}"; shift
STOP_MB="${MEM_STOP_MB:-1800}"
LOG="Build/Perf/mem.log"
mkdir -p Build/Perf
avail_mb() { awk '/MemAvailable/ {print int($2/1024)}' /proc/meminfo; }
swap_mb()  { awk '/SwapTotal/ {t=$2} /SwapFree/ {f=$2} END {print int((t-f)/1024)}' /proc/meminfo; }
tree()     { local p=$1; echo "$p"; for c in $(pgrep -P "$p" 2>/dev/null); do tree "$c"; done; }
job_mb()   { local s=0; for p in $(tree "$1"); do r=$(awk '/VmRSS/ {print $2}' /proc/$p/status 2>/dev/null); s=$((s + ${r:-0})); done; echo $((s / 1024)); }

"$@" &
PID=$!
SWAP0=$(swap_mb); MIN_AVAIL=999999; MAX_SWAP=0; MAX_JOB=0; STOPPED=0
echo "# $(date '+%F %T') start: $LABEL (stop under ${STOP_MB} MB available; swap in use at start ${SWAP0} MB)" >> "$LOG"
while kill -0 $PID 2>/dev/null; do
  a=$(avail_mb); s=$(swap_mb); j=$(job_mb $PID)
  [ "$a" -lt "$MIN_AVAIL" ] && MIN_AVAIL=$a
  [ "$s" -gt "$MAX_SWAP" ] && MAX_SWAP=$s
  [ "$j" -gt "$MAX_JOB" ] && MAX_JOB=$j
  echo "$(date '+%T') avail ${a} MB swap ${s} MB job ${j} MB" >> "$LOG"
  if [ "$a" -lt "$STOP_MB" ]; then
    echo "mem-guard: available memory ${a} MB is under ${STOP_MB} MB: stopping $LABEL" | tee -a "$LOG"
    kill -TERM $(tree $PID) 2>/dev/null
    for i in $(seq 1 10); do kill -0 $PID 2>/dev/null || break; sleep 3; done
    kill -KILL $(tree $PID) 2>/dev/null
    STOPPED=1; break
  fi
  sleep 3
done
wait $PID 2>/dev/null; CODE=$?
SUMMARY="mem-guard: $LABEL $([ $STOPPED = 1 ] && echo STOPPED || echo "exit $CODE"); job peak ${MAX_JOB} MB, least available ${MIN_AVAIL} MB, swap peak ${MAX_SWAP} MB (${SWAP0} at start)"
echo "$SUMMARY" | tee -a "$LOG"
[ $STOPPED = 1 ] && exit 9
exit $CODE
