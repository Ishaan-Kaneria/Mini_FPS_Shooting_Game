#!/usr/bin/env bash
# Runs a development build made by Tools > MiniFPS > Performance > Build Linux Dev Player under one graphics API at 1080p
# and leaves the report in Build/Perf/<tag>_<api>.txt.   usage: Tools/run-perf-build.sh before|after vulkan|glcore [views|experiments]
set -e
cd "$(dirname "$0")/.."
TAG="${1:?tag (before|after)}"; API="${2:?api (vulkan|glcore)}"; MODE="${3:-views}"
OUT="Build/Perf/${TAG}_${API}_${MODE}.txt"; rm -f "$OUT"
EXTRA=""; [ "$MODE" = experiments ] && EXTRA="-fpskitexperiments"
"Build/Perf/${TAG}/FPSKitPerf.x86_64" -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -force-"$API" -logFile "Build/Perf/${TAG}_${API}_${MODE}.log" $EXTRA -fpskitperf "$OUT" &
PID=$!
for i in $(seq 1 200); do [ -f "$OUT" ] && break; kill -0 $PID 2>/dev/null || break; sleep 2; done
kill $PID 2>/dev/null || true
cat "$OUT" 2>/dev/null || { echo "no report; see Build/Perf/${TAG}_${API}_${MODE}.log"; exit 1; }
