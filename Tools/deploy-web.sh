#!/usr/bin/env bash
#
# Rebuilds the WebGL player and publishes it to Netlify.
#
#   Tools/deploy-web.sh              # rebuild, then deploy
#   Tools/deploy-web.sh --no-build   # deploy whatever is already in Build/WebGL
#   Tools/deploy-web.sh --dry-run    # build and check, stop before uploading
#
# Netlify's CLI is an npm package and there is no Node on this machine, so this
# talks to the deploy API directly. curl and zip are all it needs.
#
# Two secrets, both from netlify.com, neither ever written to the repo:
#
#   NETLIFY_AUTH_TOKEN   User settings > Applications > Personal access tokens
#   NETLIFY_SITE_ID      Site configuration > General > Site information > Site ID
#
# Put them in ~/.netlify-env and source it, or export them per shell:
#
#   export NETLIFY_AUTH_TOKEN=nfp_...
#   export NETLIFY_SITE_ID=1234abcd-...
#
set -euo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT="$PROJECT/Build/WebGL"

BUILD=1
UPLOAD=1
for arg in "$@"; do
  case "$arg" in
    --no-build) BUILD=0 ;;
    --dry-run)  UPLOAD=0 ;;
    -h|--help)  awk 'NR>1 && /^#/ {sub(/^# ?/, ""); print; next} NR>1 {exit}' \
                  "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "deploy-web: unknown argument '$arg'" >&2; exit 2 ;;
  esac
done

# ---------------------------------------------------------------- build ----
# The freshness check below compares against this, so a build that fails and
# leaves the previous one on disk cannot be mistaken for a new one. Deploying a
# stale folder is the one failure that looks exactly like success.
STARTED_AT=$(date +%s)

if [ "$BUILD" = "1" ]; then
  echo "deploy-web: building (this takes around 15 minutes)"
  UNITY_LOG="$PROJECT/Logs/webgl-deploy.log" \
    "$PROJECT/Tools/unity-batch.sh" FPSKitBatch.BuildWebGL \
      -buildTarget WebGL -fpskitFallback false
else
  echo "deploy-web: --no-build, using what is already in Build/WebGL"
fi

# ---------------------------------------------------------------- verify ---
# A build can exit 0 having written nothing useful, and a half-written folder
# uploads just as happily as a whole one.
for required in index.html _headers Build; do
  [ -e "$OUTPUT/$required" ] || {
    echo "deploy-web: $OUTPUT/$required is missing -- refusing to deploy" >&2
    exit 1
  }
done

ls "$OUTPUT"/Build/*.br >/dev/null 2>&1 || {
  echo "deploy-web: no .br payload in $OUTPUT/Build -- refusing to deploy" >&2
  exit 1
}

if [ "$BUILD" = "1" ]; then
  BUILT_AT=$(stat -c %Y "$OUTPUT/index.html")
  [ "$BUILT_AT" -ge "$STARTED_AT" ] || {
    echo "deploy-web: index.html predates this run -- the build did not rewrite it." >&2
    echo "deploy-web: refusing to publish a stale folder as if it were new." >&2
    exit 1
  }
else
  AGE=$(( ( $(date +%s) - $(stat -c %Y "$OUTPUT/index.html") ) / 60 ))
  echo "deploy-web: that build is ${AGE} minute(s) old"
fi

echo "deploy-web: $(du -sh "$OUTPUT" | cut -f1) in $(find "$OUTPUT" -type f | wc -l) files"

[ "$UPLOAD" = "1" ] || { echo "deploy-web: --dry-run, stopping before upload"; exit 0; }

# ---------------------------------------------------------------- upload ---
: "${NETLIFY_AUTH_TOKEN:?not set -- see the header of this script}"
: "${NETLIFY_SITE_ID:?not set -- see the header of this script}"

STAGING="$(mktemp -d)"
trap 'rm -rf "$STAGING"' EXIT
ARCHIVE="$STAGING/site.zip"

# Zipped from inside the folder so index.html sits at the root of the archive.
# One level out and Netlify serves a directory listing instead of the game.
( cd "$OUTPUT" && zip -qr "$ARCHIVE" . )

echo "deploy-web: uploading $(du -h "$ARCHIVE" | cut -f1) to site $NETLIFY_SITE_ID"

BODY="$STAGING/response.json"
STATUS=$(curl -sS -o "$BODY" -w '%{http_code}' -X POST \
  -H "Authorization: Bearer $NETLIFY_AUTH_TOKEN" \
  -H "Content-Type: application/zip" \
  --data-binary "@$ARCHIVE" \
  "https://api.netlify.com/api/v1/sites/$NETLIFY_SITE_ID/deploys")

if [ "$STATUS" != "200" ] && [ "$STATUS" != "201" ]; then
  echo "deploy-web: Netlify returned HTTP $STATUS" >&2
  head -c 600 "$BODY" >&2; echo >&2
  exit 1
fi

python3 - "$BODY" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
print(f"deploy-web: deploy {d.get('id', '?')} is {d.get('state', '?')}")
print(f"deploy-web: live at {d.get('ssl_url') or d.get('url') or '(url not reported)'}")
PY

echo "deploy-web: done. Netlify finishes processing in a few seconds."
