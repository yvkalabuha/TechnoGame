#!/usr/bin/env bash
# TASK-0034: C3 stand (smoke-t4s.md §2) under accelerated knowledge. Archive copy of the commit, original smoke-t4s C3
# (baseline, byte check), then the runner (levers c_n/k and S·k, k ∈ {1; 1.5; 2; 3}). Output: lab/results/c3accel/ (never committed).
# Usage: lab/runners/c3accel/run_c3accel.sh [commit] [seeds]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
COMMIT="${1:-HEAD}"
SEEDS="${2:-200}"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
RES="$ROOT/lab/results/c3accel"
REPO="$RES/repo"
rm -rf "$REPO" "$RES/runner"
mkdir -p "$REPO" "$RES/runner" "$RES/base" "$RES/out" "$RES/out2"
git -C "$ROOT" archive "$COMMIT" | tar -x -C "$REPO"
git -C "$ROOT" rev-parse "$COMMIT" > "$RES/core_commit.txt"
cp "$ROOT/lab/runners/c3accel/C3Accel.csproj" "$ROOT/lab/runners/c3accel/C3Accel.cs" "$RES/runner/"
"$DOTNET" run --project "$REPO/src/Singularity.Lab" -c Release -- smoke-t4s --mods "$REPO/mods/core" --out "$RES/base" --seeds "$SEEDS" --only c3 > "$RES/base/stdout.log"
"$DOTNET" build "$RES/runner/C3Accel.csproj" -c Release -p:RepoRoot="$REPO" -v q -nologo > "$RES/build.log"
EXE() { "$DOTNET" "$RES/runner/bin/Release/net10.0/C3Accel.dll" "$REPO/mods/core" "$@" "$SEEDS"; }
EXE "$RES/out"
EXE "$RES/out2"
sha256sum "$RES/base/c3_runs.csv" "$RES/out/c3accel_runs.csv" "$RES/out2/c3accel_runs.csv" | tee "$RES/hashes.txt"
