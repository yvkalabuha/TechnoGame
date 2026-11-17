#!/usr/bin/env bash
# Launch the Godot 3D globe spike (TASK-0067).
# Usage: ./run-globe-spike.sh          interactive client prototype v0.3 (TASK-0069/0070; real Session on a background thread)
#        ./run-globe-spike.sh --shots  automatic run + screenshots into docs/ui/prototypes/client-v03/ (RU and EN)
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

cd "$PROJECT"

# Import assets (models, fonts); cheap when nothing changed. .godot/ is git-ignored.
"$GODOT" --headless --path . --import >/dev/null 2>&1

echo "Building C# scripts..."
"$GODOT" --headless --path . --build-solutions --quit

if [[ "${1:-}" == "--shots" ]]; then
  "$GODOT" --path . -- --shots
elif [[ "${1:-}" == "--bench" ]]; then
  "$GODOT" --path . -- --bench --out bench-out
  echo "Benchmark output: $PROJECT/bench-out"
else
  echo "Controls: LMB drag = rotate, wheel = zoom, click a slice country = actor panel, Space pause, 1/2/3 tempo, T tech tree, F3 debug, F1 help; --lang en|ru"
  "$GODOT" --path .
fi
