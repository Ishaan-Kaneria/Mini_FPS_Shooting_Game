#!/usr/bin/env bash
# Waits for the dev build (Tools > MiniFPS > Performance > Build Linux Dev Player (after)) to finish, then runs the
# Vulkan measurements unattended and writes one summary. Start it BEFORE going to bed, after starting the build:
#   Tools/overnight-perf.sh            (leave the laptop open, plugged in, on a hard surface)
# Results: Build/Perf/after_vulkan_views.txt, after_vulkan_experiments.txt, shots in Build/Perf/, summary Build/Perf/overnight_summary.txt
set -u
cd "$(dirname "$0")/.."
STATUS=Build/Perf/status_build_after.txt
echo "waiting for the build ($STATUS)"
until grep -q "^done\|^failed" "$STATUS" 2>/dev/null; do sleep 30; done
if ! grep -q "^done" "$STATUS"; then echo "build failed: $(cat $STATUS)"; exit 1; fi
SUM=Build/Perf/overnight_summary.txt
{ echo "build: $(cat $STATUS)"; echo "started $(date)"; echo "cpu temp before: $(( $(cat /sys/class/thermal/thermal_zone0/temp) / 1000 )) C"; } > "$SUM"
# let the machine cool for 5 minutes after the build before measuring: the shader compile heats it
sleep 300
Tools/run-perf-build.sh after vulkan views       >> "$SUM" 2>&1
echo "cpu temp after views: $(( $(cat /sys/class/thermal/thermal_zone0/temp) / 1000 )) C" >> "$SUM"
Tools/run-perf-build.sh after vulkan experiments >> "$SUM" 2>&1
echo "cpu temp after experiments: $(( $(cat /sys/class/thermal/thermal_zone0/temp) / 1000 )) C" >> "$SUM"
echo "finished $(date)" >> "$SUM"
