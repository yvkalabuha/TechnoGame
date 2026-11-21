#!/usr/bin/env bash
# Launch the Godot 3D globe spike (TASK-0067).
# Usage: ./run-globe-spike.sh          interactive client prototype v0.4 (TASK-0069..0071; real Session on a background thread)
#        ./run-globe-spike.sh --window  a framed 1920x1080 window instead of fullscreen (tests only; other flags such as --lang en pass through)
#        ./run-globe-spike.sh --shots  automatic run + screenshots and an audio demo WAV into shots-out/ (git-ignored; copy key frames into docs/ui by hand)
#        ./run-globe-spike.sh --bench  automatic benchmark + screenshots into bench-out/
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/Singularity.Client.Spike"
GODOT="${GODOT:-$HOME/.local/opt/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64}"

if [[ ! -x "$GODOT" ]]; then
  echo "Godot 4.7.2 mono not found at: $GODOT" >&2
  echo "Install it per $PROJECT/README.md, or set GODOT=/path/to/godot" >&2
  exit 1
fi

# Godot's C# build needs the .NET SDK on PATH.
export PATH="$HOME/.dotnet:$PATH"

# The area icons and pictures live only in docs/ui/assets/ (single source); the client reads a build-time copy (git-ignored).
rm -rf "$PROJECT/assets/ui"
mkdir -p "$PROJECT/assets/ui"
cp -r "$ROOT/docs/ui/assets/area-icons" "$ROOT/docs/ui/assets/area-pictures" "$PROJECT/assets/ui/"
rm -f "$PROJECT"/assets/ui/*/README.md

cd "$PROJECT"

# Import assets (models, fonts, audio); cheap when nothing changed. .godot/ is git-ignored. Import errors are shown, not hidden.
IMPORT_LOG="$(mktemp)"
trap 'rm -f "$IMPORT_LOG"' EXIT
if ! "$GODOT" --headless --path . --import >"$IMPORT_LOG" 2>&1 || grep -qE "^(ERROR|SCRIPT ERROR)" "$IMPORT_LOG"; then
  echo "Asset import reported errors:" >&2
  grep -E "ERROR" "$IMPORT_LOG" >&2 || tail -20 "$IMPORT_LOG" >&2
fi

echo "Building C# scripts..."
"$GODOT" --headless --path . --build-solutions --quit

if [[ "${1:-}" == "--shots" ]]; then
  "$GODOT" --path . -- "$@"
elif [[ "${1:-}" == "--bench" ]]; then
  "$GODOT" --path . -- --bench --out bench-out
  echo "Benchmark output: $PROJECT/bench-out"
else
  echo "Controls: LMB drag = rotate, wheel = zoom, click a slice country = actor panel, Space pause, 1/2/3 tempo, T E F M I G J areas (Esc map), F3 debug, F1 help; Esc = game menu (Quit game); --lang en|ru"
  "$GODOT" --path . -- "$@"
fi
