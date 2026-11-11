#!/usr/bin/env bash
# Renders the game off-screen (software OpenGL under Xvfb) and writes PNG frames you can open to SEE the result.
# Usage: tools/screenshot.sh <outdir> <frames> [game args after "--" as usual, without the "--"]
# Example: tools/screenshot.sh /tmp/shots 150 --host --name=Cam --autostart=1 --preset=debug --bot=idle
# 10 frames = 1 game second. Frames are <outdir>/f########.png (1280x720). Look at a late frame.
set -euo pipefail
OUT="$1"; FRAMES="$2"; shift 2
mkdir -p "$OUT"; rm -f "$OUT"/f*.png
cd "$(dirname "$0")/.."
timeout 300 xvfb-run -a -s "-screen 0 1280x720x24" godot --rendering-driver opengl3 --path . --resolution 1280x720 \
  --fixed-fps 10 --write-movie "$OUT/f.png" --quit-after "$FRAMES" -- "$@" 2>&1 | grep -E "ERROR|Exception|FAIL" || true
ls "$OUT" | tail -1
