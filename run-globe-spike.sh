#!/usr/bin/env bash
# Launch the Godot 3D globe spike (TASK-0067).
# Usage: ./run-globe-spike.sh          interactive client prototype v0.4 (TASK-0069..0071; real Session on a background thread)
#        ./run-globe-spike.sh --window  a framed 1920x1080 window instead of fullscreen (tests only; other flags such as --lang en pass through)
#        ./run-globe-spike.sh --shots  automatic run + screenshots into shots-out/ (git-ignored), silent (audio driver Dummy)
#        ./run-globe-spike.sh --shots --only area_science_1920x1080,tree_*   only the named frames (comma list, * and ? masks)
#        ./run-globe-spike.sh --shots --time-shots   time-control frames, silent; --only works here too
#        ./run-globe-spike.sh --shots --audio-demo   also records the audio demo WAV (real audio driver, sound is heard)
#        C# is rebuilt only when a .cs/.csproj file is newer than the last build stamp (FORCE_BUILD=1 to force)
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

STAMP=".godot/build-stamp"
# The stamp holds the list of source files: a deleted or renamed file changes it and forces a build.
# Every input file under src/ counts (code, project files, embedded data such as map packs), except build and shot output.
SRC_LIST="$( { find "$ROOT/src" -type f -not -path '*/.godot/*' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/shots-out/*' -not -path '*/bench-out/*' -not -path '*/assets/ui/*' -print; find "$ROOT" -maxdepth 1 \( -name 'Directory.Build.*' -o -name 'global.json' -o -name '*.props' \) -print; } | LC_ALL=C sort | sha256sum | cut -d' ' -f1)"
if [[ "${FORCE_BUILD:-0}" == "1" || ! -f "$STAMP" || "$(cat "$STAMP" 2>/dev/null)" != "$SRC_LIST" ]] \
   || [[ -n "$(find "$ROOT/src" -type f -newer "$STAMP" -not -path '*/.godot/*' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/shots-out/*' -not -path '*/bench-out/*' -not -path '*/assets/ui/*' -print -quit)" ]] \
   || [[ -n "$(find "$ROOT" -maxdepth 1 \( -name 'Directory.Build.*' -o -name 'global.json' -o -name '*.props' \) -newer "$STAMP" -print -quit)" ]]; then
  echo "Building C# scripts..."
  echo "$SRC_LIST" > "$STAMP.new"   # taken before the build: a file saved during the build stays newer than the stamp
  "$GODOT" --headless --path . --build-solutions --quit   # set -e: a failed build stops here and leaves the old stamp
  mv -f "$STAMP.new" "$STAMP"
else
  echo "C# unchanged since the last build: skipping the build (FORCE_BUILD=1 to force)"
fi

has_arg() { local w="$1" a; shift; for a in "$@"; do [[ "$a" == "$w" ]] && return 0; done; return 1; }
AUDIO=(--audio-driver Dummy)
if has_arg --audio-demo "$@"; then AUDIO=(); fi

if has_arg --shots "$@" || has_arg --map-frames "$@"; then   # --time-shots is a sub-mode of --shots
  "$GODOT" "${AUDIO[@]}" --path . -- "$@"
elif [[ "${1:-}" == "--bench" ]]; then
  "$GODOT" --audio-driver Dummy --path . -- --bench --out bench-out
  echo "Benchmark output: $PROJECT/bench-out"
else
  echo "Controls: LMB drag = rotate, wheel = zoom, click a slice country = actor panel, Space pause, 1/2/3 tempo, T E F M I G J areas (Esc map), F3 debug, F1 help; Esc = game menu (Quit game); --lang en|ru"
  "$GODOT" --path . -- "$@"
fi
