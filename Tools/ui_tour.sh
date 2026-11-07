#!/bin/bash
# Screenshot every screen at common phone shapes (portrait).
# Build first (Editor closed):
#   Unity -batchmode -quit -projectPath . -buildTarget Linux64 -executeMethod Builder.BuildLinuxTour -logFile tmp/tour-build.log
# Usage: Tools/ui_tour.sh [output folder]
cd "$(dirname "$0")/.."
OUT="${1:-Builds/Tour/shots}"
PLAYER=Builds/Tour/tour.x86_64
# A desktop player saves under $HOME/.config/unity3d/, which other local desktop builds share.
# Give the tour its own empty HOME per run, so it starts from a fresh save and never touches theirs.
TOUR_HOME="$(mktemp -d)"
trap 'rm -rf "$TOUR_HOME"' EXIT
for shape in "19.5x9:480:1040" "16x9:540:960"; do
  IFS=: read -r name w h <<<"$shape"
  rm -rf "$TOUR_HOME/.config"   # fresh save: shows the first-time cards
  mkdir -p "$OUT/$name"
  echo "== $name (${w}x${h})"
  HOME="$TOUR_HOME" timeout 300 "$PLAYER" -screen-width "$w" -screen-height "$h" -screen-fullscreen 0 \
    -uitour "$PWD/$OUT/$name" -logFile "$PWD/$OUT/$name.log" || echo "   ($name ended with $?)"
done
ls "$OUT"/*/*.png | wc -l
