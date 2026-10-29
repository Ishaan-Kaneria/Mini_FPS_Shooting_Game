#!/usr/bin/env bash
# One command for a play-test build of the main menu and Desert Outpost (Linux, Vulkan), then the project's settings the
# build touches put back.   usage: Tools/build-desert.sh [release|dev|both]   (default release)
#   release -> Build/Perf/smooth/FPSKitSmooth.x86_64        the real frame rate; the walk benchmark runs this one
#   dev     -> Build/Perf/desertdev/FPSKitDesertDev.x86_64  development build: Profiler, visible errors, slower
# Refuses on battery (a 30-minute build would drain it). Unity must be closed (Tools/unity-batch.sh checks).
set -u
cd "$(dirname "$0")/.."
KIND="${1:-release}"
[ "$(cat /sys/class/power_supply/ADP1/online 2>/dev/null)" = "1" ] || { echo "build-desert: on battery; plug in first" >&2; exit 3; }
build() {
  local method="$1"
  echo "build-desert: $method"
  # The build leaves URP's assets and the project settings changed (graphics APIs, frame timing stats, SSAO prefilter):
  # the files as they were before it are put back after.
  local keep; keep="$(mktemp -d)"
  cp -a ProjectSettings/ProjectSettings.asset ProjectSettings/QualitySettings.asset Assets/Settings "$keep/"
  UNITY_LOG="Logs/build-desert-$method.log" Tools/unity-batch.sh "FPSKitBatch.$method"; local code=$?
  cp -a "$keep/ProjectSettings.asset" "$keep/QualitySettings.asset" ProjectSettings/
  cp -a "$keep/Settings/." Assets/Settings/
  rm -rf "$keep"
  return $code
}
case "$KIND" in
  release) build BuildSmoothPlayer ;;
  dev) build BuildDesertDevPlayer ;;
  both) build BuildSmoothPlayer && build BuildDesertDevPlayer ;;
  *) echo "usage: $0 [release|dev|both]" >&2; exit 2 ;;
esac
